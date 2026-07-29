using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

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
                Cm2Server = _settings.Cm2Server,
                Cm2Port = _settings.Cm2Port,
                MvfDatabase = _settings.MvfDatabase,
                AcusonDatabase = _settings.AcusonDatabase,
                Cm2Sid = _settings.Cm2Sid,
                Cm2Schema = _settings.Cm2Schema,
                Username = _settings.Username,
                Password = string.Empty
            };
        }
    }

    public Task UpdateSettingsAsync(DatabaseSettings settings, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _settings = MergeSettings(settings);
        }
        return Task.CompletedTask;
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(DatabaseSettings? settings = null, CancellationToken ct = default)
    {
        var results = await TestAllConnectionsAsync(settings, ct);
        var allConnected = results.All(r => r.Connected);
        var message = string.Join("; ", results.Select(r => $"{r.Database}: {r.Message}"));
        return new ConnectionTestResult(allConnected, message, DateTime.UtcNow);
    }

    public async Task<IReadOnlyList<DatabaseConnectionTestResult>> TestAllConnectionsAsync(
        DatabaseSettings? settings = null, CancellationToken ct = default)
    {
        DatabaseSettings cfg;
        lock (_lock)
        {
            cfg = settings is null ? _settings : MergeSettings(settings);
        }

        var sqlTests = new[]
        {
            TestSqlAsync($"{cfg.MvfDatabase} @ {cfg.Server}", cfg.MvfConnectionString, ct),
            TestSqlAsync($"{cfg.AcusonDatabase} @ {cfg.Server}", cfg.AcusonConnectionString, ct),
        };
        var oracleTest = TestOracleAsync(
            $"{cfg.Cm2Sid} @ {cfg.EffectiveCm2Server}:{cfg.Cm2Port}",
            cfg.Cm2OracleConnectionString,
            ct);

        var results = await Task.WhenAll(sqlTests.Append(oracleTest));
        return results;
    }

    private DatabaseSettings MergeSettings(DatabaseSettings incoming) =>
        new()
        {
            Server = incoming.Server,
            Cm2Server = incoming.Cm2Server,
            Cm2Port = incoming.Cm2Port,
            MvfDatabase = incoming.MvfDatabase,
            AcusonDatabase = incoming.AcusonDatabase,
            Cm2Sid = incoming.Cm2Sid,
            Cm2Schema = incoming.Cm2Schema,
            Username = incoming.Username,
            Password = string.IsNullOrWhiteSpace(incoming.Password) ? _settings.Password : incoming.Password,
        };

    private static async Task<DatabaseConnectionTestResult> TestSqlAsync(
        string name, string connectionString, CancellationToken ct)
    {
        try
        {
            var testConnectionString = connectionString.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase)
                ? connectionString
                : $"{connectionString};Connect Timeout=5";

            await using var conn = new SqlConnection(testConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = new SqlCommand("SELECT 1", conn);
            await cmd.ExecuteScalarAsync(ct);
            return new DatabaseConnectionTestResult(name, true, "Connected", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return new DatabaseConnectionTestResult(name, false, ex.Message, DateTime.UtcNow);
        }
    }

    private static async Task<DatabaseConnectionTestResult> TestOracleAsync(
        string name, string connectionString, CancellationToken ct)
    {
        try
        {
            await using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM DUAL";
            await cmd.ExecuteScalarAsync(ct);
            return new DatabaseConnectionTestResult(name, true, "Connected", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return new DatabaseConnectionTestResult(name, false, ex.Message, DateTime.UtcNow);
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
