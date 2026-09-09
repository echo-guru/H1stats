using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Oracle.ManagedDataAccess.Client;

namespace H1Stats.Infrastructure.Data;

/// <summary>
/// Consult-referral documents (CM2 DOCUMENTS) with no corresponding unarrived recall.
/// </summary>
public static class UnactionedConsultReferralStore
{
    /// <summary>Incoming referral documents (Cons Ref, Echo Ref, Holter Ref, …).</summary>
    private const int ReferralDocumentCategory = 4;

    /// <summary>Initial / Follow-up consult OCC_SERVICE RIDs (informational upcoming-consult column).</summary>
    private static readonly int[] ConsultOccServiceRids =
        [21, 22, 181, 182, 201, 241, 281, 282, 283];

    public static async Task<UnactionedConsultReferralResult> GetUnactionedConsultReferralsAsync(
        string connectionString,
        string schema,
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (dateTo < dateFrom)
            (dateFrom, dateTo) = (dateTo, dateFrom);

        var oracleCardioId = Cm2CardiologistMapping.ResolveOracleId(cardiologistId);
        string cardioFilter;
        if (Cm2CardiologistMapping.IsUnassignedFilter(cardiologistId))
        {
            cardioFilter = $"""
              AND (P.PAT_CARDIOLOGIST IS NULL
                   OR P.PAT_CARDIOLOGIST IN (0, {Cm2CardiologistMapping.UndefinedId}))
              """;
            oracleCardioId = Cm2CardiologistMapping.UnassignedFilterId;
        }
        else if (string.IsNullOrWhiteSpace(oracleCardioId))
        {
            cardioFilter = string.Empty;
        }
        else
        {
            cardioFilter = "AND P.PAT_CARDIOLOGIST = :CardiologistId";
        }

        var recallIds =
            $"{Cm2RecallServiceTypes.CardiologyRecallId},{Cm2RecallServiceTypes.AdminRecallId}";
        var consultRidList = string.Join(",", ConsultOccServiceRids);

        var sql = $"""
            SELECT
                TRIM(TO_CHAR(D.DOCUMENTS_RID)) AS DocumentId,
                TRIM(TO_CHAR(D.PATIENT_RID)) AS PatientRid,
                NVL(NULLIF(TRIM(TO_CHAR(P.PAT_ID)), ''), NULLIF(TRIM(TO_CHAR(P.PATIENT_RID)), '')) AS PatientCmId,
                TRIM(NVL(P.PAT_LASTNAME, '')) AS LastName,
                TRIM(NVL(P.PAT_FIRSTNAME, '')) AS FirstName,
                P.PAT_DOB,
                TRIM(TO_CHAR(P.PAT_CARDIOLOGIST)) AS CardiologistId,
                D.DOC_DATE,
                TRIM(NVL(D.DOC_TITLE, '')) AS DocumentTitle,
                TRIM(NVL(D.DOC_PATH, '')) AS DocumentPath,
                NVL(D.DOC_CATEGORY, 0) AS DocumentCategory,
                C.APPOINTMENT_DATE AS UpcomingConsultDate,
                TRIM(NVL(C.OCC_SERVICE_TITLE, '')) AS UpcomingConsultTitle,
                TRIM(NVL(C.AppointmentNote, '')) AS UpcomingConsultNote
            FROM {schema}.DOCUMENTS D
            JOIN {schema}.PATIENT P
              ON P.PATIENT_RID = D.PATIENT_RID
            LEFT JOIN (
              SELECT
                A.PATIENT_RID,
                A.APPOINTMENT_DATE,
                S.OCC_SERVICE_TITLE,
                TRIM(NVL(A.APPOINTMENT_NOTE, '')) AS AppointmentNote,
                ROW_NUMBER() OVER (
                  PARTITION BY A.PATIENT_RID
                  ORDER BY A.APPOINTMENT_DATE, A.SCHEDULE_APPOINTMENT_RID
                ) AS Rn
              FROM {schema}.SCHEDULE_APPOINTMENT A
              JOIN {schema}.SCHEDULE_OCC_SERVICE S
                ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
              WHERE A.APPOINTMENT_DATE >= TRUNC(SYSDATE)
                AND A.SCHEDULE_OCC_SERVICE_RID IN ({consultRidList})
            ) C
              ON C.PATIENT_RID = D.PATIENT_RID AND C.Rn = 1
            WHERE D.DOC_CATEGORY = :ReferralCategory
              AND REGEXP_LIKE(NVL(D.DOC_TITLE, ' '), '^[[:space:]]*CONS[[:space:]]+REF', 'i')
              AND D.DOC_DATE >= :DateFrom
              AND D.DOC_DATE < :DateToExclusive
              AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
              AND NOT EXISTS (
                SELECT 1
                  FROM {schema}.SCHEDULE_APPOINTMENT A
                 WHERE A.PATIENT_RID = D.PATIENT_RID
                   AND A.SCHEDULE_OCC_SERVICE_RID IN ({recallIds})
                   AND NVL(A.APPOINTMENT_ARRIVED, 0) = 0
              )
              {cardioFilter}
            ORDER BY LastName, FirstName, D.DOC_DATE DESC, D.DOCUMENTS_RID DESC
            """;

        var rows = new List<UnactionedConsultReferralRow>();
        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("ReferralCategory", OracleDbType.Decimal).Value = ReferralDocumentCategory;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value =
            dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);

