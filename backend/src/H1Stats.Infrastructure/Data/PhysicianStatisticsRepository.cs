using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using H1Stats.Core.Configuration;

namespace H1Stats.Infrastructure.Data;

/// <summary>
/// Read-only physician statistics from mvf.DOSR_STUDY joined with AcusonDB ward data.
/// SQL lives in repository — not in controllers.
/// </summary>
public class PhysicianStatisticsRepository : IPhysicianStatisticsRepository
{
    private readonly DatabaseSettings _settings;

    public PhysicianStatisticsRepository(IOptions<DatabaseSettings> options) =>
        _settings = options.Value;

    public async Task<IReadOnlyList<string>> GetPhysiciansAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT DISTINCT NULLIF(LTRIM(RTRIM(PHYSICIAN_READING_STUDY)), '') AS DiagnosingPhysician
            FROM dbo.DOSR_STUDY
            WHERE PHYSICIAN_READING_STUDY IS NOT NULL
              AND LTRIM(RTRIM(PHYSICIAN_READING_STUDY)) <> ''
            ORDER BY DiagnosingPhysician
            """;

        var list = new List<string>();
        await using var conn = new SqlConnection(_settings.MvfConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0))
                list.Add(reader.GetString(0));
        }
        return list;
    }

    public async Task<PhysicianStatisticsReport> GetReportAsync(
        DateOnly dateFrom, DateOnly dateTo, string? physician, CancellationToken ct = default)
    {
        var physicianFilter = string.IsNullOrWhiteSpace(physician)
            ? string.Empty
            : "AND NULLIF(LTRIM(RTRIM(d.PHYSICIAN_READING_STUDY)), '') = @Physician";

        var sql = string.Format(
            """
            SELECT
                NULLIF(LTRIM(RTRIM(d.PHYSICIAN_READING_STUDY)), '') AS DiagnosingPhysician,
                NULLIF(LTRIM(RTRIM(d.STUDY_DESCRIPTION)), '') AS StudyType,
                NULLIF(LTRIM(RTRIM(s.Custom1)), '') AS Ward,
                COUNT(*) AS StudyCount
            FROM [{0}].dbo.DOSR_STUDY d
            LEFT JOIN [{1}].dbo.Study s
                ON s.StudyID = d.STUDY_REF
            WHERE d.STUDY_DATE >= @DateFrom
              AND d.STUDY_DATE <= @DateTo
              {2}
            GROUP BY
                NULLIF(LTRIM(RTRIM(d.PHYSICIAN_READING_STUDY)), ''),
                NULLIF(LTRIM(RTRIM(d.STUDY_DESCRIPTION)), ''),
                NULLIF(LTRIM(RTRIM(s.Custom1)), '')
            ORDER BY DiagnosingPhysician, StudyType, Ward
            """,
            _settings.MvfDatabase,
            _settings.AcusonDatabase,
            physicianFilter);

        var rows = new List<(string Physician, string StudyType, string? Ward, int Count)>();
        await using var conn = new SqlConnection(_settings.MvfConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DateFrom", dateFrom.ToString("yyyyMMdd"));
        cmd.Parameters.AddWithValue("@DateTo", dateTo.ToString("yyyyMMdd"));
        if (!string.IsNullOrWhiteSpace(physician))
            cmd.Parameters.AddWithValue("@Physician", physician.Trim());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            rows.Add((
                reader.IsDBNull(0) ? "Unknown" : reader.GetString(0),
                reader.IsDBNull(1) ? "Unknown" : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.GetInt32(3)
            ));
        }

        var grouped = rows
            .GroupBy(r => r.Physician)
            .OrderBy(g => g.Key)
            .Select(physGroup =>
            {
                var statRows = physGroup
                    .GroupBy(r => r.StudyType)
                    .OrderBy(g => g.Key)
                    .Select(typeGroup =>
                    {
                        var total = typeGroup.Sum(r => r.Count);
                        var outpatient = typeGroup
                            .Where(r => WardClassification.IsOutpatient(r.Ward))
                            .Sum(r => r.Count);
                        var inpatient = total - outpatient;
                        return new PhysicianStatRow(
                            physGroup.Key, typeGroup.Key, total, outpatient, inpatient);
                    })
                    .ToList();

                var sub = new CountTriple(
                    statRows.Sum(r => r.Total),
                    statRows.Sum(r => r.Outpatient),
                    statRows.Sum(r => r.Inpatient));

                return new PhysicianGroup(physGroup.Key, statRows, sub);
            })
            .ToList();

        var grand = new CountTriple(
            grouped.Sum(g => g.Subtotal.Total),
            grouped.Sum(g => g.Subtotal.Outpatient),
            grouped.Sum(g => g.Subtotal.Inpatient));

        return new PhysicianStatisticsReport(grouped, grand);
    }
}
