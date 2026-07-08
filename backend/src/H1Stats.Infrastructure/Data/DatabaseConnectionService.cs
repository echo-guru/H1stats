using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace H1Stats.Infrastructure.Data;

public class DatabaseConnectionService : IDatabaseConnectionService
{
    private readonly object _lock = new();
    private DatabaseSettings _settings;

    public DatabaseConnectionService(IOptions<DatabaseSettings> options)
    {
        _settings = options.Value;
    }

    public DatabaseSettings GetSettings()
    {
        lock (_lock)
        {
            return new DatabaseSettings
            {
                Server = _settings.Server,
                MvfDatabase = _settings.MvfDatabase,
                AcusonDatabase = _settings.AcusonDatabase,
                Username = _settings.Username,
                Password = string.Empty
            };
        }
    }

    public Task UpdateSettingsAsync(DatabaseSettings settings, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _settings = settings;
        }
        return Task.CompletedTask;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(DatabaseSettings? settings = null, CancellationToken ct = default)
    {
        var cfg = settings ?? _settings;
        try
        {
            await using var conn = new SqlConnection(cfg.MvfConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return new ConnectionTestResult(true, $"Connected to {cfg.Server}/{cfg.MvfDatabase}", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return new ConnectionTestResult(false, ex.Message, DateTime.UtcNow);
        }
    }
}

public class DashboardService : IDashboardService
{
    private readonly IPhysicianStatisticsRepository _repo;

    public DashboardService(IPhysicianStatisticsRepository repo) => _repo = repo;

    public async Task<DashboardSummary> GetSummaryAsync(CancellationToken ct = default)
    {
        try
        {
            var today = DateOnly.FromDateTime(DateTime.Today);
            var weekStart = today.AddDays(-(int)today.DayOfWeek + (int)DayOfWeek.Monday);
            if (today.DayOfWeek == DayOfWeek.Sunday) weekStart = weekStart.AddDays(-7);
            var monthStart = new DateOnly(today.Year, today.Month, 1);

            var todayReport = await _repo.GetReportAsync(today, today, null, ct);
            var weekReport = await _repo.GetReportAsync(weekStart, today, null, ct);
            var monthReport = await _repo.GetReportAsync(monthStart, today, null, ct);

            var topPhysicians = monthReport.Groups
                .Select(g => new NamedCount(g.Physician, g.Subtotal.Total))
                .OrderByDescending(x => x.Count)
                .Take(5)
                .ToList();

            var studyMix = monthReport.Groups
                .SelectMany(g => g.Rows)
                .GroupBy(r => r.StudyType)
                .Select(grp => new StudyMixItem(
                    grp.Key,
                    grp.Sum(r => r.Total),
                    grp.Sum(r => r.Inpatient),
                    grp.Sum(r => r.Outpatient)))
                .OrderByDescending(x => x.Count)
                .Take(8)
                .ToList();

            return new DashboardSummary(
                todayReport.GrandTotal.Total,
                weekReport.GrandTotal.Total,
                monthReport.GrandTotal.Total,
                topPhysicians,
                studyMix,
                []
            );
        }
        catch
        {
            return new DashboardSummary(0, 0, 0, [], [], []);
        }
    }
}
