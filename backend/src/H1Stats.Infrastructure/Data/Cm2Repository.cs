using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;

namespace H1Stats.Infrastructure.Data;

/// <summary>
/// Read-only access to CM2 Oracle (HEARTS1ST.TEST) for reporting-dr and referring-dr reports.
/// </summary>
public class Cm2Repository : ICm2Repository
{
    private readonly DatabaseSettings _settings;

    public Cm2Repository(IOptions<DatabaseSettings> options) =>
        _settings = options.Value;

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

    public async Task<IReadOnlyList<SiteOption>> GetSitesAsync(CancellationToken ct = default)
    {
        try
        {
            var sql = $"""
                SELECT TRIM(TO_CHAR(SCHEDULE_SITE_RID)) AS Id,
                       NULLIF(TRIM(SITE_NAME), '') AS DisplayName
                FROM {Schema}.SCHEDULE_SITE
                ORDER BY SITE_ORDER, SITE_NAME
                """;

            var list = new List<SiteOption>();
            await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.IsDBNull(0) ? null : reader.GetString(0);
                if (string.IsNullOrWhiteSpace(id))
                    continue;
                var name = reader.IsDBNull(1) ? null : reader.GetString(1);
                list.Add(new SiteOption(id, string.IsNullOrWhiteSpace(name)
                    ? Cm2SiteMapping.ResolveDisplayName(id)
                    : name.Trim()));
            }

            if (list.Count > 0)
                return list;
        }
        catch
        {
            // Fall through to curated campus list.
        }

        return Cm2SiteMapping.All
            .Select(x => new SiteOption(x.Code, x.DisplayName))
            .ToList();
    }

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

    public async Task<ReferrerHistoryReport> GetReferrerHistoryAsync(
        string mode, string entityId, string? investigationType, CancellationToken ct = default)
    {
        var isPractice = string.Equals(mode, "practice", StringComparison.OrdinalIgnoreCase);
        var isDoctor = string.Equals(mode, "doctor", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mode, "dr", StringComparison.OrdinalIgnoreCase);
        if (!isPractice && !isDoctor)
            throw new ArgumentException("mode must be doctor or practice.", nameof(mode));

        var modeKey = isPractice ? "practice" : "doctor";
        if (!TryParseEntityId(entityId, out var idKind, out var rid))
            throw new ArgumentException("Invalid id. Expected CM2:{rid} or LEGACY:{rid}.", nameof(entityId));
        if (isPractice && idKind != "CM2")
            throw new ArgumentException("Practice search requires a CM2 practice id.", nameof(entityId));

        var window = MonthPeriod.Prior12();
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
                COUNT(DISTINCT t.TEST_RID) AS StudyCount
            FROM {Schema}.TEST t
            LEFT JOIN {Schema}.DOCTOR d
              ON d.DOCTOR_RID = t.REFERRING_DOCTOR
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
                END
            """;

        var rows = new List<(string YearMonth, string InvestigationType, string? Ward, string? PracticeName, int Count)>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = window.From.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value = window.ToExclusive.ToDateTime(TimeOnly.MinValue);
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
            rows.Add((
                yearMonth,
                Cm2InvestigationTypeMapping.ResolveDisplayName(rawType),
                ward,
                string.IsNullOrWhiteSpace(practiceName) ? null : practiceName,
                Convert.ToInt32(reader.GetValue(4))));
        }

        var byMonth = rows.ToLookup(r => r.YearMonth, StringComparer.Ordinal);
        var months = window.Months
            .Select(m =>
            {
                var monthRows = byMonth[m.YearMonth].ToList();
                var testRows = monthRows
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

                return new ReferrerHistoryMonthRow(
                    m.YearMonth,
                    m.Label,
                    testRows.Sum(t => t.Total),
                    testRows.Sum(t => t.Outpatient),
                    testRows.Sum(t => t.Inpatient),
                    testRows);
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
            window.From,
            window.ToExclusive.AddDays(-1),
            combinedSites,
            months,
            grand,
            string.IsNullOrWhiteSpace(typeCode)
                ? null
                : Cm2InvestigationTypeMapping.ResolveDisplayName(typeCode));
    }

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
}

