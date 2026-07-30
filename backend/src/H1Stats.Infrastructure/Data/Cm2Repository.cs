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
        DateOnly dateFrom, DateOnly dateTo, int topN, string? investigationType, CancellationToken ct = default)
    {
        topN = Cm2TopN.Clamp(topN);
        var typeCode = Cm2InvestigationTypeMapping.ResolveReferringDoctorCode(investigationType);
        var typeFilter = string.IsNullOrWhiteSpace(typeCode)
            ? "AND t.TEST_REQUIRED IN (1, 2, 3, 4, 6, 7, 8, 9, 10, 11, 12)"
            : "AND t.TEST_REQUIRED = :InvestigationType";

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
                : Cm2InvestigationTypeMapping.ResolveDisplayName(typeCode));
    }

    public async Task<TopReferringPracticesReport> GetTopReferringPracticesAsync(
        DateOnly dateFrom, DateOnly dateTo, int topN, CancellationToken ct = default)
    {
        topN = Cm2TopN.Clamp(topN);
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

        return new TopReferringPracticesReport(groups, grand, topN);
    }
}

