using H1Stats.Core.Configuration;
using H1Stats.Core.Models;

namespace H1Stats.Core.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task LogoutAsync(string username, CancellationToken ct = default);
}

public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> UpdateUserAsync(string username, UpdateUserRequest request, CancellationToken ct = default);
}

public interface IDashboardService
{
    Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default);
}

public interface IPhysicianStatisticsRepository
{
    Task<IReadOnlyList<string>> GetPhysiciansAsync(CancellationToken ct = default);
    Task<PhysicianStatisticsReport> GetReportAsync(DateOnly dateFrom, DateOnly dateTo, string? physician, CancellationToken ct = default);
}

public interface IDatabaseConnectionService
{
    DatabaseSettings GetSettings();
    Task UpdateSettingsAsync(DatabaseSettings settings, CancellationToken ct = default);
    Task<ConnectionTestResult> TestConnectionAsync(DatabaseSettings? settings = null, CancellationToken ct = default);
}
