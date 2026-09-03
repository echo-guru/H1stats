using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using System.Diagnostics;

namespace H1Stats.Infrastructure.Data;

/// <summary>
/// Read-only access to CM2 Oracle (HEARTS1ST.TEST) for reporting-dr and referring-dr reports.
/// </summary>
public class Cm2Repository : ICm2Repository
{
    private readonly DatabaseSettings _settings;
    private readonly IDatabaseConnectionService _db;

    public Cm2Repository(IOptions<DatabaseSettings> options, IDatabaseConnectionService db)
    {
        _settings = options.Value;
        _db = db;
    }

    private string Schema =>
        string.IsNullOrWhiteSpace(_settings.Cm2Schema) ? "HEARTS1ST" : _settings.Cm2Schema.Trim().ToUpperInvariant();

    public async Task<bool> TestConnectionAsync(CancellationToken ct = default)
    {
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM DUAL";
        await cmd.ExecuteScalarAsync(ct);
        return true;
    }

    public async Task<IReadOnlyList<CardiologistOption>> GetCardiologistsAsync(CancellationToken ct = default)
    {
        var sql = $"""
            SELECT DISTINCT TRIM(CARDIOLOGIST1) AS CardiologistId
            FROM {Schema}.TEST
            WHERE CARDIOLOGIST1 IS NOT NULL
              AND TRIM(CARDIOLOGIST1) IS NOT NULL
            """;

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            if (!reader.IsDBNull(0))
                ids.Add(reader.GetString(0).Trim());
        }