        if (!string.IsNullOrWhiteSpace(cardioFilter)
            && !Cm2CardiologistMapping.IsUnassignedFilter(oracleCardioId))
            cmd.Parameters.Add("CardiologistId", OracleDbType.Decimal).Value =
                Convert.ToDecimal(oracleCardioId);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var documentId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(documentId))
                continue;

            var patientRid = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            var patientCmId = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
            var lastName = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
            if (lastName.Length > 0)
                lastName = lastName.ToUpperInvariant();
            var firstName = StripLeadingSalutation(reader.IsDBNull(4) ? "" : reader.GetString(4).Trim());
            var dob = reader.IsDBNull(5) ? null : ReadDateOnly(reader.GetValue(5));

            var cardioRaw = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();
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

            var documentDate = ReadDateOnly(reader.IsDBNull(7) ? null : reader.GetValue(7))
                ?? dateFrom;
            var title = reader.IsDBNull(8) ? null : reader.GetString(8).Trim();
            if (string.IsNullOrWhiteSpace(title)) title = null;
            var path = reader.IsDBNull(9) ? null : reader.GetString(9).Trim();
            if (string.IsNullOrWhiteSpace(path)) path = null;
            var category = reader.IsDBNull(10) ? 0 : Convert.ToInt32(reader.GetDecimal(10));
            var upcomingDate = reader.IsDBNull(11) ? null : ReadDateOnly(reader.GetValue(11));
            var upcomingTitle = reader.IsDBNull(12) ? null : reader.GetString(12).Trim();
            if (string.IsNullOrWhiteSpace(upcomingTitle)) upcomingTitle = null;
            var upcomingNote = reader.IsDBNull(13) ? null : reader.GetString(13).Trim();
            if (string.IsNullOrWhiteSpace(upcomingNote)) upcomingNote = null;

            rows.Add(new UnactionedConsultReferralRow(
                documentId,
                patientRid,
                patientCmId,
                lastName,
                firstName,
                dob,
                string.IsNullOrWhiteSpace(cardioId) ? null : cardioId,
                cardioName,
                documentDate,
                title,
                path,
                category,
                upcomingDate,
                upcomingTitle,
                upcomingNote));
        }

        return new UnactionedConsultReferralResult(
            dateFrom,
            dateTo,
            string.IsNullOrWhiteSpace(oracleCardioId) ? null : oracleCardioId,
            rows.Count,
            rows);
    }

    /// <summary>
    /// Resolves the consult-referral PDF on the CM2 drive so the API can stream it to the browser.
    /// </summary>
    public static async Task<(StudyReportFile? File, string? Error)> GetConsultReferralPdfAsync(
        string connectionString,
        string schema,
        string documentId,
        string? reportRoot,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (string.IsNullOrWhiteSpace(documentId)
            || !decimal.TryParse(documentId.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            return (null, "Invalid document id");

        var sql = $"""
            SELECT TRIM(NVL(DOC_PATH, ''))
              FROM {schema}.DOCUMENTS
             WHERE DOCUMENTS_RID = :DocumentId
               AND DOC_CATEGORY = :ReferralCategory
               AND REGEXP_LIKE(NVL(DOC_TITLE, ' '), '^[[:space:]]*CONS[[:space:]]+REF', 'i')
            """;

        string? relativePath;
        try
        {
            await using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = sql;
            cmd.Parameters.Add("DocumentId", OracleDbType.Decimal).Value = rid;
            cmd.Parameters.Add("ReferralCategory", OracleDbType.Decimal).Value = ReferralDocumentCategory;
            var value = await cmd.ExecuteScalarAsync(ct);
            if (value is null or DBNull)
                return (null, "Document not found");
            relativePath = Convert.ToString(value)?.Trim();
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }

        if (string.IsNullOrWhiteSpace(relativePath))
            return (null, "This referral has no stored file path.");

        if (!TryResolveUnderRoot(reportRoot, relativePath, out var fullPath, out var resolveError))
            return (null, resolveError);

        if (!File.Exists(fullPath))
            return (null, "Referral PDF was not found on the CM2 drive.");

        var fileName = Path.GetFileName(fullPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = "referral.pdf";
        var ext = Path.GetExtension(fullPath);
        var contentType = ext.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            ? "application/pdf"
            : "application/octet-stream";
        return (new StudyReportFile(fullPath, fileName, contentType), null);
    }

    private static bool TryResolveUnderRoot(
        string? reportRoot, string relativePath, out string fullPath, out string error)
    {
        fullPath = "";
        var root = reportRoot?.Trim();
        if (string.IsNullOrWhiteSpace(root))
        {
            error = "CM2 report folder is not configured (Database:Cm2ReportRoot). Set it to the CM2 mapped drive, typically T:\\.";
            return false;
        }

        var relative = relativePath.Replace('/', '\\').Trim().TrimStart('\\');
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

    private static string NormalizeSchema(string schema) =>
        string.IsNullOrWhiteSpace(schema) ? "HEARTS1ST" : schema.Trim().ToUpperInvariant();

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
}
