using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;

namespace H1Stats.Infrastructure.Auth;

/// <summary>In-memory user store for v1 — replace with SQL persistence later.</summary>
public class AuthService : IAuthService, IUserAdminService
{
    private readonly object _lock = new();
    private readonly List<UserAccount> _users =
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
            Modules: ModulesForRole(isAdmin: true)
        ),
    ];

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
            user.Modules,
            token
        ));
    }

    public Task LogoutAsync(string username, CancellationToken ct = default) =>
        Task.CompletedTask;

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

            var passwordHash = string.IsNullOrWhiteSpace(request.Password)
                ? current.PasswordHash
                : PasswordHasher.Hash(request.Password);

            var updated = current with
            {
                IsAdmin = isAdmin,
                Modules = ModulesForRole(isAdmin),
                PasswordHash = passwordHash,
            };

            _users[index] = updated;
            return Task.FromResult<(UserDto?, string?)>((ToDto(updated), null));
        }
    }

    private static UserDto ToDto(UserAccount user) =>
        new(user.Username, user.IsAdmin, user.IsDisabled);

    private static IReadOnlyList<string> ModulesForRole(bool isAdmin) =>
        isAdmin
            ? [AppModules.Dashboard, AppModules.Clinical, AppModules.Administration]
            : [AppModules.Dashboard, AppModules.Clinical];
}