        return ids
            .Select(id => new CardiologistOption(id, Cm2CardiologistMapping.ResolveDisplayName(id)))
            .OrderBy(c => Cm2CardiologistMapping.IsUndefinedId(c.Id) ? 1 : 0)
            .ThenBy(c => c.DisplayName)
            .ToList();
    }

    public async Task<IReadOnlyList<SiteOption>> GetSitesAsync(CancellationToken ct = default) =>
        await PinkRecallWorklistStore.GetSitesAsync(_settings.Cm2OracleConnectionString, Schema, ct);

    public async Task<PhysicianStatisticsReport> GetReportingDrReportAsync(
        DateOnly dateFrom, DateOnly dateTo, string? cardiologist, CancellationToken ct = default)
    {
        var oracleId = Cm2CardiologistMapping.ResolveOracleId(cardiologist);
        var physicianFilter = string.IsNullOrWhiteSpace(oracleId)
            ? string.Empty
            : "AND TRIM(t.CARDIOLOGIST1) = :Physician";

        var sql = $"""
            SELECT
                TRIM(t.CARDIOLOGIST1) AS CardiologistId,
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS StudyType,
                NULLIF(TRIM(t.WARD), '') AS Ward,
                COUNT(DISTINCT t.TEST_RID) AS StudyCount
            FROM {Schema}.TEST t
            WHERE t.TEST_DATE >= :DateFrom
              AND t.TEST_DATE < :DateToExclusive
              AND t.TEST_REQUIRED IN (1, 2, 3, 4)
              {physicianFilter}
            GROUP BY
                TRIM(t.CARDIOLOGIST1),
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), ''),
                NULLIF(TRIM(t.WARD), '')
            ORDER BY CardiologistId, StudyType, Ward
            """;

        var rows = new List<(string Physician, string StudyType, string? Ward, int Count)>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (!string.IsNullOrWhiteSpace(oracleId))
            cmd.Parameters.Add("Physician", OracleDbType.Varchar2).Value = oracleId;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var rawId = reader.IsDBNull(0) ? null : reader.GetString(0);
            var rawType = reader.IsDBNull(1) ? null : reader.GetString(1);
            rows.Add((
                Cm2CardiologistMapping.ResolveDisplayName(rawId),
                Cm2InvestigationTypeMapping.ResolveDisplayName(rawType),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                Convert.ToInt32(reader.GetValue(3))
            ));
        }

        var grouped = rows
            .GroupBy(r => r.Physician)
            .Select(physGroup =>
            {
                var statRows = physGroup
                    .GroupBy(r => r.StudyType)
                    .Select(typeGroup =>
                    {
                        var total = typeGroup.Sum(r => r.Count);
                        var outpatient = typeGroup
                            .Where(r => WardClassification.IsCm2Outpatient(r.Ward))
                            .Sum(r => r.Count);
                        var inpatient = typeGroup
                            .Where(r => WardClassification.IsCm2Inpatient(r.Ward))
                            .Sum(r => r.Count);
                        return new PhysicianStatRow(
                            physGroup.Key, typeGroup.Key, total, outpatient, inpatient);
                    })
                    .OrderByDescending(r => r.Total)
                    .ThenBy(r => r.StudyType)
                    .ToList();

                var sub = new CountTriple(
                    statRows.Sum(r => r.Total),
                    statRows.Sum(r => r.Outpatient),
                    statRows.Sum(r => r.Inpatient));

                return new PhysicianGroup(physGroup.Key, statRows, sub);
            })
            .OrderBy(g => Cm2CardiologistMapping.IsUndefinedDisplayName(g.Physician) ? 1 : 0)
            .ThenByDescending(g => g.Subtotal.Total)
            .ThenBy(g => g.Physician)
            .ToList();

        var grand = new CountTriple(
            grouped.Sum(g => g.Subtotal.Total),
            grouped.Sum(g => g.Subtotal.Outpatient),
            grouped.Sum(g => g.Subtotal.Inpatient));

        return new PhysicianStatisticsReport(grouped, grand);
    }

    public async Task<TopReferringDoctorsReport> GetTopReferringDoctorsAsync(
        DateOnly dateFrom, DateOnly dateTo, int topN, string? investigationType, string? site, CancellationToken ct = default)
    {
        topN = Cm2TopN.Clamp(topN);
        var typeCode = Cm2InvestigationTypeMapping.ResolveReferringDoctorCode(investigationType);
        var typeFilter = string.IsNullOrWhiteSpace(typeCode)
            ? "AND t.TEST_REQUIRED IN (1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12)"
            : "AND t.TEST_REQUIRED = :InvestigationType";
        var siteCode = Cm2SiteMapping.ResolveCode(site);
        var siteFilter = SiteSql(siteCode);

        var sql = $"""
            SELECT
                CASE
                    WHEN d.CM2_DOCTOR_RID IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                    THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                    WHEN t.REFERRING_DOCTOR IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                    THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                    ELSE 'UNKNOWN'
                END AS PersonId,
                CASE
                    WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(cd.DOC_TITLE || ' ', '') ||
                            NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                            NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                            NVL(cd.DOC_LASTNAME, '')
                        )
                    WHEN d.DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(d.DOCTOR_TITLE || ' ', '') ||
                            NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                            NVL(d.DOCTOR_LASTNAME, '')
                        )
                    ELSE NULL
                END AS DisplayName,
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS InvestigationTypeCode,
                NULLIF(TRIM(t.WARD), '') AS Ward,
                COUNT(DISTINCT t.TEST_RID) AS StudyCount
            FROM {Schema}.TEST t
            LEFT JOIN {Schema}.DOCTOR d
              ON d.DOCTOR_RID = t.REFERRING_DOCTOR
            LEFT JOIN {Schema}.CM2_DOCTOR cd
              ON cd.CM2_DOCTOR_RID = d.CM2_DOCTOR_RID
            WHERE t.TEST_DATE >= :DateFrom
              AND t.TEST_DATE < :DateToExclusive
              {typeFilter}
              {siteFilter}
            GROUP BY
                CASE
                    WHEN d.CM2_DOCTOR_RID IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                    THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                    WHEN t.REFERRING_DOCTOR IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                    THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                    ELSE 'UNKNOWN'
                END,
                CASE
                    WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(cd.DOC_TITLE || ' ', '') ||
                            NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                            NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                            NVL(cd.DOC_LASTNAME, '')
                        )
                    WHEN d.DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(d.DOCTOR_TITLE || ' ', '') ||
                            NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                            NVL(d.DOCTOR_LASTNAME, '')
                        )
                    ELSE NULL
                END,
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), ''),
                NULLIF(TRIM(t.WARD), '')
            """;

        var rows = new List<(string PersonId, string DisplayName, string InvestigationType, string? Ward, int Count)>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (!string.IsNullOrWhiteSpace(typeCode))
            cmd.Parameters.Add("InvestigationType", OracleDbType.Decimal).Value = Convert.ToDecimal(typeCode);
        AddSiteParameter(cmd, siteCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var personId = reader.IsDBNull(0) ? "UNKNOWN" : reader.GetString(0);
            var displayName = reader.IsDBNull(1) ? null : reader.GetString(1)?.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = personId == "UNKNOWN" ? "Unknown" : personId;

            var rawType = reader.IsDBNull(2) ? null : reader.GetString(2);
            rows.Add((
                personId,
                displayName,
                Cm2InvestigationTypeMapping.ResolveDisplayName(rawType),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                Convert.ToInt32(reader.GetValue(4))
            ));
        }

        var personGroups = rows
            .GroupBy(r => r.PersonId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var displayName = g
                    .Select(r => r.DisplayName)
                    .Where(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith("CM2:", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase))
                    .DefaultIfEmpty(g.Key == "UNKNOWN" ? "Unknown" : g.First().DisplayName)
                    .First();
                var total = g.Sum(r => r.Count);
                return new { PersonId = g.Key, DisplayName = displayName, Total = total, Rows = g.ToList() };
            })
            .OrderByDescending(g => g.Total)
            .ThenBy(g => g.DisplayName)
            .Take(topN)
            .ToList();

        var groups = personGroups
            .Select((g, index) =>
            {
                var statRows = g.Rows
                    .GroupBy(r => r.InvestigationType)
                    .OrderBy(tg => tg.Key)
                    .Select(tg =>
                    {
                        var total = tg.Sum(r => r.Count);
                        var outpatient = tg.Where(r => WardClassification.IsCm2Outpatient(r.Ward)).Sum(r => r.Count);
                        var inpatient = tg.Where(r => WardClassification.IsCm2Inpatient(r.Ward)).Sum(r => r.Count);
                        return new ReferringDoctorStatRow(
                            g.PersonId, g.DisplayName, tg.Key, total, outpatient, inpatient);
                    })
                    .ToList();

                var sub = new CountTriple(
                    statRows.Sum(r => r.Total),
                    statRows.Sum(r => r.Outpatient),
                    statRows.Sum(r => r.Inpatient));

                return new ReferringDoctorGroup(index + 1, g.PersonId, g.DisplayName, statRows, sub);
            })
            .ToList();

        var grand = new CountTriple(
            groups.Sum(g => g.Subtotal.Total),
            groups.Sum(g => g.Subtotal.Outpatient),
            groups.Sum(g => g.Subtotal.Inpatient));

        return new TopReferringDoctorsReport(
            groups,
            grand,
            topN,
            string.IsNullOrWhiteSpace(typeCode)
                ? null
                : Cm2InvestigationTypeMapping.ResolveDisplayName(typeCode),
            string.IsNullOrWhiteSpace(siteCode)
                ? null
                : Cm2SiteMapping.ResolveDisplayName(siteCode));
    }

    public async Task<TopReferringPracticesReport> GetTopReferringPracticesAsync(
        DateOnly dateFrom, DateOnly dateTo, int topN, string? site, CancellationToken ct = default)
    {
        topN = Cm2TopN.Clamp(topN);
        var siteCode = Cm2SiteMapping.ResolveCode(site);
        var siteFilter = SiteSql(siteCode);
        const string validProviderRegex = "^[0-9]{6}[0-9A-Z]{2}$";

        // Practice → provider number (referring Dr at location) → investigation type.
        // Resting ECG excluded (same set as Top Referring Doctors).
        var sql = $"""
            SELECT
                CASE
                    WHEN d.CM2_PRACTICE_RID IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_PRACTICE_RID)) IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_PRACTICE_RID)) NOT IN ('0', '1')
                    THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_PRACTICE_RID))
                    ELSE 'UNRESOLVED'
                END AS PracticeId,
                CASE
                    WHEN cp.CM2_PRACTICE_RID IS NOT NULL THEN
                        NVL(NULLIF(TRIM(cp.PRAC_NAME), ''), NVL(NULLIF(TRIM(cp.PRAC_ALIAS), ''), 'Unnamed Practice'))
                    ELSE 'Unresolved Practice'
                END AS PracticeName,
                CASE
                    WHEN cp.CM2_PRACTICE_RID IS NOT NULL THEN NULLIF(TRIM(cp.PRAC_SA_SUBURB), '')
                    ELSE NULLIF(TRIM(d.DOCTOR_SUBURB), '')
                END AS Suburb,
                CASE
                    WHEN REGEXP_LIKE(
                        UPPER(REGEXP_REPLACE(TRIM(NVL(d.DOCTOR_PROV_ID, '')), '[^0-9A-Z]', '')),
                        '{validProviderRegex}')
                    THEN UPPER(REGEXP_REPLACE(TRIM(d.DOCTOR_PROV_ID), '[^0-9A-Z]', ''))
                    WHEN t.REFERRING_DOCTOR IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                    THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                    ELSE 'UNKNOWN'
                END AS ProviderKey,
                CASE
                    WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(cd.DOC_TITLE || ' ', '') ||
                            NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                            NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                            NVL(cd.DOC_LASTNAME, '')
                        )
                    WHEN d.DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(d.DOCTOR_TITLE || ' ', '') ||
                            NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                            NVL(d.DOCTOR_LASTNAME, '')
                        )
                    ELSE NULL
                END AS DoctorName,
                CASE
                    WHEN REGEXP_LIKE(
                        UPPER(REGEXP_REPLACE(TRIM(NVL(d.DOCTOR_PROV_ID, '')), '[^0-9A-Z]', '')),
                        '{validProviderRegex}')
                    THEN UPPER(REGEXP_REPLACE(TRIM(d.DOCTOR_PROV_ID), '[^0-9A-Z]', ''))
                    ELSE NULL
                END AS ProviderNumber,
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS InvestigationTypeCode,
                NULLIF(TRIM(t.WARD), '') AS Ward,
                COUNT(DISTINCT t.TEST_RID) AS StudyCount
            FROM {Schema}.TEST t
            LEFT JOIN {Schema}.DOCTOR d
              ON d.DOCTOR_RID = t.REFERRING_DOCTOR
            LEFT JOIN {Schema}.CM2_DOCTOR cd
              ON cd.CM2_DOCTOR_RID = d.CM2_DOCTOR_RID
            LEFT JOIN {Schema}.CM2_PRACTICE cp
              ON cp.CM2_PRACTICE_RID = d.CM2_PRACTICE_RID
            WHERE t.TEST_DATE >= :DateFrom
              AND t.TEST_DATE < :DateToExclusive
              AND t.TEST_REQUIRED IN (1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12)
              {siteFilter}
            GROUP BY
                CASE
                    WHEN d.CM2_PRACTICE_RID IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_PRACTICE_RID)) IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_PRACTICE_RID)) NOT IN ('0', '1')
                    THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_PRACTICE_RID))
                    ELSE 'UNRESOLVED'
                END,
                CASE
                    WHEN cp.CM2_PRACTICE_RID IS NOT NULL THEN
                        NVL(NULLIF(TRIM(cp.PRAC_NAME), ''), NVL(NULLIF(TRIM(cp.PRAC_ALIAS), ''), 'Unnamed Practice'))
                    ELSE 'Unresolved Practice'
                END,
                CASE
                    WHEN cp.CM2_PRACTICE_RID IS NOT NULL THEN NULLIF(TRIM(cp.PRAC_SA_SUBURB), '')
                    ELSE NULLIF(TRIM(d.DOCTOR_SUBURB), '')
                END,
                CASE
                    WHEN REGEXP_LIKE(
                        UPPER(REGEXP_REPLACE(TRIM(NVL(d.DOCTOR_PROV_ID, '')), '[^0-9A-Z]', '')),
                        '{validProviderRegex}')
                    THEN UPPER(REGEXP_REPLACE(TRIM(d.DOCTOR_PROV_ID), '[^0-9A-Z]', ''))
                    WHEN t.REFERRING_DOCTOR IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                    THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                    ELSE 'UNKNOWN'
                END,
                CASE
                    WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(cd.DOC_TITLE || ' ', '') ||
                            NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                            NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                            NVL(cd.DOC_LASTNAME, '')
                        )
                    WHEN d.DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(d.DOCTOR_TITLE || ' ', '') ||
                            NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                            NVL(d.DOCTOR_LASTNAME, '')
                        )
                    ELSE NULL
                END,
                CASE
                    WHEN REGEXP_LIKE(
                        UPPER(REGEXP_REPLACE(TRIM(NVL(d.DOCTOR_PROV_ID, '')), '[^0-9A-Z]', '')),
                        '{validProviderRegex}')
                    THEN UPPER(REGEXP_REPLACE(TRIM(d.DOCTOR_PROV_ID), '[^0-9A-Z]', ''))
                    ELSE NULL
                END,
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), ''),
                NULLIF(TRIM(t.WARD), '')
            """;

        var rows = new List<(
            string PracticeId, string PracticeName, string? Suburb,
            string ProviderKey, string DoctorName, string? ProviderNumber,
            string InvestigationType, string? Ward, int Count)>();

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);
        AddSiteParameter(cmd, siteCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var practiceId = reader.IsDBNull(0) ? "UNRESOLVED" : reader.GetString(0);
            var practiceName = reader.IsDBNull(1) ? "Unresolved Practice" : reader.GetString(1);
            var suburb = reader.IsDBNull(2) ? null : reader.GetString(2);
            var providerKey = reader.IsDBNull(3) ? "UNKNOWN" : reader.GetString(3);
            var doctorName = reader.IsDBNull(4) ? null : reader.GetString(4)?.Trim();
            var providerNumber = reader.IsDBNull(5) ? null : reader.GetString(5);
            var rawType = reader.IsDBNull(6) ? null : reader.GetString(6);
            var ward = reader.IsDBNull(7) ? null : reader.GetString(7);
            var count = Convert.ToInt32(reader.GetValue(8));

            if (string.IsNullOrWhiteSpace(doctorName))
                doctorName = providerNumber ?? providerKey;

            rows.Add((
                practiceId,
                practiceName,
                suburb,
                providerKey,
                doctorName,
                providerNumber,
                Cm2InvestigationTypeMapping.ResolveDisplayName(rawType),
                ward,
                count));
        }

        var practiceGroups = rows
            .GroupBy(r => r.PracticeId, StringComparer.OrdinalIgnoreCase)
            .Select(pg =>
            {
                var practiceName = pg
                    .Select(r => r.PracticeName)
                    .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n))
                    ?? "Unresolved Practice";
                var suburb = pg.Select(r => r.Suburb).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));
                var total = pg.Sum(r => r.Count);

                var doctors = pg
                    .GroupBy(r => r.ProviderKey, StringComparer.OrdinalIgnoreCase)
                    .Select(dg =>
                    {
                        var doctorName = dg
                            .Select(r => r.DoctorName)
                            .Where(n => !string.IsNullOrWhiteSpace(n))
                            .GroupBy(n => n!, StringComparer.OrdinalIgnoreCase)
                            .OrderByDescending(g => g.Count())
                            .Select(g => g.Key)
                            .FirstOrDefault()
                            ?? dg.Key;
                        var providerNumber = dg
                            .Select(r => r.ProviderNumber)
                            .FirstOrDefault(p => !string.IsNullOrWhiteSpace(p));

                        var testRows = dg
                            .GroupBy(r => r.InvestigationType)
                            .Select(tg =>
                            {
                                var t = tg.Sum(r => r.Count);
                                var op = tg.Where(r => WardClassification.IsCm2Outpatient(r.Ward)).Sum(r => r.Count);
                                var ip = tg.Where(r => WardClassification.IsCm2Inpatient(r.Ward)).Sum(r => r.Count);
                                return new ReferringPracticeTestRow(tg.Key, t, op, ip);
                            })
                            .OrderByDescending(r => r.Total)
                            .ThenBy(r => r.InvestigationType)
                            .ToList();

                        var doctorSub = new CountTriple(
                            testRows.Sum(r => r.Total),
                            testRows.Sum(r => r.Outpatient),
                            testRows.Sum(r => r.Inpatient));

                        return new ReferringPracticeDoctor(
                            dg.Key, doctorName, providerNumber, testRows, doctorSub);
                    })
                    .OrderByDescending(d => d.Subtotal.Total)
                    .ThenBy(d => d.DisplayName)
                    .ToList();

                var practiceSub = new CountTriple(
                    doctors.Sum(d => d.Subtotal.Total),
                    doctors.Sum(d => d.Subtotal.Outpatient),
                    doctors.Sum(d => d.Subtotal.Inpatient));

                return new
                {
                    PracticeId = pg.Key,
                    PracticeName = practiceName,
                    Suburb = suburb,
                    Total = total,
                    Doctors = doctors,
                    Subtotal = practiceSub,
                };
            })
            .OrderByDescending(p => p.Total)
            .ThenBy(p => p.PracticeName)
            .Take(topN)
            .ToList();

        var groups = practiceGroups
            .Select((p, index) => new ReferringPracticeGroup(
                index + 1,
                p.PracticeId,
                p.PracticeName,
                p.Suburb,
                p.Doctors,
                p.Subtotal))
            .ToList();

        var grand = new CountTriple(
            groups.Sum(g => g.Subtotal.Total),
            groups.Sum(g => g.Subtotal.Outpatient),
            groups.Sum(g => g.Subtotal.Inpatient));

        return new TopReferringPracticesReport(
            groups,
            grand,
            topN,
            string.IsNullOrWhiteSpace(siteCode)
                ? null
                : Cm2SiteMapping.ResolveDisplayName(siteCode));
    }

    public async Task<ReferrersGrowthReport> GetReferrersGrowthByQuarterAsync(
        int year, int quarter, int topN, string? investigationType, string? sortBy, string? mode, string? site, CancellationToken ct = default)
    {
        topN = Cm2TopN.Clamp(topN);
        var declineMode = string.Equals(mode, "decline", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "drop", StringComparison.OrdinalIgnoreCase);
        var modeKey = declineMode ? "decline" : "growth";
        var sortByPercent = string.Equals(sortBy, "percent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sortBy, "growthPercent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(sortBy, "growth%", StringComparison.OrdinalIgnoreCase);
        var sortKey = sortByPercent ? "percent" : "growth";
        var current = QuarterPeriod.Of(year, quarter);
        var previous = QuarterPeriod.Previous(year, quarter);
        var typeCode = Cm2InvestigationTypeMapping.ResolveReferringDoctorCode(investigationType);
        var typeFilter = string.IsNullOrWhiteSpace(typeCode)
            ? "AND t.TEST_REQUIRED IN (1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12)"
            : "AND t.TEST_REQUIRED = :InvestigationType";
        var siteCode = Cm2SiteMapping.ResolveCode(site);
        var siteFilter = SiteSql(siteCode);

        var sql = $"""
            SELECT
                CASE
                    WHEN d.CM2_DOCTOR_RID IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                    THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                    WHEN t.REFERRING_DOCTOR IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                    THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                    ELSE 'UNKNOWN'
                END AS PersonId,
                CASE
                    WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(cd.DOC_TITLE || ' ', '') ||
                            NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                            NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                            NVL(cd.DOC_LASTNAME, '')
                        )
                    WHEN d.DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(d.DOCTOR_TITLE || ' ', '') ||
                            NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                            NVL(d.DOCTOR_LASTNAME, '')
                        )
                    ELSE NULL
                END AS DisplayName,
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS InvestigationTypeCode,
                COUNT(DISTINCT CASE
                    WHEN t.TEST_DATE >= :PrevFrom AND t.TEST_DATE < :PrevToExclusive
                    THEN t.TEST_RID END) AS PreviousTotal,
                COUNT(DISTINCT CASE
                    WHEN t.TEST_DATE >= :CurrFrom AND t.TEST_DATE < :CurrToExclusive
                    THEN t.TEST_RID END) AS CurrentTotal
            FROM {Schema}.TEST t
            LEFT JOIN {Schema}.DOCTOR d
              ON d.DOCTOR_RID = t.REFERRING_DOCTOR
            LEFT JOIN {Schema}.CM2_DOCTOR cd
              ON cd.CM2_DOCTOR_RID = d.CM2_DOCTOR_RID
            WHERE t.TEST_DATE >= :PrevFrom
              AND t.TEST_DATE < :CurrToExclusive
              {typeFilter}
              {siteFilter}
            GROUP BY
                CASE
                    WHEN d.CM2_DOCTOR_RID IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                    THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                    WHEN t.REFERRING_DOCTOR IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                    THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                    ELSE 'UNKNOWN'
                END,
                CASE
                    WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(cd.DOC_TITLE || ' ', '') ||
                            NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                            NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                            NVL(cd.DOC_LASTNAME, '')
                        )
                    WHEN d.DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(d.DOCTOR_TITLE || ' ', '') ||
                            NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                            NVL(d.DOCTOR_LASTNAME, '')
                        )
                    ELSE NULL
                END,
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '')
            """;

        var raw = new List<(string PersonId, string DisplayName, string InvestigationType, int Previous, int Current)>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("PrevFrom", OracleDbType.Date).Value = previous.From.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("PrevToExclusive", OracleDbType.Date).Value = previous.ToExclusive.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("CurrFrom", OracleDbType.Date).Value = current.From.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("CurrToExclusive", OracleDbType.Date).Value = current.ToExclusive.ToDateTime(TimeOnly.MinValue);
        if (!string.IsNullOrWhiteSpace(typeCode))
            cmd.Parameters.Add("InvestigationType", OracleDbType.Decimal).Value = Convert.ToDecimal(typeCode);
        AddSiteParameter(cmd, siteCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var personId = reader.IsDBNull(0) ? "UNKNOWN" : reader.GetString(0);
            var displayName = reader.IsDBNull(1) ? null : reader.GetString(1)?.Trim();
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = personId == "UNKNOWN" ? "Unknown" : personId;

            var rawType = reader.IsDBNull(2) ? null : reader.GetString(2);
            var prev = Convert.ToInt32(reader.GetValue(3));
            var curr = Convert.ToInt32(reader.GetValue(4));
            if (prev == 0 && curr == 0)
                continue;

            raw.Add((
                personId,
                displayName,
                Cm2InvestigationTypeMapping.ResolveDisplayName(rawType),
                prev,
                curr));
        }

        // Referrers from the selected (current) quarter, ranked by growth vs previous quarter.
        var candidates = raw
            .GroupBy(r => r.PersonId, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var displayName = g
                    .Select(r => r.DisplayName)
                    .Where(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith("CM2:", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase))
                    .DefaultIfEmpty(g.First().DisplayName)
                    .First();
                var previousTotal = g.Sum(r => r.Previous);
                var currentTotal = g.Sum(r => r.Current);
                var growth = currentTotal - previousTotal;
                decimal? pct = previousTotal > 0
                    ? Math.Round((decimal)growth / previousTotal * 100m, 1)
                    : null;
                var percentSortKey = previousTotal == 0 && currentTotal > 0
                    ? decimal.MaxValue
                    : pct ?? decimal.MinValue;

                var testRows = g
                    .GroupBy(r => r.InvestigationType)
                    .Select(tg =>
                    {
                        var prevT = tg.Sum(r => r.Previous);
                        var currT = tg.Sum(r => r.Current);
                        var gT = currT - prevT;
                        decimal? pctT = prevT > 0
                            ? Math.Round((decimal)gT / prevT * 100m, 1)
                            : null;
                        return new ReferrerGrowthTestRow(tg.Key, prevT, currT, gT, pctT);
                    })
                    .OrderBy(t => declineMode ? t.Growth : -t.Growth)
                    .ThenByDescending(t => declineMode ? t.PreviousTotal : t.CurrentTotal)
                    .ThenBy(t => t.InvestigationType)
                    .ToList();

                return new
                {
                    PersonId = g.Key,
                    DisplayName = displayName,
                    PreviousTotal = previousTotal,
                    CurrentTotal = currentTotal,
                    Growth = growth,
                    GrowthPercent = pct,
                    PercentSortKey = percentSortKey,
                    TestRows = (IReadOnlyList<ReferrerGrowthTestRow>)testRows,
                };
            });

        var filtered = declineMode
            ? candidates.Where(r => r.PreviousTotal > 0 && r.Growth < 0)
            : candidates.Where(r => r.CurrentTotal > 0);

        var ordered = declineMode
            ? (sortByPercent
                ? filtered
                    .OrderBy(r => r.GrowthPercent ?? 0m)
                    .ThenBy(r => r.Growth)
                    .ThenByDescending(r => r.PreviousTotal)
                    .ThenBy(r => r.DisplayName)
                : filtered
                    .OrderBy(r => r.Growth)
                    .ThenByDescending(r => r.PreviousTotal)
                    .ThenBy(r => r.DisplayName))
            : (sortByPercent
                ? filtered
                    .OrderByDescending(r => r.PercentSortKey)
                    .ThenByDescending(r => r.Growth)
                    .ThenByDescending(r => r.CurrentTotal)
                    .ThenBy(r => r.DisplayName)
                : filtered
                    .OrderByDescending(r => r.Growth)
                    .ThenByDescending(r => r.CurrentTotal)
                    .ThenBy(r => r.DisplayName));

        var rows = ordered
            .Take(topN)
            .Select((r, index) => new ReferrerGrowthRow(
                index + 1,
                r.PersonId,
                r.DisplayName,
                r.PreviousTotal,
                r.CurrentTotal,
                r.Growth,
                r.GrowthPercent,
                r.TestRows))
            .ToList();

        return new ReferrersGrowthReport(
            current.Label,
            previous.Label,
            current.From,
            current.ToExclusive.AddDays(-1),
            previous.From,
            previous.ToExclusive.AddDays(-1),
            rows,
            topN,
            string.IsNullOrWhiteSpace(typeCode)
                ? null
                : Cm2InvestigationTypeMapping.ResolveDisplayName(typeCode),
            sortKey,
            modeKey,
            string.IsNullOrWhiteSpace(siteCode)
                ? null
                : Cm2SiteMapping.ResolveDisplayName(siteCode));
    }

    public async Task<IReadOnlyList<ReferrerSearchHit>> SearchReferringDoctorsAsync(
        string query, CancellationToken ct = default)
    {
        var like = LikeContains(query);
        if (like is null)
            return [];

        var sql = $"""
            SELECT PersonId, DisplayName, Hint
            FROM (
                SELECT
                    CASE
                        WHEN d.CM2_DOCTOR_RID IS NOT NULL
                             AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                             AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                        THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                        WHEN d.DOCTOR_RID IS NOT NULL
                             AND TRIM(TO_CHAR(d.DOCTOR_RID)) IS NOT NULL
                             AND TRIM(TO_CHAR(d.DOCTOR_RID)) NOT IN ('0', '1')
                        THEN 'LEGACY:' || TRIM(TO_CHAR(d.DOCTOR_RID))
                        ELSE NULL
                    END AS PersonId,
                    CASE
                        WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                            TRIM(
                                NVL(cd.DOC_TITLE || ' ', '') ||
                                NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                                NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                                NVL(cd.DOC_LASTNAME, '')
                            )
                        ELSE
                            TRIM(
                                NVL(d.DOCTOR_TITLE || ' ', '') ||
                                NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                                NVL(d.DOCTOR_LASTNAME, '')
                            )
                    END AS DisplayName,
                    NVL(
                        NULLIF(TRIM(cp.PRAC_SA_SUBURB), ''),
                        NVL(NULLIF(TRIM(cp.PRAC_NAME), ''), NULLIF(TRIM(d.DOCTOR_SUBURB), ''))
                    ) AS Hint
                FROM {Schema}.DOCTOR d
                LEFT JOIN {Schema}.CM2_DOCTOR cd
                  ON cd.CM2_DOCTOR_RID = d.CM2_DOCTOR_RID
                LEFT JOIN {Schema}.CM2_PRACTICE cp
                  ON cp.CM2_PRACTICE_RID = d.CM2_PRACTICE_RID
                WHERE UPPER(NVL(cd.DOC_LASTNAME, '') || ', ' || NVL(cd.DOC_FIRSTNAME, '')) LIKE :Query ESCAPE '!'
                   OR UPPER(NVL(cd.DOC_FIRSTNAME, '') || ' ' || NVL(cd.DOC_LASTNAME, '')) LIKE :Query ESCAPE '!'
                   OR UPPER(NVL(cd.DOC_LASTNAME, '')) LIKE :Query ESCAPE '!'
                   OR UPPER(NVL(d.DOCTOR_LASTNAME, '') || ', ' || NVL(d.DOCTOR_FIRSTNAME, '')) LIKE :Query ESCAPE '!'
                   OR UPPER(NVL(d.DOCTOR_FIRSTNAME, '') || ' ' || NVL(d.DOCTOR_LASTNAME, '')) LIKE :Query ESCAPE '!'
                   OR UPPER(NVL(d.DOCTOR_LASTNAME, '')) LIKE :Query ESCAPE '!'
            ) src
            WHERE PersonId IS NOT NULL
              AND ROWNUM <= 500
            """;

        var raw = new List<(string Id, string DisplayName, string? Hint)>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("Query", OracleDbType.Varchar2).Value = like;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var name = reader.IsDBNull(1) ? null : reader.GetString(1)?.Trim();
            var hint = reader.IsDBNull(2) ? null : reader.GetString(2)?.Trim();
            raw.Add((id, string.IsNullOrWhiteSpace(name) ? id : name, string.IsNullOrWhiteSpace(hint) ? null : hint));
        }

        return raw
            .GroupBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var name = g
                    .Select(r => r.DisplayName)
                    .Where(n => !string.IsNullOrWhiteSpace(n) && !n.StartsWith("CM2:", StringComparison.OrdinalIgnoreCase) && !n.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase))
                    .DefaultIfEmpty(g.First().DisplayName)
                    .First();
                var hint = g.Select(r => r.Hint).FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
                return new ReferrerSearchHit(g.Key, name, hint);
            })
            .OrderBy(h => h.DisplayName)
            .ThenBy(h => h.SecondaryText)
            .Take(40)
            .ToList();
    }

    public async Task<IReadOnlyList<ReferrerSearchHit>> SearchReferringPracticesAsync(
        string query, CancellationToken ct = default)
    {
        var like = LikeContains(query);
        if (like is null)
            return [];

        var sql = $"""
            SELECT Id, DisplayName, Suburb
            FROM (
                SELECT
                    'CM2:' || TRIM(TO_CHAR(cp.CM2_PRACTICE_RID)) AS Id,
                    NVL(NULLIF(TRIM(cp.PRAC_NAME), ''), NVL(NULLIF(TRIM(cp.PRAC_ALIAS), ''), 'Unnamed Practice')) AS DisplayName,
                    NULLIF(TRIM(cp.PRAC_SA_SUBURB), '') AS Suburb
                FROM {Schema}.CM2_PRACTICE cp
                WHERE cp.CM2_PRACTICE_RID IS NOT NULL
                  AND TRIM(TO_CHAR(cp.CM2_PRACTICE_RID)) IS NOT NULL
                  AND TRIM(TO_CHAR(cp.CM2_PRACTICE_RID)) NOT IN ('0', '1')
                  AND (
                      UPPER(NVL(cp.PRAC_NAME, '')) LIKE :Query ESCAPE '!'
                      OR UPPER(NVL(cp.PRAC_ALIAS, '')) LIKE :Query ESCAPE '!'
                      OR UPPER(NVL(cp.PRAC_SA_SUBURB, '')) LIKE :Query ESCAPE '!'
                  )
                ORDER BY DisplayName, Suburb
            )
            WHERE ROWNUM <= 40
            """;

        var list = new List<ReferrerSearchHit>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("Query", OracleDbType.Varchar2).Value = like;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var id = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(id))
                continue;
            var name = reader.IsDBNull(1) ? "Unnamed Practice" : reader.GetString(1);
            var suburb = reader.IsDBNull(2) ? null : reader.GetString(2);
            list.Add(new ReferrerSearchHit(id, name, string.IsNullOrWhiteSpace(suburb) ? null : suburb));
        }

        return list;
    }

    public async Task<PracticeDoctorsReport> GetPracticeDoctorsAsync(
        string practiceId, string? investigationType, CancellationToken ct = default)
    {
        if (!TryParseEntityId(practiceId, out var idKind, out var rid) || idKind != "CM2")
            throw new ArgumentException("Invalid id. Expected CM2:{rid}.", nameof(practiceId));

        var resolved = await ResolveEntityAsync(true, idKind, rid, ct);
        if (resolved is null)
            throw new KeyNotFoundException("Practice not found.");

        var today = DateOnly.FromDateTime(DateTime.Today);
        var yearTo = today;
        var yearFrom = today.AddYears(-1);
        var yearMonths = new HashSet<string>(
            MonthPeriod.MonthsInRange(yearFrom, yearTo).Select(m => m.YearMonth),
            StringComparer.Ordinal);
        var quarters = QuarterPeriod.LastN(20, today);
        var quarterFrom = quarters[0].From;
        var dateToExclusive = yearTo.AddDays(1);
        var typeCode = Cm2InvestigationTypeMapping.ResolveReferringDoctorCode(investigationType);
        var typeFilter = string.IsNullOrWhiteSpace(typeCode)
            ? "AND t.TEST_REQUIRED IN (1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12)"
            : "AND t.TEST_REQUIRED = :InvestigationType";

        const string personIdSql = """
            CASE
                WHEN d.CM2_DOCTOR_RID IS NOT NULL
                     AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                     AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                WHEN d.DOCTOR_RID IS NOT NULL
                     AND TRIM(TO_CHAR(d.DOCTOR_RID)) IS NOT NULL
                     AND TRIM(TO_CHAR(d.DOCTOR_RID)) NOT IN ('0', '1')
                THEN 'LEGACY:' || TRIM(TO_CHAR(d.DOCTOR_RID))
                ELSE NULL
            END
            """;
        const string doctorNameSql = """
            CASE
                WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                    TRIM(
                        NVL(cd.DOC_TITLE || ' ', '') ||
                        NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                        NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                        NVL(cd.DOC_LASTNAME, '')
                    )
                ELSE
                    TRIM(
                        NVL(d.DOCTOR_TITLE || ' ', '') ||
                        NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                        NVL(d.DOCTOR_LASTNAME, '')
                    )
            END
            """;

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var activity = new List<(string DoctorId, string YearMonth, string InvestigationType, string? Ward, int Count)>();

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT PersonId, DisplayName
                FROM (
                    SELECT
                        {personIdSql} AS PersonId,
                        {doctorNameSql} AS DisplayName
                    FROM {Schema}.DOCTOR d
                    LEFT JOIN {Schema}.CM2_DOCTOR cd
                      ON cd.CM2_DOCTOR_RID = d.CM2_DOCTOR_RID
                    WHERE d.CM2_PRACTICE_RID = :Rid
                )
                WHERE PersonId IS NOT NULL
                """;
            cmd.Parameters.Add("Rid", OracleDbType.Decimal).Value = Convert.ToDecimal(rid);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var name = reader.IsDBNull(1) ? null : reader.GetString(1)?.Trim();
                if (!string.IsNullOrWhiteSpace(name) && !names.ContainsKey(id))
                    names[id] = name;
                else
                    names.TryAdd(id, id);
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    {personIdSql} AS PersonId,
                    {doctorNameSql} AS DoctorName,
                    TO_CHAR(t.TEST_DATE, 'YYYY-MM') AS YearMonth,
                    NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS InvestigationTypeCode,
                    NULLIF(TRIM(t.WARD), '') AS Ward,
                    COUNT(DISTINCT t.TEST_RID) AS StudyCount
                FROM {Schema}.TEST t
                LEFT JOIN {Schema}.DOCTOR d
                  ON d.DOCTOR_RID = t.REFERRING_DOCTOR
                LEFT JOIN {Schema}.CM2_DOCTOR cd
                  ON cd.CM2_DOCTOR_RID = d.CM2_DOCTOR_RID
                WHERE t.TEST_DATE >= :DateFrom
                  AND t.TEST_DATE < :DateToExclusive
                  AND d.CM2_PRACTICE_RID = :Rid
                  {typeFilter}
                GROUP BY
                    {personIdSql},
                    {doctorNameSql},
                    TO_CHAR(t.TEST_DATE, 'YYYY-MM'),
                    NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), ''),
                    NULLIF(TRIM(t.WARD), '')
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = quarterFrom.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("Rid", OracleDbType.Decimal).Value = Convert.ToDecimal(rid);
            if (!string.IsNullOrWhiteSpace(typeCode))
                cmd.Parameters.Add("InvestigationType", OracleDbType.Decimal).Value = Convert.ToDecimal(typeCode);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var name = reader.IsDBNull(1) ? null : reader.GetString(1)?.Trim();
                var yearMonth = reader.IsDBNull(2) ? null : reader.GetString(2);
                if (string.IsNullOrWhiteSpace(yearMonth))
                    continue;
                if (!string.IsNullOrWhiteSpace(name))
                    names[id] = name;
                else
                    names.TryAdd(id, id);
                var rawType = reader.IsDBNull(3) ? null : reader.GetString(3);
                var ward = reader.IsDBNull(4) ? null : reader.GetString(4);
                activity.Add((
                    id,
                    yearMonth,
                    Cm2InvestigationTypeMapping.ResolveDisplayName(rawType),
                    ward,
                    Convert.ToInt32(reader.GetValue(5))));
            }
        }

        var byDoctorMonth = activity.ToLookup(r => r.DoctorId, StringComparer.OrdinalIgnoreCase);
        var doctors = names.Keys
            .Select(id =>
            {
                var monthRows = byDoctorMonth[id].ToList();
                var byQuarter = monthRows.ToLookup(r => YearMonthToQuarterKey(r.YearMonth), StringComparer.Ordinal);
                var quarterRows = quarters
                    .Select(q =>
                    {
                        var testRows = AggregateTestRows(
                            byQuarter[q.Key].Select(r => (r.InvestigationType, r.Ward, r.Count)));
                        return new PracticeDoctorQuarter(
                            q.Key,
                            q.Label,
                            testRows.Sum(t => t.Total),
                            testRows);
                    })
                    .ToList();
                var yearTotal = monthRows
                    .Where(r => yearMonths.Contains(r.YearMonth))
                    .Sum(r => r.Count);
                var displayName = names.TryGetValue(id, out var n) && !string.IsNullOrWhiteSpace(n) ? n : id;
                return new PracticeDoctorRow(id, displayName, yearTotal, quarterRows);
            })
            .OrderByDescending(d => d.YearTotal)
            .ThenBy(d => d.DisplayName)
            .ToList();

        return new PracticeDoctorsReport(
            practiceId.Trim(),
            resolved.Value.DisplayName,
            resolved.Value.Suburb,
            yearFrom,
            yearTo,
            quarterFrom,
            yearTo,
            doctors,
            string.IsNullOrWhiteSpace(typeCode)
                ? null
                : Cm2InvestigationTypeMapping.ResolveDisplayName(typeCode));
    }

    public async Task<ReferrerHistoryReport> GetReferrerHistoryAsync(
        string mode, string entityId, string? investigationType, DateOnly dateFrom, DateOnly dateTo, string? grain = null, CancellationToken ct = default)
    {
        var isPractice = string.Equals(mode, "practice", StringComparison.OrdinalIgnoreCase);
        var isDoctor = string.Equals(mode, "doctor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "dr", StringComparison.OrdinalIgnoreCase);
        if (!isPractice && !isDoctor)
            throw new ArgumentException("mode must be doctor or practice.", nameof(mode));
        if (dateFrom > dateTo)
            throw new ArgumentException("dateFrom must be on or before dateTo.");

        var modeKey = isPractice ? "practice" : "doctor";
        if (!TryParseEntityId(entityId, out var idKind, out var rid))
            throw new ArgumentException("Invalid id. Expected CM2:{rid} or LEGACY:{rid}.", nameof(entityId));
        if (isPractice && idKind != "CM2")
            throw new ArgumentException("Practice search requires a CM2 practice id.", nameof(entityId));

        var quarterly = string.Equals(grain?.Trim(), "quarter", StringComparison.OrdinalIgnoreCase);
        var grainKey = quarterly ? "quarter" : "month";
        var monthsWindow = MonthPeriod.MonthsInRange(dateFrom, dateTo);
        var dateToExclusive = dateTo.AddDays(1);
        var typeCode = Cm2InvestigationTypeMapping.ResolveReferringDoctorCode(investigationType);
        var typeFilter = string.IsNullOrWhiteSpace(typeCode)
            ? "AND t.TEST_REQUIRED IN (1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12)"
            : "AND t.TEST_REQUIRED = :InvestigationType";
        var entityFilter = isPractice
            ? "AND d.CM2_PRACTICE_RID = :Rid"
            : idKind == "CM2"
                ? "AND d.CM2_DOCTOR_RID = :Rid"
                : "AND t.REFERRING_DOCTOR = :Rid";

        var resolved = await ResolveEntityAsync(isPractice, idKind, rid, ct);
        if (resolved is null)
            throw new KeyNotFoundException(isPractice ? "Practice not found." : "Doctor not found.");

        var sql = $"""
            SELECT
                TO_CHAR(t.TEST_DATE, 'YYYY-MM') AS YearMonth,
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS InvestigationTypeCode,
                NULLIF(TRIM(t.WARD), '') AS Ward,
                CASE
                    WHEN cp.CM2_PRACTICE_RID IS NOT NULL THEN
                        NVL(NULLIF(TRIM(cp.PRAC_NAME), ''), NVL(NULLIF(TRIM(cp.PRAC_ALIAS), ''), 'Unnamed Practice'))
                    ELSE NULL
                END AS PracticeName,
                CASE
                    WHEN d.CM2_DOCTOR_RID IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                    THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                    WHEN t.REFERRING_DOCTOR IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                    THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                    ELSE 'UNKNOWN'
                END AS PersonId,
                CASE
                    WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(cd.DOC_TITLE || ' ', '') ||
                            NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                            NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                            NVL(cd.DOC_LASTNAME, '')
                        )
                    WHEN d.DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(d.DOCTOR_TITLE || ' ', '') ||
                            NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                            NVL(d.DOCTOR_LASTNAME, '')
                        )
                    ELSE NULL
                END AS DoctorName,
                COUNT(DISTINCT t.TEST_RID) AS StudyCount
            FROM {Schema}.TEST t
            LEFT JOIN {Schema}.DOCTOR d
              ON d.DOCTOR_RID = t.REFERRING_DOCTOR
            LEFT JOIN {Schema}.CM2_DOCTOR cd
              ON cd.CM2_DOCTOR_RID = d.CM2_DOCTOR_RID
            LEFT JOIN {Schema}.CM2_PRACTICE cp
              ON cp.CM2_PRACTICE_RID = d.CM2_PRACTICE_RID
            WHERE t.TEST_DATE >= :DateFrom
              AND t.TEST_DATE < :DateToExclusive
              {typeFilter}
              {entityFilter}
            GROUP BY
                TO_CHAR(t.TEST_DATE, 'YYYY-MM'),
                NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), ''),
                NULLIF(TRIM(t.WARD), ''),
                CASE
                    WHEN cp.CM2_PRACTICE_RID IS NOT NULL THEN
                        NVL(NULLIF(TRIM(cp.PRAC_NAME), ''), NVL(NULLIF(TRIM(cp.PRAC_ALIAS), ''), 'Unnamed Practice'))
                    ELSE NULL
                END,
                CASE
                    WHEN d.CM2_DOCTOR_RID IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                         AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                    THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                    WHEN t.REFERRING_DOCTOR IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                         AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                    THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                    ELSE 'UNKNOWN'
                END,
                CASE
                    WHEN cd.CM2_DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(cd.DOC_TITLE || ' ', '') ||
                            NVL(cd.DOC_FIRSTNAME || ' ', '') ||
                            NVL(cd.DOC_MIDDLENAME || ' ', '') ||
                            NVL(cd.DOC_LASTNAME, '')
                        )
                    WHEN d.DOCTOR_RID IS NOT NULL THEN
                        TRIM(
                            NVL(d.DOCTOR_TITLE || ' ', '') ||
                            NVL(d.DOCTOR_FIRSTNAME || ' ', '') ||
                            NVL(d.DOCTOR_LASTNAME, '')
                        )
                    ELSE NULL
                END
            """;

        var rows = new List<(string YearMonth, string InvestigationType, string? Ward, string? PracticeName, string DoctorId, string DoctorName, int Count)>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("Rid", OracleDbType.Decimal).Value = Convert.ToDecimal(rid);
        if (!string.IsNullOrWhiteSpace(typeCode))
            cmd.Parameters.Add("InvestigationType", OracleDbType.Decimal).Value = Convert.ToDecimal(typeCode);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var yearMonth = reader.IsDBNull(0) ? null : reader.GetString(0);
            if (string.IsNullOrWhiteSpace(yearMonth))
                continue;
            var rawType = reader.IsDBNull(1) ? null : reader.GetString(1);
            var ward = reader.IsDBNull(2) ? null : reader.GetString(2);
            var practiceName = reader.IsDBNull(3) ? null : reader.GetString(3)?.Trim();
            var doctorId = reader.IsDBNull(4) ? "UNKNOWN" : reader.GetString(4);
            var doctorName = reader.IsDBNull(5) ? null : reader.GetString(5)?.Trim();
            if (string.IsNullOrWhiteSpace(doctorName))
                doctorName = doctorId == "UNKNOWN" ? "Unknown" : doctorId;
            rows.Add((
                yearMonth,
                Cm2InvestigationTypeMapping.ResolveDisplayName(rawType),
                ward,
                string.IsNullOrWhiteSpace(practiceName) ? null : practiceName,
                string.IsNullOrWhiteSpace(doctorId) ? "UNKNOWN" : doctorId,
                doctorName,
                Convert.ToInt32(reader.GetValue(6))));
        }

        var byPeriod = rows.ToLookup(
            r => quarterly ? YearMonthToQuarterKey(r.YearMonth) : r.YearMonth,
            StringComparer.Ordinal);
        var periods = quarterly
            ? QuarterPeriod.QuartersInRange(dateFrom, dateTo)
                .Select(q => (q.Key, q.Label))
            : monthsWindow.Select(m => (Key: m.YearMonth, m.Label));
        var months = periods
            .Select(p =>
            {
                var periodRows = byPeriod[p.Key].ToList();
                var testRows = AggregateTestRows(periodRows);
                IReadOnlyList<ReferrerHistoryDoctorRow> doctorRows = isPractice
                    ? periodRows
                        .GroupBy(r => r.DoctorId, StringComparer.OrdinalIgnoreCase)
                        .Select(dg =>
                        {
                            var doctorTests = AggregateTestRows(dg);
                            var displayName = dg
                                .Select(r => r.DoctorName)
                                .Where(n => !string.IsNullOrWhiteSpace(n))
                                .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
                                .OrderByDescending(g => g.Count())
                                .Select(g => g.Key)
                                .FirstOrDefault()
                                ?? dg.Key;
                            return new ReferrerHistoryDoctorRow(
                                dg.Key,
                                displayName,
                                doctorTests.Sum(t => t.Total),
                                doctorTests.Sum(t => t.Outpatient),
                                doctorTests.Sum(t => t.Inpatient),
                                doctorTests);
                        })
                        .OrderByDescending(d => d.Total)
                        .ThenBy(d => d.DisplayName)
                        .ToList()
                    : [];

                return new ReferrerHistoryMonthRow(
                    p.Key,
                    p.Label,
                    testRows.Sum(t => t.Total),
                    testRows.Sum(t => t.Outpatient),
                    testRows.Sum(t => t.Inpatient),
                    testRows,
                    doctorRows);
            })
            .ToList();

        IReadOnlyList<string> combinedSites = isPractice
            ? Array.Empty<string>()
            : rows
                .Select(r => r.PracticeName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .Cast<string>()
                .ToList();

        var grand = new CountTriple(
            months.Sum(m => m.Total),
            months.Sum(m => m.Outpatient),
            months.Sum(m => m.Inpatient));

        return new ReferrerHistoryReport(
            modeKey,
            entityId.Trim(),
            resolved.Value.DisplayName,
            resolved.Value.Suburb,
            dateFrom,
            dateTo,
            combinedSites,
            months,
            grand,
            string.IsNullOrWhiteSpace(typeCode)
                ? null
                : Cm2InvestigationTypeMapping.ResolveDisplayName(typeCode),
            grainKey);
    }

    public async Task<ReferrerHistoryStudiesResult> GetReferrerHistoryStudiesAsync(
        string mode, string entityId, string investigationType, string periodKey,
        DateOnly dateFrom, DateOnly dateTo, string? grain = null, string? doctorId = null,
        CancellationToken ct = default)
    {
        var isPractice = string.Equals(mode, "practice", StringComparison.OrdinalIgnoreCase);
        var isDoctor = string.Equals(mode, "doctor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "dr", StringComparison.OrdinalIgnoreCase);
        if (!isPractice && !isDoctor)
            throw new ArgumentException("mode must be doctor or practice.", nameof(mode));
        if (dateFrom > dateTo)
            throw new ArgumentException("dateFrom must be on or before dateTo.");
        if (string.IsNullOrWhiteSpace(investigationType))
            throw new ArgumentException("investigationType is required.", nameof(investigationType));
        if (string.IsNullOrWhiteSpace(periodKey))
            throw new ArgumentException("period is required.", nameof(periodKey));
        if (!TryGetPeriodBounds(grain, periodKey, out var periodFrom, out var periodToExclusive, out var periodLabel))
            throw new ArgumentException("Invalid period. Expected YYYY-MM or YYYY-Qn.", nameof(periodKey));

        var modeKey = isPractice ? "practice" : "doctor";
        if (!TryParseEntityId(entityId, out var idKind, out var rid))
            throw new ArgumentException("Invalid id. Expected CM2:{rid} or LEGACY:{rid}.", nameof(entityId));
        if (isPractice && idKind != "CM2")
            throw new ArgumentException("Practice search requires a CM2 practice id.", nameof(entityId));

        var typeCode = Cm2InvestigationTypeMapping.ResolveReferringDoctorCode(investigationType);
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new ArgumentException("Unknown investigation type.", nameof(investigationType));

        var from = periodFrom > dateFrom ? periodFrom : dateFrom;
        var toExclusive = periodToExclusive < dateTo.AddDays(1) ? periodToExclusive : dateTo.AddDays(1);
        var entityFilter = isPractice
            ? "AND d.CM2_PRACTICE_RID = :Rid"
            : idKind == "CM2"
                ? "AND d.CM2_DOCTOR_RID = :Rid"
                : "AND t.REFERRING_DOCTOR = :Rid";

        string doctorFilter = "";
        string? doctorIdKey = null;
        if (isPractice && !string.IsNullOrWhiteSpace(doctorId))
        {
            doctorIdKey = doctorId.Trim();
            doctorFilter = """
                AND (
                    CASE
                        WHEN d.CM2_DOCTOR_RID IS NOT NULL
                             AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) IS NOT NULL
                             AND TRIM(TO_CHAR(d.CM2_DOCTOR_RID)) NOT IN ('0', '1')
                        THEN 'CM2:' || TRIM(TO_CHAR(d.CM2_DOCTOR_RID))
                        WHEN t.REFERRING_DOCTOR IS NOT NULL
                             AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) IS NOT NULL
                             AND TRIM(TO_CHAR(t.REFERRING_DOCTOR)) NOT IN ('0', '1')
                        THEN 'LEGACY:' || TRIM(TO_CHAR(t.REFERRING_DOCTOR))
                        ELSE 'UNKNOWN'
                    END
                ) = :DoctorId
                """;
        }

        var resolved = await ResolveEntityAsync(isPractice, idKind, rid, ct);
        if (resolved is null)
            throw new KeyNotFoundException(isPractice ? "Practice not found." : "Doctor not found.");

        var studies = new List<ReferrerHistoryStudyRow>();
        if (from < toExclusive)
        {
            var sql = $"""
                SELECT
                    NVL(
                        NULLIF(TRIM(TO_CHAR(p.PAT_ID)), ''),
                        NULLIF(TRIM(TO_CHAR(p.PATIENT_RID)), '')
                    ) AS PatientCmId,
                    TRIM(
                        NVL(p.PAT_TITLE || ' ', '') ||
                        NVL(p.PAT_FIRSTNAME || ' ', '') ||
                        NVL(p.PAT_MIDDLE_NAME || ' ', '') ||
                        NVL(UPPER(p.PAT_LASTNAME), '')
                    ) AS PatientName,
                    TO_CHAR(t.TEST_DATE, 'YYYY-MM-DD') AS StudyDate,
                    TRIM(TO_CHAR(t.TEST_RID)) AS TestRid,
                    NULLIF(TRIM(t.REPORT_PATH), '') AS ReportPath
                FROM {Schema}.TEST t
                LEFT JOIN {Schema}.PATIENT p
                  ON p.PATIENT_RID = t.PATIENT_RID
                LEFT JOIN {Schema}.DOCTOR d
                  ON d.DOCTOR_RID = t.REFERRING_DOCTOR
                WHERE t.TEST_DATE >= :DateFrom
                  AND t.TEST_DATE < :DateToExclusive
                  AND t.TEST_REQUIRED = :InvestigationType
                  {entityFilter}
                  {doctorFilter}
                ORDER BY t.TEST_DATE DESC, p.PAT_LASTNAME, p.PAT_FIRSTNAME, t.TEST_RID
                """;

            await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = sql;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = from.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = toExclusive.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("Rid", OracleDbType.Decimal).Value = Convert.ToDecimal(rid);
            cmd.Parameters.Add("InvestigationType", OracleDbType.Decimal).Value = Convert.ToDecimal(typeCode);
            if (doctorIdKey is not null)
                cmd.Parameters.Add("DoctorId", OracleDbType.Varchar2).Value = doctorIdKey;

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var cmId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                var name = reader.IsDBNull(1) ? "" : reader.GetString(1)?.Trim() ?? "";
                var dateStr = reader.IsDBNull(2) ? null : reader.GetString(2);
                if (string.IsNullOrWhiteSpace(dateStr) || !DateOnly.TryParse(dateStr, out var studyDate))
                    continue;
                var testRid = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
                var reportPath = reader.IsDBNull(4) ? null : reader.GetString(4);
                var hasReport = !string.IsNullOrWhiteSpace(reportPath);
                string? wordUri = null;
                if (hasReport && TryBuildReportFullPath(reportPath!, out var fullPath, out _))
                    wordUri = ToWordLaunchUri(fullPath);
                studies.Add(new ReferrerHistoryStudyRow(
                    cmId,
                    string.IsNullOrWhiteSpace(name) ? "Unknown" : name,
                    studyDate,
                    testRid,
                    hasReport,
                    wordUri));
            }
        }

        return new ReferrerHistoryStudiesResult(
            modeKey,
            entityId.Trim(),
            resolved.Value.DisplayName,
            periodKey.Trim(),
            periodLabel,
            Cm2InvestigationTypeMapping.ResolveDisplayName(typeCode),
            doctorIdKey,
            null,
            studies);
    }

    public async Task<StudyReportFile?> GetStudyReportFileAsync(string testRid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(testRid)
            || !decimal.TryParse(testRid.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            throw new ArgumentException("Invalid study id.", nameof(testRid));

        string? reportPath = null;
        await using (var conn = new OracleConnection(_settings.Cm2OracleConnectionString))
        {
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT NULLIF(TRIM(REPORT_PATH), '')
                FROM {Schema}.TEST
                WHERE TEST_RID = :Rid
                """;
            cmd.Parameters.Add("Rid", OracleDbType.Decimal).Value = rid;
            var value = await cmd.ExecuteScalarAsync(ct);
            if (value is null or DBNull)
                return null;
            reportPath = Convert.ToString(value)?.Trim();
        }

        if (string.IsNullOrWhiteSpace(reportPath))
            return null;
        if (!TryBuildReportFullPath(reportPath, out var fullPath, out var error))
            throw new InvalidOperationException(error);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Report file not found at {fullPath}.", fullPath);

        var fileName = Path.GetFileName(fullPath);
        var ext = Path.GetExtension(fullPath);
        var contentType = ext.Equals(".rtf", StringComparison.OrdinalIgnoreCase)
            ? "application/rtf"
            : ext.Equals(".doc", StringComparison.OrdinalIgnoreCase)
                ? "application/msword"
                : "application/octet-stream";
        return new StudyReportFile(fullPath, fileName, contentType);
    }

    public async Task OpenStudyReportAsync(string testRid, CancellationToken ct = default)
    {
        var file = await GetStudyReportFileAsync(testRid, ct)
            ?? throw new FileNotFoundException("No report on file for this study.");

        var word = ResolveWordExecutable();
        ProcessStartInfo psi;
        if (!string.IsNullOrWhiteSpace(word))
        {
            psi = new ProcessStartInfo
            {
                FileName = word,
                Arguments = "\"" + file.FullPath + "\"",
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(word) ?? "",
            };
        }
        else
        {
            psi = new ProcessStartInfo
            {
                FileName = file.FullPath,
                UseShellExecute = true,
            };
        }

        var proc = Process.Start(psi);
        if (proc is null && !string.IsNullOrWhiteSpace(word))
            throw new InvalidOperationException("Windows did not start Word. Check Database:Cm2WordPath.");
    }

    private string? ResolveWordExecutable()
    {
        var configured = _db.GetSettings().Cm2WordPath?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
            configured = _settings.Cm2WordPath?.Trim();
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        string[] candidates =
        [
            @"C:\Program Files (x86)\Microsoft Office\OFFICE11\Winword.exe",
            @"C:\Program Files\Microsoft Office\OFFICE11\Winword.exe",
            @"C:\Program Files (x86)\Microsoft Office\Office14\WINWORD.EXE",
            @"C:\Program Files\Microsoft Office\Office16\WINWORD.EXE",
            @"C:\Program Files (x86)\Microsoft Office\root\Office16\WINWORD.EXE",
            @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE",
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Resolves LETTER_PATH / REPORT_PATH under Cm2ReportRoot. Does not open or modify the file.
    /// </summary>
    private bool TryBuildReportFullPath(string reportPath, out string fullPath, out string error)
    {
        fullPath = "";
        var root = _db.GetSettings().Cm2ReportRoot?.Trim();
        if (string.IsNullOrWhiteSpace(root))
            root = _settings.Cm2ReportRoot?.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            error = "CM2 report folder is not configured (Database:Cm2ReportRoot). Set it to the CM2 mapped drive, typically T:\\.";
            return false;
        }

        var relative = reportPath.Replace('/', '\\').Trim().TrimStart('\\');
        if (string.IsNullOrWhiteSpace(relative)
            || relative.Contains("..", StringComparison.Ordinal)
            || Path.IsPathRooted(relative) && relative.Contains(':'))
        {
            error = "Invalid CM2 file path.";
            return false;
        }

        var rootFull = Path.GetFullPath(root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(rootFull, relative));
        if (!combined.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
        {
            error = "Invalid CM2 file path.";
            return false;
        }

        fullPath = combined;
        error = "";
        return true;
    }

    private static string? ToWordLaunchUri(string fullPath)
    {
        try
        {
            var normalised = fullPath.Replace('/', '\\').Trim();
            string fileUri;
            if (normalised.StartsWith(@"\\", StringComparison.Ordinal))
            {
                fileUri = "file:" + normalised.Replace('\\', '/');
            }
            else
            {
                fileUri = "file:///" + normalised.Replace('\\', '/');
            }

            return "ms-word:ofe|u|" + new Uri(fileUri).AbsoluteUri;
        }
        catch
        {
            return null;
        }
    }

    private static List<ReferrerHistoryTestRow> AggregateTestRows(
        IEnumerable<(string YearMonth, string InvestigationType, string? Ward, string? PracticeName, string DoctorId, string DoctorName, int Count)> rows) =>
        AggregateTestRows(rows.Select(r => (r.InvestigationType, r.Ward, r.Count)));

    private static List<ReferrerHistoryTestRow> AggregateTestRows(
        IEnumerable<(string InvestigationType, string? Ward, int Count)> rows) =>
        rows
            .GroupBy(r => r.InvestigationType)
            .Select(tg =>
            {
                var total = tg.Sum(r => r.Count);
                var outpatient = tg.Where(r => WardClassification.IsCm2Outpatient(r.Ward)).Sum(r => r.Count);
                var inpatient = tg.Where(r => WardClassification.IsCm2Inpatient(r.Ward)).Sum(r => r.Count);
                return new ReferrerHistoryTestRow(tg.Key, total, outpatient, inpatient);
            })
            .OrderByDescending(t => t.Total)
            .ThenBy(t => t.InvestigationType)
            .ToList();

    private async Task<(string DisplayName, string? Suburb)?> ResolveEntityAsync(
        bool isPractice, string idKind, string rid, CancellationToken ct)
    {
        string sql;
        if (isPractice)
        {
            sql = $"""
                SELECT
                    NVL(NULLIF(TRIM(PRAC_NAME), ''), NVL(NULLIF(TRIM(PRAC_ALIAS), ''), 'Unnamed Practice')) AS DisplayName,
                    NULLIF(TRIM(PRAC_SA_SUBURB), '') AS Suburb
                FROM {Schema}.CM2_PRACTICE
                WHERE CM2_PRACTICE_RID = :Rid
                """;
        }
        else if (idKind == "CM2")
        {
            sql = $"""
                SELECT
                    TRIM(
                        NVL(DOC_TITLE || ' ', '') ||
                        NVL(DOC_FIRSTNAME || ' ', '') ||
                        NVL(DOC_MIDDLENAME || ' ', '') ||
                        NVL(DOC_LASTNAME, '')
                    ) AS DisplayName,
                    CAST(NULL AS VARCHAR2(1)) AS Suburb
                FROM {Schema}.CM2_DOCTOR
                WHERE CM2_DOCTOR_RID = :Rid
                """;
        }
        else
        {
            sql = $"""
                SELECT
                    TRIM(
                        NVL(DOCTOR_TITLE || ' ', '') ||
                        NVL(DOCTOR_FIRSTNAME || ' ', '') ||
                        NVL(DOCTOR_LASTNAME, '')
                    ) AS DisplayName,
                    NULLIF(TRIM(DOCTOR_SUBURB), '') AS Suburb
                FROM {Schema}.DOCTOR
                WHERE DOCTOR_RID = :Rid
                """;
        }

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("Rid", OracleDbType.Decimal).Value = Convert.ToDecimal(rid);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var name = reader.IsDBNull(0) ? null : reader.GetString(0)?.Trim();
        var suburb = reader.IsDBNull(1) ? null : reader.GetString(1)?.Trim();
        if (string.IsNullOrWhiteSpace(name))
            name = isPractice ? "Unnamed Practice" : "Unknown";
        return (name, string.IsNullOrWhiteSpace(suburb) ? null : suburb);
    }

    private static bool TryGetPeriodBounds(
        string? grain, string periodKey, out DateOnly from, out DateOnly toExclusive, out string label)
    {
        from = default;
        toExclusive = default;
        label = periodKey;
        var key = periodKey.Trim();
        var quarterly = string.Equals(grain?.Trim(), "quarter", StringComparison.OrdinalIgnoreCase);
        if (quarterly)
        {
            var qIndex = key.IndexOf("-Q", StringComparison.OrdinalIgnoreCase);
            if (qIndex > 0
                && int.TryParse(key[..qIndex], out var year)
                && int.TryParse(key[(qIndex + 2)..], out var quarter)
                && quarter is >= 1 and <= 4)
            {
                var q = QuarterPeriod.Of(year, quarter);
                from = q.From;
                toExclusive = q.ToExclusive;
                label = q.Label;
                return true;
            }
            return false;
        }

        if (key.Length == 7 && key[4] == '-'
            && int.TryParse(key.AsSpan(0, 4), out var y)
            && int.TryParse(key.AsSpan(5, 2), out var m)
            && m is >= 1 and <= 12)
        {
            var period = MonthPeriod.Of(y, m);
            from = period.From;
            toExclusive = period.ToExclusive;
            label = period.Label;
            return true;
        }

        return false;
    }

    private static string YearMonthToQuarterKey(string yearMonth)
    {
        if (yearMonth.Length < 7
            || !int.TryParse(yearMonth.AsSpan(5, 2), out var month)
            || month is < 1 or > 12)
            return yearMonth;
        var quarter = (month - 1) / 3 + 1;
        return $"{yearMonth[..4]}-Q{quarter}";
    }

    private static bool TryParseEntityId(string? id, out string kind, out string rid)
    {
        kind = "";
        rid = "";
        if (string.IsNullOrWhiteSpace(id))
            return false;

        var s = id.Trim();
        string prefix;
        if (s.StartsWith("CM2:", StringComparison.OrdinalIgnoreCase))
            prefix = "CM2:";
        else if (s.StartsWith("LEGACY:", StringComparison.OrdinalIgnoreCase))
            prefix = "LEGACY:";
        else
            return false;

        kind = prefix[..^1].ToUpperInvariant();
        rid = s[prefix.Length..].Trim();
        if (string.IsNullOrWhiteSpace(rid) || rid is "0" or "1")
            return false;
        return decimal.TryParse(rid, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    public async Task<DrSummaryReport> GetDrSummaryAsync(
        int year, int month, CancellationToken ct = default)
    {
        var period = MonthPeriod.Of(year, month);
        var dateFrom = period.From.ToDateTime(TimeOnly.MinValue);
        var dateToExclusive = period.ToExclusive.ToDateTime(TimeOnly.MinValue);

        var testRows = new List<(string Id, string? TypeCode, string? Ward, int Count)>();
        var consultRows = new List<(string Id, int Count)>();
        var dccsByAppointment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TRIM(TO_CHAR(t.CARDIOLOGIST1)) AS CardiologistId,
                    NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS StudyType,
                    NULLIF(TRIM(TO_CHAR(t.WARD)), '') AS Ward,
                    COUNT(DISTINCT t.TEST_RID) AS StudyCount
                FROM {Schema}.TEST t
                WHERE t.TEST_DATE >= :DateFrom
                  AND t.TEST_DATE < :DateToExclusive
                  AND t.TEST_REQUIRED IN (1, 2, 3, 4)
                GROUP BY
                    TRIM(TO_CHAR(t.CARDIOLOGIST1)),
                    NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), ''),
                    NULLIF(TRIM(TO_CHAR(t.WARD)), '')
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                testRows.Add((
                    reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    Convert.ToInt32(reader.GetValue(3))));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TRIM(TO_CHAR(c.USER_LIST_RID)) AS CardiologistId,
                    COUNT(*) AS ConsultCount
                FROM {Schema}.CONSULT c
                JOIN {Schema}.LETTERS l
                  ON l.LETTERS_RID = c.LETTERS_RID
                WHERE l.LETTER_DATE >= :DateFrom
                  AND l.LETTER_DATE < :DateToExclusive
                GROUP BY TRIM(TO_CHAR(c.USER_LIST_RID))
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                consultRows.Add((
                    reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                    Convert.ToInt32(reader.GetValue(1))));
            }
        }

        var dccsIds = string.Join(", ", Cm2DrSummaryMapping.DccsOccServiceIds);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TRIM(TO_CHAR(a.SCHEDULE_APPOINTMENT_RID)) AS AppointmentId,
                    TRIM(TO_CHAR(t.CARDIOLOGIST1)) AS CardiologistId
                FROM {Schema}.SCHEDULE_APPOINTMENT a
                JOIN {Schema}.SCHEDULE_OCC_SERVICE o
                  ON o.SCHEDULE_OCC_SERVICE_RID = a.SCHEDULE_OCC_SERVICE_RID
                LEFT JOIN {Schema}.TEST t
                  ON t.PATIENT_RID = a.PATIENT_RID
                 AND t.TEST_DATE >= TRUNC(a.APPOINTMENT_DATE)
                 AND t.TEST_DATE < TRUNC(a.APPOINTMENT_DATE) + 1
                 AND t.CARDIOLOGIST1 IS NOT NULL
                 AND t.CARDIOLOGIST1 NOT IN (0, 1)
                WHERE a.APPOINTMENT_DATE >= :DateFrom
                  AND a.APPOINTMENT_DATE < :DateToExclusive
                  AND o.SCHEDULE_OCC_SERVICE_RID IN ({dccsIds})
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var appointmentId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(appointmentId))
                    continue;
                var cardiologistId = reader.IsDBNull(1) ? null : reader.GetString(1).Trim();
                if (!dccsByAppointment.TryGetValue(appointmentId, out var existing)
                    || string.IsNullOrWhiteSpace(existing))
                {
                    dccsByAppointment[appointmentId] = string.IsNullOrWhiteSpace(cardiologistId)
                        ? null
                        : cardiologistId;
                }
            }
        }

        var columnIds = new List<string>(Cm2CardiologistMapping.SummaryColumnIds);
        var seen = new HashSet<string>(columnIds, StringComparer.OrdinalIgnoreCase);

        void Consider(string? rawId)
        {
            var id = Cm2CardiologistMapping.NormalizeId(rawId);
            if (string.IsNullOrWhiteSpace(id)
                || Cm2CardiologistMapping.IsUndefinedId(id)
                || Cm2DrSummaryMapping.IsExcludedCardiologist(id))
                return;
            if (seen.Add(id))
                columnIds.Add(id);
        }

        foreach (var row in testRows)
            Consider(row.Id);
        foreach (var row in consultRows)
            Consider(row.Id);
        foreach (var id in dccsByAppointment.Values)
            Consider(id);

        var extras = columnIds
            .Skip(Cm2CardiologistMapping.SummaryColumnIds.Count)
            .OrderBy(id => Cm2CardiologistMapping.ResolveDisplayName(id), StringComparer.OrdinalIgnoreCase)
            .ToList();
        columnIds = [.. Cm2CardiologistMapping.SummaryColumnIds, .. extras];

        var indexById = columnIds
            .Select((id, i) => (id, i))
            .ToDictionary(x => x.id, x => x.i, StringComparer.OrdinalIgnoreCase);

        int Col(string? rawId)
        {
            var id = Cm2CardiologistMapping.NormalizeId(rawId);
            return indexById.TryGetValue(id, out var i) ? i : -1;
        }

        var acc = new DrSummaryAccumulator(columnIds.Count);
        foreach (var (id, typeCode, ward, count) in testRows)
            acc.AddTest(Col(id), typeCode, ward, count);
        foreach (var (id, count) in consultRows)
            acc.AddConsult(Col(id), count);
        foreach (var cardiologistId in dccsByAppointment.Values)
            acc.AddDccs(Col(cardiologistId));

        var columns = columnIds
            .Select(id => new DrSummaryColumn(
                id,
                Cm2CardiologistMapping.ResolveInitials(id),
                Cm2CardiologistMapping.ResolveDisplayName(id)))
            .ToList();

        return new DrSummaryReport(
            period.Year,
            period.Month,
            period.Label,
            period.From,
            period.ToExclusive,
            columns,
            acc.ToRows(),
            acc.UnassignedDccs);
    }

    public async Task<JamesReport> GetJamesReportAsync(CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        var currentYear = today.Year;
        var dateFrom = new DateOnly(currentYear - 2, 1, 1);
        var dateToExclusive = today.AddDays(1);
        var dateFromDt = dateFrom.ToDateTime(TimeOnly.MinValue);
        var dateToExclusiveDt = dateToExclusive.ToDateTime(TimeOnly.MinValue);

        // siteId → (year, quarter) → (echo, seDse, consults)
        var counts = new Dictionary<string, Dictionary<(int Year, int Quarter), (int Echo, int SeDse, int Consults)>>(
            StringComparer.OrdinalIgnoreCase);

        void Ensure(string siteId, int year, int quarter)
        {
            if (!counts.TryGetValue(siteId, out var byQ))
            {
                byQ = new Dictionary<(int, int), (int, int, int)>();
                counts[siteId] = byQ;
            }
            if (!byQ.ContainsKey((year, quarter)))
                byQ[(year, quarter)] = (0, 0, 0);
        }

        void AddEchoSe(string siteId, int year, int quarter, int echo, int seDse)
        {
            Ensure(siteId, year, quarter);
            var cur = counts[siteId][(year, quarter)];
            counts[siteId][(year, quarter)] = (cur.Echo + echo, cur.SeDse + seDse, cur.Consults);
        }

        void AddConsults(string siteId, int year, int quarter, int consults)
        {
            Ensure(siteId, year, quarter);
            var cur = counts[siteId][(year, quarter)];
            counts[siteId][(year, quarter)] = (cur.Echo, cur.SeDse, cur.Consults + consults);
        }

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);

        // Echo (TTE=1) and SE/DSE (2+3) from completed studies by TEST.SITE.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TRIM(TO_CHAR(t.SITE)) AS SiteId,
                    EXTRACT(YEAR FROM t.TEST_DATE) AS Yr,
                    TO_NUMBER(TO_CHAR(t.TEST_DATE, 'Q')) AS Qtr,
                    COUNT(DISTINCT CASE WHEN t.TEST_REQUIRED = 1 THEN t.TEST_RID END) AS EchoCount,
                    COUNT(DISTINCT CASE WHEN t.TEST_REQUIRED IN (2, 3) THEN t.TEST_RID END) AS SeDseCount
                FROM {Schema}.TEST t
                WHERE t.TEST_DATE >= :DateFrom
                  AND t.TEST_DATE < :DateToExclusive
                  AND t.TEST_REQUIRED IN (1, 2, 3)
                  AND t.SITE IN (1, 2, 3)
                GROUP BY
                    TRIM(TO_CHAR(t.SITE)),
                    EXTRACT(YEAR FROM t.TEST_DATE),
                    TO_NUMBER(TO_CHAR(t.TEST_DATE, 'Q'))
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFromDt;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusiveDt;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var siteId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(siteId)) continue;
                var year = Convert.ToInt32(reader.GetValue(1));
                var quarter = Convert.ToInt32(reader.GetValue(2));
                var echo = Convert.ToInt32(reader.GetValue(3));
                var seDse = Convert.ToInt32(reader.GetValue(4));
                AddEchoSe(siteId, year, quarter, echo, seDse);
            }
        }

        // Consults by campus: diary bookings (same title patterns as Research / GPH SMS).
        // CONSULT/LETTERS have no SITE; room → SCHEDULE_SITE is the campus grain.
        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TRIM(TO_CHAR(ST.SCHEDULE_SITE_RID)) AS SiteId,
                    EXTRACT(YEAR FROM A.APPOINTMENT_DATE) AS Yr,
                    TO_NUMBER(TO_CHAR(A.APPOINTMENT_DATE, 'Q')) AS Qtr,
                    COUNT(*) AS ConsultCount
                FROM {Schema}.SCHEDULE_APPOINTMENT A
                JOIN {Schema}.PATIENT P
                  ON P.PATIENT_RID = A.PATIENT_RID
                LEFT JOIN {Schema}.SCHEDULE_OCC_SERVICE S
                  ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
                LEFT JOIN {Schema}.SCHEDULE_ROOM R
                  ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
                LEFT JOIN {Schema}.SCHEDULE_SITE ST
                  ON ST.SCHEDULE_SITE_RID = R.SCHEDULE_SITE_RID
                WHERE A.APPOINTMENT_DATE >= :DateFrom
                  AND A.APPOINTMENT_DATE < :DateToExclusive
                  AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
                  AND ST.SCHEDULE_SITE_RID IN (1, 2, 3)
                  AND (
                        REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'INITIAL[[:space:]-]*CONSULT')
                     OR REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'FOLLOW[[:space:]-]*UP[[:space:]-]*CONSULT')
                     OR REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'FOLLOW[[:space:]-]*UP[[:space:]-]*-[[:space:]-]*NEW[[:space:]-]*REFERRAL')
                     OR UPPER(NVL(S.OCC_SERVICE_TITLE, '')) LIKE '%CONSULT%'
                     OR UPPER(NVL(S.OCC_SERVICE_TITLE, '')) LIKE '%NEW REFERRAL%'
                      )
                GROUP BY
                    TRIM(TO_CHAR(ST.SCHEDULE_SITE_RID)),
                    EXTRACT(YEAR FROM A.APPOINTMENT_DATE),
                    TO_NUMBER(TO_CHAR(A.APPOINTMENT_DATE, 'Q'))
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFromDt;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusiveDt;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var siteId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(siteId)) continue;
                var year = Convert.ToInt32(reader.GetValue(1));
                var quarter = Convert.ToInt32(reader.GetValue(2));
                var consults = Convert.ToInt32(reader.GetValue(3));
                AddConsults(siteId, year, quarter, consults);
            }
        }

        var siteSections = new List<JamesReportSiteSection>();
        foreach (var (code, displayName) in Cm2SiteMapping.All)
        {
            var years = new List<JamesReportYearBlock>();
            for (var year = currentYear - 2; year <= currentYear; year++)
            {
                var quarters = new List<JamesReportQuarterRow>();
                for (var q = 1; q <= 4; q++)
                {
                    var qPeriod = QuarterPeriod.Of(year, q);
                    // Only include quarters that have started (current year may be partial).
                    if (qPeriod.From >= dateToExclusive)
                        break;

                    counts.TryGetValue(code, out var byQ);
                    byQ ??= new Dictionary<(int, int), (int, int, int)>();
                    byQ.TryGetValue((year, q), out var cell);
                    quarters.Add(new JamesReportQuarterRow(
                        year,
                        q,
                        $"Qtr{q}",
                        cell.Echo,
                        cell.SeDse,
                        cell.Consults));
                }

                if (quarters.Count == 0)
                    continue;

                years.Add(new JamesReportYearBlock(
                    year,
                    Partial: year == currentYear,
                    quarters,
                    quarters.Sum(r => r.Echo),
                    quarters.Sum(r => r.SeDse),
                    quarters.Sum(r => r.Consults)));
            }

            siteSections.Add(new JamesReportSiteSection(code, displayName, years));
        }

        return new JamesReport(dateFrom, dateToExclusive, currentYear, siteSections);
    }

    public async Task<DrSummaryReport> GetDrSummaryHistoryAsync(
        string? cardiologistId, int year, int month, CancellationToken ct = default)
    {
        if (IsAllDoctorsHistory(cardiologistId))
            return await GetAllDoctorsDrSummaryHistoryAsync(year, month, ct);

        var id = Cm2CardiologistMapping.NormalizeId(cardiologistId);
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Cardiologist is required.", nameof(cardiologistId));

        var asOf = new DateOnly(year, month, 1);
        var window = MonthPeriod.Prior12(asOf);
        var dateFrom = window.From.ToDateTime(TimeOnly.MinValue);
        var dateToExclusive = window.ToExclusive.ToDateTime(TimeOnly.MinValue);
        var monthKeys = window.Months.Select(m => m.YearMonth).ToList();
        var monthIndex = monthKeys
            .Select((key, i) => (key, i))
            .ToDictionary(x => x.key, x => x.i, StringComparer.OrdinalIgnoreCase);

        int MonthCol(string? yearMonth) =>
            yearMonth is not null && monthIndex.TryGetValue(yearMonth, out var i) ? i : -1;

        var acc = new DrSummaryAccumulator(monthKeys.Count);
        var dccsIds = string.Join(", ", Cm2DrSummaryMapping.DccsOccServiceIds);

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TO_CHAR(t.TEST_DATE, 'YYYY-MM') AS YearMonth,
                    NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS StudyType,
                    NULLIF(TRIM(TO_CHAR(t.WARD)), '') AS Ward,
                    COUNT(DISTINCT t.TEST_RID) AS StudyCount
                FROM {Schema}.TEST t
                WHERE t.TEST_DATE >= :DateFrom
                  AND t.TEST_DATE < :DateToExclusive
                  AND t.TEST_REQUIRED IN (1, 2, 3, 4)
                  AND TRIM(TO_CHAR(t.CARDIOLOGIST1)) = :Physician
                GROUP BY
                    TO_CHAR(t.TEST_DATE, 'YYYY-MM'),
                    NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), ''),
                    NULLIF(TRIM(TO_CHAR(t.WARD)), '')
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            cmd.Parameters.Add("Physician", OracleDbType.Varchar2).Value = id;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var ym = reader.IsDBNull(0) ? null : reader.GetString(0);
                acc.AddTest(
                    MonthCol(ym),
                    reader.IsDBNull(1) ? null : reader.GetString(1),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    Convert.ToInt32(reader.GetValue(3)));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TO_CHAR(l.LETTER_DATE, 'YYYY-MM') AS YearMonth,
                    COUNT(*) AS ConsultCount
                FROM {Schema}.CONSULT c
                JOIN {Schema}.LETTERS l
                  ON l.LETTERS_RID = c.LETTERS_RID
                WHERE l.LETTER_DATE >= :DateFrom
                  AND l.LETTER_DATE < :DateToExclusive
                  AND TRIM(TO_CHAR(c.USER_LIST_RID)) = :Physician
                GROUP BY TO_CHAR(l.LETTER_DATE, 'YYYY-MM')
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            cmd.Parameters.Add("Physician", OracleDbType.Varchar2).Value = id;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var ym = reader.IsDBNull(0) ? null : reader.GetString(0);
                acc.AddConsult(MonthCol(ym), Convert.ToInt32(reader.GetValue(1)));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TRIM(TO_CHAR(a.SCHEDULE_APPOINTMENT_RID)) AS AppointmentId,
                    TO_CHAR(a.APPOINTMENT_DATE, 'YYYY-MM') AS YearMonth
                FROM {Schema}.SCHEDULE_APPOINTMENT a
                JOIN {Schema}.SCHEDULE_OCC_SERVICE o
                  ON o.SCHEDULE_OCC_SERVICE_RID = a.SCHEDULE_OCC_SERVICE_RID
                LEFT JOIN {Schema}.TEST t
                  ON t.PATIENT_RID = a.PATIENT_RID
                 AND t.TEST_DATE >= TRUNC(a.APPOINTMENT_DATE)
                 AND t.TEST_DATE < TRUNC(a.APPOINTMENT_DATE) + 1
                 AND TRIM(TO_CHAR(t.CARDIOLOGIST1)) = :Physician
                WHERE a.APPOINTMENT_DATE >= :DateFrom
                  AND a.APPOINTMENT_DATE < :DateToExclusive
                  AND o.SCHEDULE_OCC_SERVICE_RID IN ({dccsIds})
                  AND t.TEST_RID IS NOT NULL
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            cmd.Parameters.Add("Physician", OracleDbType.Varchar2).Value = id;
            var seenAppt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var appointmentId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(appointmentId) || !seenAppt.Add(appointmentId))
                    continue;
                var ym = reader.IsDBNull(1) ? null : reader.GetString(1);
                acc.AddDccs(MonthCol(ym));
            }
        }

        var columns = window.Months
            .Select(m => new DrSummaryColumn(m.YearMonth, m.Label, m.Label))
            .ToList();

        return new DrSummaryReport(
            year,
            month,
            $"{Cm2CardiologistMapping.ResolveDisplayName(id)} — prior 12 months",
            window.From,
            window.ToExclusive,
            columns,
            acc.ToRows());
    }

    private async Task<DrSummaryReport> GetAllDoctorsDrSummaryHistoryAsync(
        int year, int month, CancellationToken ct)
    {
        var asOf = new DateOnly(year, month, 1);
        var window = MonthPeriod.Prior12(asOf);
        var dateFrom = window.From.ToDateTime(TimeOnly.MinValue);
        var dateToExclusive = window.ToExclusive.ToDateTime(TimeOnly.MinValue);
        var monthKeys = window.Months.Select(m => m.YearMonth).ToList();
        var monthIndex = monthKeys
            .Select((key, i) => (key, i))
            .ToDictionary(x => x.key, x => x.i, StringComparer.OrdinalIgnoreCase);

        int MonthCol(string? yearMonth) =>
            yearMonth is not null && monthIndex.TryGetValue(yearMonth, out var i) ? i : -1;

        int MonthColForCardiologist(string? yearMonth, string? rawId)
        {
            var id = Cm2CardiologistMapping.NormalizeId(rawId);
            if (string.IsNullOrWhiteSpace(id)
                || Cm2CardiologistMapping.IsUndefinedId(id)
                || Cm2DrSummaryMapping.IsExcludedCardiologist(id))
                return -1;
            return MonthCol(yearMonth);
        }

        var acc = new DrSummaryAccumulator(monthKeys.Count);
        var dccsIds = string.Join(", ", Cm2DrSummaryMapping.DccsOccServiceIds);

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TO_CHAR(t.TEST_DATE, 'YYYY-MM') AS YearMonth,
                    TRIM(TO_CHAR(t.CARDIOLOGIST1)) AS CardiologistId,
                    NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), '') AS StudyType,
                    NULLIF(TRIM(TO_CHAR(t.WARD)), '') AS Ward,
                    COUNT(DISTINCT t.TEST_RID) AS StudyCount
                FROM {Schema}.TEST t
                WHERE t.TEST_DATE >= :DateFrom
                  AND t.TEST_DATE < :DateToExclusive
                  AND t.TEST_REQUIRED IN (1, 2, 3, 4)
                GROUP BY
                    TO_CHAR(t.TEST_DATE, 'YYYY-MM'),
                    TRIM(TO_CHAR(t.CARDIOLOGIST1)),
                    NULLIF(TRIM(TO_CHAR(t.TEST_REQUIRED)), ''),
                    NULLIF(TRIM(TO_CHAR(t.WARD)), '')
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var ym = reader.IsDBNull(0) ? null : reader.GetString(0);
                var cardio = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                acc.AddTest(
                    MonthColForCardiologist(ym, cardio),
                    reader.IsDBNull(2) ? null : reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    Convert.ToInt32(reader.GetValue(4)));
            }
        }

        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TO_CHAR(l.LETTER_DATE, 'YYYY-MM') AS YearMonth,
                    TRIM(TO_CHAR(c.USER_LIST_RID)) AS CardiologistId,
                    COUNT(*) AS ConsultCount
                FROM {Schema}.CONSULT c
                JOIN {Schema}.LETTERS l
                  ON l.LETTERS_RID = c.LETTERS_RID
                WHERE l.LETTER_DATE >= :DateFrom
                  AND l.LETTER_DATE < :DateToExclusive
                GROUP BY
                    TO_CHAR(l.LETTER_DATE, 'YYYY-MM'),
                    TRIM(TO_CHAR(c.USER_LIST_RID))
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var ym = reader.IsDBNull(0) ? null : reader.GetString(0);
                var cardio = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
                acc.AddConsult(MonthColForCardiologist(ym, cardio), Convert.ToInt32(reader.GetValue(2)));
            }
        }

        var dccsByAppointment = new Dictionary<string, (string? YearMonth, string? CardiologistId)>(
            StringComparer.OrdinalIgnoreCase);
        await using (var cmd = conn.CreateCommand())
        {
            cmd.BindByName = true;
            cmd.CommandText = $"""
                SELECT
                    TRIM(TO_CHAR(a.SCHEDULE_APPOINTMENT_RID)) AS AppointmentId,
                    TO_CHAR(a.APPOINTMENT_DATE, 'YYYY-MM') AS YearMonth,
                    TRIM(TO_CHAR(t.CARDIOLOGIST1)) AS CardiologistId
                FROM {Schema}.SCHEDULE_APPOINTMENT a
                JOIN {Schema}.SCHEDULE_OCC_SERVICE o
                  ON o.SCHEDULE_OCC_SERVICE_RID = a.SCHEDULE_OCC_SERVICE_RID
                LEFT JOIN {Schema}.TEST t
                  ON t.PATIENT_RID = a.PATIENT_RID
                 AND t.TEST_DATE >= TRUNC(a.APPOINTMENT_DATE)
                 AND t.TEST_DATE < TRUNC(a.APPOINTMENT_DATE) + 1
                 AND t.CARDIOLOGIST1 IS NOT NULL
                 AND t.CARDIOLOGIST1 NOT IN (0, 1)
                WHERE a.APPOINTMENT_DATE >= :DateFrom
                  AND a.APPOINTMENT_DATE < :DateToExclusive
                  AND o.SCHEDULE_OCC_SERVICE_RID IN ({dccsIds})
                """;
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = dateToExclusive;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var appointmentId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
                if (string.IsNullOrWhiteSpace(appointmentId))
                    continue;
                var ym = reader.IsDBNull(1) ? null : reader.GetString(1);
                var cardio = reader.IsDBNull(2) ? null : reader.GetString(2).Trim();
                if (!dccsByAppointment.TryGetValue(appointmentId, out var existing)
                    || string.IsNullOrWhiteSpace(existing.CardiologistId))
                {
                    dccsByAppointment[appointmentId] = (
                        ym,
                        string.IsNullOrWhiteSpace(cardio) ? null : cardio);
                }
            }
        }

        foreach (var (ym, cardio) in dccsByAppointment.Values)
            acc.AddDccs(MonthColForCardiologist(ym, cardio));

        var columns = window.Months
            .Select(m => new DrSummaryColumn(m.YearMonth, m.Label, m.Label))
            .ToList();

        return new DrSummaryReport(
            year,
            month,
            "All doctors — prior 12 months",
            window.From,
            window.ToExclusive,
            columns,
            acc.ToRows());
    }

    private static bool IsAllDoctorsHistory(string? cardiologistId) =>
        string.IsNullOrWhiteSpace(cardiologistId)
        || string.Equals(
            cardiologistId.Trim(),
            Cm2DrSummaryMapping.AllDoctorsColumnId,
            StringComparison.OrdinalIgnoreCase);

    private static string? LikeContains(string? query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return null;
        var trimmed = query.Trim();
        if (trimmed.Length < 2)
            return null;
        var escaped = trimmed
            .Replace("!", "!!", StringComparison.Ordinal)
            .Replace("%", "!%", StringComparison.Ordinal)
            .Replace("_", "!_", StringComparison.Ordinal);
        return "%" + escaped.ToUpperInvariant() + "%";
    }

    private static string SiteSql(string? siteCode) =>
        string.IsNullOrWhiteSpace(siteCode) ? string.Empty : "AND t.SITE = :Site";

    private static void AddSiteParameter(OracleCommand cmd, string? siteCode)
    {
        if (!string.IsNullOrWhiteSpace(siteCode))
            cmd.Parameters.Add("Site", OracleDbType.Decimal).Value = Convert.ToDecimal(siteCode);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Mirrors GPH_patList_importer outlookScheduleService.getUpcomingSchedule,
    /// filtered to Initial / Follow-up consult service titles (consultServiceTitleMatch).
    /// Cardiologist is PATIENT.PAT_CARDIOLOGIST, not same-day TEST.
    /// </remarks>
    public async Task<ResearchUpcomingConsultsResult> GetUpcomingConsultsAsync(
        DateOnly dateFrom, DateOnly dateTo, string? cardiologistId = null, CancellationToken ct = default)
    {
        if (dateTo < dateFrom)
            (dateFrom, dateTo) = (dateTo, dateFrom);

        var oracleCardioId = Cm2CardiologistMapping.ResolveOracleId(cardiologistId);
        var cardioFilter = string.IsNullOrWhiteSpace(oracleCardioId)
            || Cm2CardiologistMapping.IsUndefinedId(oracleCardioId)
            ? string.Empty
            : "AND P.PAT_CARDIOLOGIST = :CardiologistId";

        // Same title patterns as GPH SMS "Initial & Follow-up consult only".
        var sql = $"""
            SELECT
                TRIM(TO_CHAR(A.SCHEDULE_APPOINTMENT_RID)) AS AppointmentId,
                NVL(NULLIF(TRIM(TO_CHAR(P.PAT_ID)), ''), NULLIF(TRIM(TO_CHAR(P.PATIENT_RID)), '')) AS PatientCmId,
                P.PAT_FIRSTNAME,
                P.PAT_LASTNAME,
                A.APPOINTMENT_DATE,
                A.APPOINTMENT_TIME,
                TRIM(NVL(S.OCC_SERVICE_TITLE, '')) AS ServiceTitle,
                TRIM(NVL(ST.SITE_NAME, '')) AS SiteName,
                TRIM(TO_CHAR(P.PAT_CARDIOLOGIST)) AS CardiologistId,
                TRIM(NVL(A.APPOINTMENT_NOTE, '')) AS AppointmentNote,
                TRIM(TO_CHAR(P.PATIENT_RID)) AS PatientRid
            FROM {Schema}.SCHEDULE_APPOINTMENT A
            JOIN {Schema}.PATIENT P
              ON P.PATIENT_RID = A.PATIENT_RID
            LEFT JOIN {Schema}.SCHEDULE_OCC_SERVICE S
              ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
            LEFT JOIN {Schema}.SCHEDULE_ROOM R
              ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
            LEFT JOIN {Schema}.SCHEDULE_SITE ST
              ON ST.SCHEDULE_SITE_RID = R.SCHEDULE_SITE_RID
            WHERE A.APPOINTMENT_DATE >= :DateFrom
              AND A.APPOINTMENT_DATE < :DateToExclusive
              AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
              AND (
                    -- Same consult titles as GPH SMS consultServiceTitleMatch (Oracle has no JS \s)
                    REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'INITIAL[[:space:]-]*CONSULT')
                 OR REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'FOLLOW[[:space:]-]*UP[[:space:]-]*CONSULT')
                 OR REGEXP_LIKE(UPPER(NVL(S.OCC_SERVICE_TITLE, '')), 'FOLLOW[[:space:]-]*UP[[:space:]-]*-[[:space:]-]*NEW[[:space:]-]*REFERRAL')
                 OR UPPER(NVL(S.OCC_SERVICE_TITLE, '')) LIKE '%CONSULT%'
                 OR UPPER(NVL(S.OCC_SERVICE_TITLE, '')) LIKE '%NEW REFERRAL%'
                  )
              {cardioFilter}
            ORDER BY A.APPOINTMENT_DATE, A.APPOINTMENT_TIME, P.PAT_ID, A.SCHEDULE_APPOINTMENT_RID
            """;

        var rows = new List<ResearchUpcomingConsult>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value =
            dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);
        if (!string.IsNullOrWhiteSpace(cardioFilter))
            cmd.Parameters.Add("CardiologistId", OracleDbType.Decimal).Value =
                Convert.ToDecimal(oracleCardioId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var appointmentId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(appointmentId))
                continue;

            var patientCmId = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            var firstName = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
            var lastName = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
            firstName = StripLeadingSalutation(firstName);
            var patientName = FormatPatientName(lastName, firstName);

            var appointmentDateRaw = reader.IsDBNull(4) ? null : reader.GetValue(4);
            var appointmentDate = ReadDateOnly(appointmentDateRaw) ?? dateFrom;
            var appointmentTime = FormatAppointmentTime(reader.IsDBNull(5) ? null : reader.GetValue(5))
                ?? FormatAppointmentTimeFromDate(appointmentDateRaw);
            var serviceTitle = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
            var siteRaw = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
            var siteName = string.IsNullOrWhiteSpace(siteRaw) ? null : siteRaw;

            var cardioRaw = reader.IsDBNull(8) ? null : reader.GetString(8).Trim();
            var cardioId = Cm2CardiologistMapping.NormalizeId(cardioRaw);
            if (string.IsNullOrWhiteSpace(cardioId)
                || Cm2CardiologistMapping.IsUndefinedId(cardioId)
                || cardioId == "0")
            {
                cardioId = "";
            }

            var cardioName = string.IsNullOrWhiteSpace(cardioId)
                ? null
                : Cm2CardiologistMapping.ResolveDisplayName(cardioId);

            var note = reader.IsDBNull(9) ? null : reader.GetString(9).Trim();
            if (string.IsNullOrWhiteSpace(note))
                note = null;
            var patientRid = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
            if (string.IsNullOrWhiteSpace(patientRid))
                patientRid = null;

            rows.Add(new ResearchUpcomingConsult(
                appointmentId,
                patientCmId,
                patientName,
                appointmentDate,
                appointmentTime,
                serviceTitle,
                siteName,
                string.IsNullOrWhiteSpace(cardioId) ? null : cardioId,
                cardioName,
                note,
                ResearchWorklistStatus.NotAssessed,
                patientRid));
        }

        return new ResearchUpcomingConsultsResult(
            dateFrom,
            dateTo,
            string.IsNullOrWhiteSpace(cardioFilter) ? null : oracleCardioId,
            rows.Count,
            rows);
    }

    /// <inheritdoc />
    public Task<PinkRecallWorklistResult> GetUnarrivedPinkRecallsAsync(
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? site = null,
        string? cardiologistId = null,
        CancellationToken ct = default) =>
        PinkRecallWorklistStore.GetUnarrivedPinkRecallsAsync(
            _settings.Cm2OracleConnectionString, Schema, dateFrom, dateTo, site, cardiologistId, ct);

    /// <inheritdoc />
    public Task<(bool Ok, string? Error)> UpdateAppointmentNoteAsync(
        string appointmentId, string? note, CancellationToken ct = default) =>
        PinkRecallWorklistStore.UpdateAppointmentNoteAsync(
            _settings.Cm2OracleConnectionString, Schema, appointmentId, note, ct);

    public Task<(bool Ok, string? Error, string? ServiceTypeId, string? ServiceTitle)> UpdateAppointmentServiceTypeAsync(
        string appointmentId, string serviceTypeId, CancellationToken ct = default) =>
        PinkRecallWorklistStore.UpdateAppointmentServiceTypeAsync(
            _settings.Cm2OracleConnectionString, Schema, appointmentId, serviceTypeId, ct);

    public Task<(bool Ok, string? Error)> ArriveAppointmentAsync(
        string appointmentId, CancellationToken ct = default) =>
        PinkRecallWorklistStore.ArriveAppointmentAsync(
            _settings.Cm2OracleConnectionString, Schema, appointmentId, ct);

    public Task<(bool Ok, string? Error, DateOnly? Date, string? Time)> UpdateAppointmentScheduleAsync(
        string appointmentId, string date, string? time, CancellationToken ct = default) =>
        PinkRecallWorklistStore.UpdateAppointmentScheduleAsync(
            _settings.Cm2OracleConnectionString, Schema, appointmentId, date, time, ct);

    public Task<AvailableDiarySlotsResult> GetAvailableDiarySlotsAsync(
        string appointmentId, string date, CancellationToken ct = default) =>
        PinkRecallWorklistStore.GetAvailableDiarySlotsAsync(
            _settings.Cm2OracleConnectionString, Schema, appointmentId, date, ct);

    public async Task<ResearchUpcomingConsult?> GetUpcomingConsultByIdAsync(
        string appointmentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(appointmentId)
            || !decimal.TryParse(appointmentId.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            return null;

        var sql = $"""
            SELECT
                TRIM(TO_CHAR(A.SCHEDULE_APPOINTMENT_RID)) AS AppointmentId,
                NVL(NULLIF(TRIM(TO_CHAR(P.PAT_ID)), ''), NULLIF(TRIM(TO_CHAR(P.PATIENT_RID)), '')) AS PatientCmId,
                P.PAT_FIRSTNAME,
                P.PAT_LASTNAME,
                A.APPOINTMENT_DATE,
                A.APPOINTMENT_TIME,
                TRIM(NVL(S.OCC_SERVICE_TITLE, '')) AS ServiceTitle,
                TRIM(NVL(ST.SITE_NAME, '')) AS SiteName,
                TRIM(TO_CHAR(P.PAT_CARDIOLOGIST)) AS CardiologistId,
                TRIM(NVL(A.APPOINTMENT_NOTE, '')) AS AppointmentNote,
                TRIM(TO_CHAR(P.PATIENT_RID)) AS PatientRid,
                P.PAT_DOB
            FROM {Schema}.SCHEDULE_APPOINTMENT A
            JOIN {Schema}.PATIENT P
              ON P.PATIENT_RID = A.PATIENT_RID
            LEFT JOIN {Schema}.SCHEDULE_OCC_SERVICE S
              ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
            LEFT JOIN {Schema}.SCHEDULE_ROOM R
              ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
            LEFT JOIN {Schema}.SCHEDULE_SITE ST
              ON ST.SCHEDULE_SITE_RID = R.SCHEDULE_SITE_RID
            WHERE A.SCHEDULE_APPOINTMENT_RID = :AppointmentRid
            """;

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var id = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
        if (string.IsNullOrWhiteSpace(id))
            return null;

        var patientCmId = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
        var firstName = StripLeadingSalutation(reader.IsDBNull(2) ? "" : reader.GetString(2).Trim());
        var lastName = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
        var appointmentDateRaw = reader.IsDBNull(4) ? null : reader.GetValue(4);
        var appointmentDate = ReadDateOnly(appointmentDateRaw) ?? DateOnly.FromDateTime(DateTime.Today);
        var appointmentTime = FormatAppointmentTime(reader.IsDBNull(5) ? null : reader.GetValue(5))
            ?? FormatAppointmentTimeFromDate(appointmentDateRaw);
        var serviceTitle = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
        var siteRaw = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
        var cardioRaw = reader.IsDBNull(8) ? null : reader.GetString(8).Trim();
        var cardioId = Cm2CardiologistMapping.NormalizeId(cardioRaw);
        if (string.IsNullOrWhiteSpace(cardioId)
            || Cm2CardiologistMapping.IsUndefinedId(cardioId)
            || cardioId == "0")
            cardioId = "";
        var note = reader.IsDBNull(9) ? null : reader.GetString(9).Trim();
        if (string.IsNullOrWhiteSpace(note))
            note = null;
        var patientRid = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
        if (string.IsNullOrWhiteSpace(patientRid))
            patientRid = null;
        var dob = reader.FieldCount > 11 && !reader.IsDBNull(11) ? ReadDateOnly(reader.GetValue(11)) : null;
        var ageYears = dob is DateOnly dateOfBirth ? AgeYearsOn(dateOfBirth, appointmentDate) : null;

        return new ResearchUpcomingConsult(
            id,
            patientCmId,
            FormatPatientName(lastName, firstName),
            appointmentDate,
            appointmentTime,
            serviceTitle,
            string.IsNullOrWhiteSpace(siteRaw) ? null : siteRaw,
            string.IsNullOrWhiteSpace(cardioId) ? null : cardioId,
            string.IsNullOrWhiteSpace(cardioId) ? null : Cm2CardiologistMapping.ResolveDisplayName(cardioId),
            note,
            ResearchWorklistStatus.NotAssessed,
            patientRid,
            AgeYears: ageYears);
    }

    public async Task<ResearchCm2LetterRef?> GetLatestConsultLetterAsync(
        string patientRid, DateOnly? beforeDate = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientRid)
            || !decimal.TryParse(patientRid.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            return null;

        // CONSULT ⋈ LETTERS; consultation letters = LETTER_TYPE 6 (initial) / 7 (follow-up).
        // Date authority is LETTERS.LETTER_DATE. Prefer letter before upcoming appointment.
        var dateFilter = beforeDate is null
            ? ""
            : "AND L.LETTER_DATE < :BeforeDate";

        var sql = $"""
            SELECT
                TRIM(TO_CHAR(C.PATIENT_RID)) AS PatientRid,
                TRIM(TO_CHAR(C.CONSULT_RID)) AS ConsultRid,
                TRIM(TO_CHAR(L.LETTERS_RID)) AS LettersRid,
                L.LETTER_DATE,
                L.LETTER_TYPE,
                C.CONSULT_TYPE,
                TRIM(NVL(L.LETTER_TITLE, '')) AS LetterTitle,
                TRIM(NVL(L.LETTER_PATH, '')) AS LetterPath
            FROM {Schema}.CONSULT C
            JOIN {Schema}.LETTERS L
              ON L.LETTERS_RID = C.LETTERS_RID
            WHERE C.PATIENT_RID = :PatientRid
              AND L.LETTER_TYPE IN (6, 7)
              AND NULLIF(TRIM(L.LETTER_PATH), '') IS NOT NULL
              {dateFilter}
            ORDER BY L.LETTER_DATE DESC NULLS LAST, L.LETTERS_RID DESC
            FETCH FIRST 1 ROW ONLY
            """;

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("PatientRid", OracleDbType.Decimal).Value = rid;
        if (beforeDate is not null)
            cmd.Parameters.Add("BeforeDate", OracleDbType.Date).Value =
                beforeDate.Value.ToDateTime(TimeOnly.MinValue);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var path = reader.IsDBNull(7) ? "" : reader.GetString(7).Trim();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        return new ResearchCm2LetterRef(
            reader.IsDBNull(0) ? patientRid.Trim() : reader.GetString(0).Trim(),
            reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
            reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
            ReadDateOnly(reader.IsDBNull(3) ? null : reader.GetValue(3)),
            reader.IsDBNull(4) ? null : Convert.ToInt32(reader.GetValue(4)),
            reader.IsDBNull(5) ? null : Convert.ToInt32(reader.GetValue(5)),
            reader.IsDBNull(6) ? null : NullIfBlank(reader.GetString(6)),
            path);
    }

    public async Task<ResearchCm2StudyReportRef?> GetLatestInvestigationReportAsync(
        string patientRid, IReadOnlyList<int> testRequiredCodes, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(patientRid)
            || !decimal.TryParse(patientRid.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid)
            || testRequiredCodes.Count == 0)
            return null;

        var codes = testRequiredCodes.Distinct().ToList();
        var inList = string.Join(", ", codes.Select((_, i) => $":Tr{i}"));

        var sql = $"""
            SELECT
                TRIM(TO_CHAR(T.PATIENT_RID)) AS PatientRid,
                TRIM(TO_CHAR(T.TEST_RID)) AS TestRid,
                T.TEST_DATE,
                T.TEST_REQUIRED,
                TRIM(NVL(T.REPORT_PATH, '')) AS ReportPath
            FROM {Schema}.TEST T
            WHERE T.PATIENT_RID = :PatientRid
              AND T.TEST_REQUIRED IN ({inList})
              AND NULLIF(TRIM(T.REPORT_PATH), '') IS NOT NULL
            ORDER BY T.TEST_DATE DESC NULLS LAST, T.TEST_RID DESC
            FETCH FIRST 1 ROW ONLY
            """;

        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("PatientRid", OracleDbType.Decimal).Value = rid;
        for (var i = 0; i < codes.Count; i++)
            cmd.Parameters.Add($"Tr{i}", OracleDbType.Decimal).Value = codes[i];

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null;

        var path = reader.IsDBNull(4) ? "" : reader.GetString(4).Trim();
        if (string.IsNullOrWhiteSpace(path))
            return null;

        var testRequired = reader.IsDBNull(3) ? 0 : Convert.ToInt32(reader.GetValue(3));
        return new ResearchCm2StudyReportRef(
            reader.IsDBNull(0) ? patientRid.Trim() : reader.GetString(0).Trim(),
            reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
            ReadDateOnly(reader.IsDBNull(2) ? null : reader.GetValue(2)),
            testRequired,
            Cm2InvestigationTypeMapping.ResolveDisplayName(testRequired.ToString()),
            path);
    }

    public bool TryResolveCm2FilePath(string relativePath, out string fullPath, out string error) =>
        TryBuildReportFullPath(relativePath, out fullPath, out error);

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string StripLeadingSalutation(string firstName)
    {
        var s = firstName.Trim();
        for (var i = 0; i < 5; i++)
        {
            var next = System.Text.RegularExpressions.Regex.Replace(
                s, @"^(?:Mr|Mrs|Ms|Miss|Dr|Prof)\.?\s+", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (next == s)
                break;
            s = next.Trim();
        }
        return s;
    }

    private static string FormatPatientName(string lastName, string firstName)
    {
        lastName = (lastName ?? "").Trim();
        firstName = (firstName ?? "").Trim();
        if (lastName.Length > 0)
            lastName = lastName.ToUpperInvariant();
        if (lastName.Length > 0 && firstName.Length > 0)
            return $"{lastName}, {firstName}";
        if (lastName.Length > 0)
            return lastName;
        if (firstName.Length > 0)
            return firstName;
        return "—";
    }

    private static int? AgeYearsOn(DateOnly dateOfBirth, DateOnly onDate)
    {
        var age = onDate.Year - dateOfBirth.Year;
        if (onDate < dateOfBirth.AddYears(age))
            age--;
        return age is >= 0 and <= 120 ? age : null;
    }

    private static DateOnly? ReadDateOnly(object? value)
    {
        if (value is null or DBNull)
            return null;
        if (value is DateTime dt)
            return DateOnly.FromDateTime(dt);
        if (value is DateTimeOffset dto)
            return DateOnly.FromDateTime(dto.DateTime);
        if (DateTime.TryParse(Convert.ToString(value), out var parsed))
            return DateOnly.FromDateTime(parsed);
        return null;
    }

    /// <summary>Formats CM2 APPOINTMENT_TIME (often HHMM number/string) as HH:mm.</summary>
    private static string? FormatAppointmentTime(object? value)
    {
        if (value is null or DBNull)
            return null;

        if (value is DateTime dt)
        {
            if (dt.Hour == 0 && dt.Minute == 0)
                return null;
            return $"{dt.Hour:D2}:{dt.Minute:D2}";
        }

        string digits;
        if (value is decimal or int or long or short or double or float)
        {
            var n = Convert.ToInt32(Convert.ToDecimal(value));
            if (n <= 0)
                return null;
            digits = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            var raw = Convert.ToString(value)?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            digits = System.Text.RegularExpressions.Regex.Replace(raw, @"\D", "");
        }

        if (string.IsNullOrWhiteSpace(digits) || digits == "0" || digits == "0000")
            return null;
        digits = digits.PadLeft(4, '0');
        if (digits.Length > 4)
            digits = digits[^4..];
        var hour = int.Parse(digits[..2], System.Globalization.CultureInfo.InvariantCulture);
        var minute = int.Parse(digits[2..], System.Globalization.CultureInfo.InvariantCulture);
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
            return null;
        return $"{hour:D2}:{minute:D2}";
    }

    private static string? FormatAppointmentTimeFromDate(object? value) =>
        value is DateTime dt && (dt.Hour != 0 || dt.Minute != 0)
            ? $"{dt.Hour:D2}:{dt.Minute:D2}"
            : null;
}

