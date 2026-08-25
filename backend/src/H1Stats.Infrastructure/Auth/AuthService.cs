using System.Text.Json;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;

namespace H1Stats.Infrastructure.Auth;

/// <summary>User store persisted to JSON so admin changes survive API restarts.</summary>
public class AuthService : IAuthService, IUserAdminService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly object _lock = new();
    private readonly string _usersFilePath;
    private readonly List<UserAccount> _users;

    public AuthService(string usersFilePath)
    {
        _usersFilePath = usersFilePath;
        _users = LoadOrSeed();
    }

    public Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        UserAccount? user;
        lock (_lock)
        {
            user = _users.FirstOrDefault(u =>
                u.Username.Equals(request.Username, StringComparison.OrdinalIgnoreCase));
        }

        if (user is null || user.IsDisabled)
            return Task.FromResult<LoginResponse?>(null);

        if (user.LockedUntil.HasValue && user.LockedUntil > DateTime.UtcNow)
            return Task.FromResult<LoginResponse?>(null);

        if (!PasswordHasher.Verify(request.Password, user.PasswordHash))
            return Task.FromResult<LoginResponse?>(null);

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        return Task.FromResult<LoginResponse?>(new LoginResponse(
            user.Username,
            user.IsAdmin,
            user.IsSuperAdmin,
            user.Modules,
            token
        ));
    }

    public Task LogoutAsync(string username, CancellationToken ct = default) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<string>> ListEnabledUsernamesAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<string>>(_users
                .Where(u => !u.IsDisabled)
                .Select(u => u.Username)
                .OrderBy(u => u, StringComparer.OrdinalIgnoreCase)
                .ToList());
        }
    }

    public Task<IReadOnlyList<string>> GetFavoriteReportsAsync(string username, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var user = _users.FirstOrDefault(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(NormalizeFavorites(user?.FavoriteReportPaths));
        }
    }

    public Task<(IReadOnlyList<string>? Paths, string? Error)> SetFavoriteReportsAsync(
        string username, IReadOnlyList<string> paths, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var index = _users.FindIndex(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
                return Task.FromResult<(IReadOnlyList<string>?, string?)>((null, "User not found"));

            var normalized = NormalizeFavorites(paths);
            _users[index] = _users[index] with { FavoriteReportPaths = normalized };
            PersistUnlocked();
            return Task.FromResult<(IReadOnlyList<string>?, string?)>((normalized, null));
        }
    }

    public Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<UserDto>>(_users
                .OrderBy(u => u.Username)
                .Select(ToDto)
                .ToList());
        }
    }

    public Task<(UserDto? User, string? Error)> CreateUserAsync(
        CreateUserRequest request, CancellationToken ct = default)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(username))
            return Task.FromResult<(UserDto?, string?)>((null, "Username is required"));

        if (string.IsNullOrWhiteSpace(request.Password))
            return Task.FromResult<(UserDto?, string?)>((null, "Password is required"));

        lock (_lock)
        {
            if (_users.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<(UserDto?, string?)>((null, "Username already exists"));

            var user = new UserAccount(
                username,
                PasswordHasher.Hash(request.Password),
                request.IsAdmin,
                IsDisabled: false,
                Modules: ModulesForRole(request.IsAdmin));

            _users.Add(user);
            PersistUnlocked();
            return Task.FromResult<(UserDto?, string?)>((ToDto(user), null));
        }
    }

    public Task<(UserDto? User, string? Error)> UpdateUserAsync(
        string username, UpdateUserRequest request, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var index = _users.FindIndex(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return Task.FromResult<(UserDto?, string?)>((null, "User not found"));

            var current = _users[index];
            var isAdmin = request.IsAdmin ?? current.IsAdmin;
            var isDisabled = request.IsDisabled ?? current.IsDisabled;

            var newUsername = string.IsNullOrWhiteSpace(request.NewUsername)
                ? current.Username
                : request.NewUsername.Trim();

            if (string.IsNullOrWhiteSpace(newUsername))
                return Task.FromResult<(UserDto?, string?)>((null, "Username is required"));

            if (!newUsername.Equals(current.Username, StringComparison.OrdinalIgnoreCase)
                && _users.Any(u => u.Username.Equals(newUsername, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<(UserDto?, string?)>((null, "Username already exists"));

            var wasEnabledAdmin = current.IsAdmin && !current.IsDisabled;
            var willBeEnabledAdmin = isAdmin && !isDisabled;
            if (wasEnabledAdmin && !willBeEnabledAdmin)
            {
                var otherEnabledAdmins = _users.Count(u =>
                    !u.Username.Equals(current.Username, StringComparison.OrdinalIgnoreCase)
                    && u.IsAdmin
                    && !u.IsDisabled);
                if (otherEnabledAdmins == 0)
                    return Task.FromResult<(UserDto?, string?)>((null, "At least one active admin is required"));
            }

            var passwordHash = string.IsNullOrWhiteSpace(request.Password)
                ? current.PasswordHash
                : PasswordHasher.Hash(request.Password);

            var updated = current with
            {
                Username = newUsername,
                IsAdmin = isAdmin,
                IsDisabled = isDisabled,
                Modules = ModulesForRole(isAdmin),
                PasswordHash = passwordHash,
                FavoriteReportPaths = current.FavoriteReportPaths,
                IsSuperAdmin = current.IsSuperAdmin,
            };

            _users[index] = updated;
            PersistUnlocked();
            return Task.FromResult<(UserDto?, string?)>((ToDto(updated), null));
        }
    }

    public Task<(bool Deleted, string? Error)> DeleteUserAsync(string username, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var index = _users.FindIndex(u =>
                u.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

            if (index < 0)
                return Task.FromResult<(bool, string?)>((false, "User not found"));

            var current = _users[index];
            if (current.IsAdmin && !current.IsDisabled)
            {
                var otherEnabledAdmins = _users.Count(u =>
                    !u.Username.Equals(current.Username, StringComparison.OrdinalIgnoreCase)
                    && u.IsAdmin
                    && !u.IsDisabled);
                if (otherEnabledAdmins == 0)
                    return Task.FromResult<(bool, string?)>((false, "Cannot delete the last active admin"));
            }

            _users.RemoveAt(index);
            PersistUnlocked();
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    private List<UserAccount> LoadOrSeed()
    {
        try
        {
            if (File.Exists(_usersFilePath))
            {
                var json = File.ReadAllText(_usersFilePath);
                var stored = JsonSerializer.Deserialize<List<StoredUser>>(json, JsonOptions);
                var loaded = stored?
                    .Where(u => !string.IsNullOrWhiteSpace(u.Username) && !string.IsNullOrWhiteSpace(u.PasswordHash))
                    .Select(ToAccount)
                    .ToList() ?? [];

                if (loaded.Count > 0)
                    return loaded;
            }
        }
        catch
        {
            // Fall through to seed if the file is missing or corrupt.
        }

        var seeded = DefaultUsers();
        Persist(seeded);
        return seeded;
    }

    private void PersistUnlocked() => Persist(_users);

    private void Persist(IReadOnlyList<UserAccount> users)
    {
        var directory = Path.GetDirectoryName(_usersFilePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var payload = users.Select(ToStored).ToList();
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var tempPath = _usersFilePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _usersFilePath, overwrite: true);
    }

    private static List<UserAccount> DefaultUsers() =>
    [
        new UserAccount(
            "admin",
            PasswordHasher.Hash("admin"),
            IsAdmin: true,
            IsDisabled: false,
            Modules: ModulesForRole(isAdmin: true)
        ),
        new UserAccount(
            "clinical",
            PasswordHasher.Hash("clinical"),
            IsAdmin: false,
            IsDisabled: false,
            Modules: ModulesForRole(isAdmin: false)
        ),
        new UserAccount(
            "tonyf",
            PasswordHasher.Hash("tony"),
            IsAdmin: true,
            IsDisabled: false,
            Modules: ModulesForRole(isAdmin: true),
            IsSuperAdmin: true
        ),
    ];

    private static StoredUser ToStored(UserAccount user) => new()
    {
        Username = user.Username,
        PasswordHash = user.PasswordHash,
        IsAdmin = user.IsAdmin,
        IsDisabled = user.IsDisabled,
        IsSuperAdmin = user.IsSuperAdmin,
        Modules = user.Modules.ToList(),
        FailedLoginAttempts = user.FailedLoginAttempts,
        LockedUntil = user.LockedUntil,
        FavoriteReportPaths = NormalizeFavorites(user.FavoriteReportPaths).ToList(),
    };

    private static UserAccount ToAccount(StoredUser user) => new(
        user.Username,
        user.PasswordHash,
        user.IsAdmin,
        user.IsDisabled,
        user.Modules.Count > 0 ? user.Modules : ModulesForRole(user.IsAdmin),
        user.FailedLoginAttempts,
        user.LockedUntil,
        NormalizeFavorites(user.FavoriteReportPaths),
        user.IsSuperAdmin
    );

    private static IReadOnlyList<string> NormalizeFavorites(IEnumerable<string>? paths) =>
        ClinicalReportCatalog.Normalize(paths);

    private static UserDto ToDto(UserAccount user) =>
        new(user.Username, user.IsAdmin, user.IsDisabled);

    private static IReadOnlyList<string> ModulesForRole(bool isAdmin) =>
        isAdmin
            ? [AppModules.Dashboard, AppModules.Clinical, AppModules.Administration]
            : [AppModules.Dashboard, AppModules.Clinical];

    private sealed class StoredUser
    {
        public string Username { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public bool IsAdmin { get; set; }
        public bool IsDisabled { get; set; }
        public bool IsSuperAdmin { get; set; }
        public List<string> Modules { get; set; } = [];
        public int FailedLoginAttempts { get; set; }
        public DateTime? LockedUntil { get; set; }
        public List<string> FavoriteReportPaths { get; set; } = [];
    }
}
