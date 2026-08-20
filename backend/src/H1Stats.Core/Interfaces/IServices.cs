using H1Stats.Core.Configuration;
using H1Stats.Core.Models;

namespace H1Stats.Core.Interfaces;

public interface IAuthService
{
    Task<LoginResponse?> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task LogoutAsync(string username, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListEnabledUsernamesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetFavoriteReportsAsync(string username, CancellationToken ct = default);
    Task<(IReadOnlyList<string>? Paths, string? Error)> SetFavoriteReportsAsync(
        string username, IReadOnlyList<string> paths, CancellationToken ct = default);
}

public interface IUserAdminService
{
    Task<IReadOnlyList<UserDto>> ListUsersAsync(CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> CreateUserAsync(CreateUserRequest request, CancellationToken ct = default);
    Task<(UserDto? User, string? Error)> UpdateUserAsync(string username, UpdateUserRequest request, CancellationToken ct = default);
    Task<(bool Deleted, string? Error)> DeleteUserAsync(string username, CancellationToken ct = default);
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

public interface ICm2Repository
{
    Task<bool> TestConnectionAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CardiologistOption>> GetCardiologistsAsync(CancellationToken ct = default);
    Task<PhysicianStatisticsReport> GetReportingDrReportAsync(
        DateOnly dateFrom, DateOnly dateTo, string? cardiologist, CancellationToken ct = default);
    Task<IReadOnlyList<SiteOption>> GetSitesAsync(CancellationToken ct = default);
    Task<TopReferringDoctorsReport> GetTopReferringDoctorsAsync(
        DateOnly dateFrom, DateOnly dateTo, int topN, string? investigationType, string? site, CancellationToken ct = default);
    Task<TopReferringPracticesReport> GetTopReferringPracticesAsync(
        DateOnly dateFrom, DateOnly dateTo, int topN, string? site, CancellationToken ct = default);
    Task<ReferrersGrowthReport> GetReferrersGrowthByQuarterAsync(
        int year, int quarter, int topN, string? investigationType, string? sortBy, string? mode, string? site, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerSearchHit>> SearchReferringDoctorsAsync(string query, CancellationToken ct = default);
    Task<IReadOnlyList<ReferrerSearchHit>> SearchReferringPracticesAsync(string query, CancellationToken ct = default);
    Task<ReferrerHistoryReport> GetReferrerHistoryAsync(
        string mode, string entityId, string? investigationType, DateOnly dateFrom, DateOnly dateTo, string? grain = null, CancellationToken ct = default);
    Task<DrSummaryReport> GetDrSummaryAsync(int year, int month, CancellationToken ct = default);
    Task<DrSummaryReport> GetDrSummaryHistoryAsync(
        string? cardiologistId, int year, int month, CancellationToken ct = default);
}

public interface IDatabaseConnectionService
{
    DatabaseSettings GetSettings();
    Task UpdateSettingsAsync(DatabaseSettings settings, CancellationToken ct = default);
    Task<ConnectionTestResult> TestConnectionAsync(DatabaseSettings? settings = null, CancellationToken ct = default);
    Task<IReadOnlyList<DatabaseConnectionTestResult>> TestAllConnectionsAsync(
        DatabaseSettings? settings = null, CancellationToken ct = default);
}
