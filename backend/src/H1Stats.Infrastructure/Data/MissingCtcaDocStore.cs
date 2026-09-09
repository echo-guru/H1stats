using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Oracle.ManagedDataAccess.Client;

namespace H1Stats.Infrastructure.Data;

/// <summary>
/// Past CT Coronary Angiogram diary bookings with no imported test/results CTCA document.
/// </summary>
public static class MissingCtcaDocStore
{
    /// <summary>CT Coronary Angiogram in the CM2 diary.</summary>
    private const int CtcaOccServiceRid = 24;

    /// <summary>Imported test/results documents (echo PDFs, CTCA reports, ECGs, …).</summary>
    private const int TestResultsDocumentCategory = 1;

    public static async Task<MissingCtcaDocResult> GetMissingCtcaDocsAsync(
        string connectionString,
        string schema,
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        (dateFrom, dateTo) = ClampToPast(dateFrom, dateTo);

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

        var sql = $"""
            SELECT * FROM (
              SELECT
                TRIM(TO_CHAR(A.SCHEDULE_APPOINTMENT_RID)) AS AppointmentId,
                TRIM(TO_CHAR(A.PATIENT_RID)) AS PatientRid,
                NVL(NULLIF(TRIM(TO_CHAR(P.PAT_ID)), ''), NULLIF(TRIM(TO_CHAR(P.PATIENT_RID)), '')) AS PatientCmId,
                TRIM(NVL(P.PAT_LASTNAME, '')) AS LastName,
                TRIM(NVL(P.PAT_FIRSTNAME, '')) AS FirstName,
                P.PAT_DOB,
                TRIM(TO_CHAR(P.PAT_CARDIOLOGIST)) AS CardiologistId,
                A.APPOINTMENT_DATE,
                A.APPOINTMENT_TIME,
                NVL(A.APPOINTMENT_ARRIVED, 0) AS Arrived,
                TRIM(NVL(A.APPOINTMENT_NOTE, '')) AS AppointmentNote,
                TRIM(NVL(R.ROOM_NAME, '')) AS RoomName,
                TRIM(NVL(ST.SITE_NAME, '')) AS SiteName,
                TRIM(NVL(S.OCC_SERVICE_TITLE, '')) AS ServiceTitle,
                COUNT(*) OVER (PARTITION BY A.PATIENT_RID) AS BookingCount,
                ROW_NUMBER() OVER (
                  PARTITION BY A.PATIENT_RID
                  ORDER BY A.APPOINTMENT_DATE DESC, A.SCHEDULE_APPOINTMENT_RID DESC
                ) AS Rn
              FROM {schema}.SCHEDULE_APPOINTMENT A
              JOIN {schema}.PATIENT P
                ON P.PATIENT_RID = A.PATIENT_RID
              JOIN {schema}.SCHEDULE_OCC_SERVICE S
                ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
              LEFT JOIN {schema}.SCHEDULE_ROOM R
                ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
              LEFT JOIN {schema}.SCHEDULE_SITE ST
                ON ST.SCHEDULE_SITE_RID = R.SCHEDULE_SITE_RID
              WHERE A.SCHEDULE_OCC_SERVICE_RID = :CtcaOccServiceRid
                AND A.APPOINTMENT_DATE >= :DateFrom
                AND A.APPOINTMENT_DATE < :DateToExclusive
                AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
                AND NOT EXISTS (
                  SELECT 1
                    FROM {schema}.DOCUMENTS D
                   WHERE D.PATIENT_RID = A.PATIENT_RID
                     AND D.DOC_CATEGORY = :ResultsCategory
                     AND (
                          REGEXP_LIKE(NVL(D.DOC_TITLE, ' '), 'CTCA', 'i')
                       OR REGEXP_LIKE(NVL(D.DOC_TITLE, ' '), 'CT[[:space:]]*CORONARY[[:space:]]*ANGIO', 'i')
                     )
                     AND D.DOC_DATE >= TRUNC(A.APPOINTMENT_DATE)
                )
                {cardioFilter}
            ) WHERE Rn = 1
            ORDER BY APPOINTMENT_DATE DESC, LastName, FirstName, PatientRid
            """;

        var rows = new List<MissingCtcaDocRow>();
        if (dateFrom > dateTo)
        {
            return new MissingCtcaDocResult(
                dateFrom,
                dateTo,
                string.IsNullOrWhiteSpace(oracleCardioId) ? null : oracleCardioId,
                0,
                rows);
        }

        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("CtcaOccServiceRid", OracleDbType.Decimal).Value = CtcaOccServiceRid;
        cmd.Parameters.Add("ResultsCategory", OracleDbType.Decimal).Value = TestResultsDocumentCategory;
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
            var appointmentId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(appointmentId))
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

            var appointmentDateRaw = reader.IsDBNull(7) ? null : reader.GetValue(7);
            var appointmentDate = ReadDateOnly(appointmentDateRaw) ?? dateFrom;
            var appointmentTime = FormatAppointmentTime(reader.IsDBNull(8) ? null : reader.GetValue(8))
                ?? FormatAppointmentTimeFromDate(appointmentDateRaw);
            var arrived = !reader.IsDBNull(9) && Convert.ToInt32(reader.GetDecimal(9)) != 0;
            var note = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
            if (string.IsNullOrWhiteSpace(note)) note = null;
            var room = reader.IsDBNull(11) ? null : reader.GetString(11).Trim();
            if (string.IsNullOrWhiteSpace(room)) room = null;
            var site = reader.IsDBNull(12) ? null : reader.GetString(12).Trim();
            if (string.IsNullOrWhiteSpace(site)) site = null;
            var serviceTitle = reader.IsDBNull(13) ? "" : reader.GetString(13).Trim();
            if (string.IsNullOrWhiteSpace(serviceTitle)) serviceTitle = "CT Coronary Angiogram";
            var bookingCount = reader.IsDBNull(14) ? 1 : Convert.ToInt32(reader.GetDecimal(14));

            rows.Add(new MissingCtcaDocRow(
                appointmentId,
                patientRid,
                patientCmId,
                lastName,
                firstName,
                dob,
                string.IsNullOrWhiteSpace(cardioId) ? null : cardioId,
                cardioName,
                appointmentDate,
                appointmentTime,
                arrived,
                note,
                room,
                site,
                serviceTitle,
                Math.Max(1, bookingCount)));
        }

        return new MissingCtcaDocResult(
            dateFrom,
            dateTo,
            string.IsNullOrWhiteSpace(oracleCardioId) ? null : oracleCardioId,
            rows.Count,
            rows);
    }

    internal static (DateOnly From, DateOnly To) ClampToPast(DateOnly dateFrom, DateOnly dateTo)
    {
        if (dateTo < dateFrom)
            (dateFrom, dateTo) = (dateTo, dateFrom);

        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        if (dateTo > yesterday)
            dateTo = yesterday;
        return (dateFrom, dateTo);
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

    private static string? FormatAppointmentTime(object? value)
    {
        if (value is DateTime dt && (dt.Hour != 0 || dt.Minute != 0))
            return $"{dt.Hour:D2}:{dt.Minute:D2}";
        return null;
    }

    private static string? FormatAppointmentTimeFromDate(object? value)
    {
        if (value is DateTime dt && (dt.Hour != 0 || dt.Minute != 0))
            return $"{dt.Hour:D2}:{dt.Minute:D2}";
        return null;
    }
}
