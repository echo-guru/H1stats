using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Oracle.ManagedDataAccess.Client;

namespace H1Stats.Infrastructure.Data;

/// <summary>
/// SandBox: patients with Initial/Follow-up consults in a lookback window who have no H1 cardiologist.
/// </summary>
public static class UnassignedConsultStore
{
    /// <summary>Initial / Follow-up consult OCC_SERVICE RIDs (excludes phone / telehealth).</summary>
    private static readonly int[] ConsultOccServiceRids =
        [21, 22, 181, 182, 201, 241, 281, 282, 283];

    public static async Task<UnassignedConsultPatientResult> GetUnassignedConsultPatientsAsync(
        string connectionString,
        string schema,
        int lookbackMonths = 12,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (lookbackMonths < 1) lookbackMonths = 12;
        if (lookbackMonths > 120) lookbackMonths = 120;

        var dateTo = DateOnly.FromDateTime(DateTime.Today);
        var dateFrom = dateTo.AddMonths(-lookbackMonths);

        var ridList = string.Join(",", ConsultOccServiceRids);
        var undefinedId = Cm2CardiologistMapping.UndefinedId;

        var sql = $"""
            SELECT * FROM (
              SELECT
                TRIM(TO_CHAR(A.PATIENT_RID)) AS PatientRid,
                NVL(NULLIF(TRIM(TO_CHAR(P.PAT_ID)), ''), NULLIF(TRIM(TO_CHAR(P.PATIENT_RID)), '')) AS PatientCmId,
                TRIM(NVL(P.PAT_LASTNAME, '')) AS LastName,
                TRIM(NVL(P.PAT_FIRSTNAME, '')) AS FirstName,
                P.PAT_DOB,
                TRIM(TO_CHAR(A.SCHEDULE_APPOINTMENT_RID)) AS AppointmentId,
                A.APPOINTMENT_DATE,
                TRIM(NVL(A.APPOINTMENT_NOTE, '')) AS AppointmentNote,
                TRIM(NVL(S.OCC_SERVICE_TITLE, '')) AS ServiceTitle,
                NVL(A.APPOINTMENT_ARRIVED, 0) AS Arrived,
                TRIM(NVL(R.ROOM_NAME, '')) AS RoomName,
                TRIM(NVL(ST.SITE_NAME, '')) AS SiteName,
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
              WHERE A.APPOINTMENT_DATE >= :DateFrom
                AND A.APPOINTMENT_DATE < :DateToExclusive
                AND A.SCHEDULE_OCC_SERVICE_RID IN ({ridList})
                AND (P.PAT_CARDIOLOGIST IS NULL OR P.PAT_CARDIOLOGIST IN (0, {undefinedId}))
                AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
            ) WHERE Rn = 1
            ORDER BY LastName, FirstName, PatientRid
            """;

        var rows = new List<UnassignedConsultPatientRow>();
        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value =
            dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var patientRid = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(patientRid))
                continue;

            var patientCmId = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            var lastName = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
            if (lastName.Length > 0)
                lastName = lastName.ToUpperInvariant();
            var firstName = StripLeadingSalutation(reader.IsDBNull(3) ? "" : reader.GetString(3).Trim());
            var dob = reader.IsDBNull(4) ? null : ReadDateOnly(reader.GetValue(4));
            var appointmentId = reader.IsDBNull(5) ? "" : reader.GetString(5).Trim();
            var appointmentDate = ReadDateOnly(reader.IsDBNull(6) ? null : reader.GetValue(6))
                ?? dateFrom;
            var note = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
            if (string.IsNullOrWhiteSpace(note)) note = null;
            var serviceTitle = reader.IsDBNull(8) ? "" : reader.GetString(8).Trim();
            var arrived = !reader.IsDBNull(9) && Convert.ToInt32(reader.GetDecimal(9)) != 0;
            var room = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
            if (string.IsNullOrWhiteSpace(room)) room = null;
            var site = reader.IsDBNull(11) ? null : reader.GetString(11).Trim();
            if (string.IsNullOrWhiteSpace(site)) site = null;
            var bookingCount = reader.IsDBNull(12) ? 1 : Convert.ToInt32(reader.GetDecimal(12));
            var time = FormatAppointmentTime(reader.IsDBNull(6) ? null : reader.GetValue(6));

            rows.Add(new UnassignedConsultPatientRow(
                patientRid,
                patientCmId,
                lastName,
                firstName,
                dob,
                appointmentId,
                appointmentDate,
                time,
                string.IsNullOrWhiteSpace(serviceTitle) ? null : serviceTitle,
                arrived,
                note,
                room,
                site,
                Math.Max(1, bookingCount)));
        }

        return new UnassignedConsultPatientResult(dateFrom, dateTo, rows.Count, rows);
    }

    public static async Task<(bool Ok, string? Error, string? CardiologistId, string? CardiologistName)> AssignPatientCardiologistAsync(
        string connectionString,
        string schema,
        string patientRid,
        string cardiologistId,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (string.IsNullOrWhiteSpace(patientRid)
            || !decimal.TryParse(patientRid.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            return (false, "Invalid patient id", null, null);

        var oracleId = Cm2CardiologistMapping.ResolveOracleId(cardiologistId);
        if (string.IsNullOrWhiteSpace(oracleId)
            || Cm2CardiologistMapping.IsUndefinedId(oracleId)
            || Cm2CardiologistMapping.IsUnassignedFilter(oracleId)
            || !Cm2CardiologistMapping.SummaryColumnIds.Contains(oracleId))
            return (false, "Cardiologist must be one of the Hearts 1st doctors", null, null);

        if (!decimal.TryParse(oracleId, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var cardioRid))
            return (false, "Invalid cardiologist id", null, null);

        var displayName = Cm2CardiologistMapping.ResolveDisplayName(oracleId);

        var sql = $"""
            UPDATE {schema}.PATIENT
               SET PAT_CARDIOLOGIST = :CardiologistId
             WHERE PATIENT_RID = :PatientRid
            """;

        try
        {
            await using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = sql;
            cmd.Parameters.Add("CardiologistId", OracleDbType.Decimal).Value = cardioRid;
            cmd.Parameters.Add("PatientRid", OracleDbType.Decimal).Value = rid;

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected == 0)
                return (false, "Patient not found", null, null);

            return (true, null, oracleId, displayName);
        }
        catch (OracleException ex) when (ex.Number == 1031)
        {
            return (false, "Oracle user lacks UPDATE on patient cardiologist. Ask DBA to grant UPDATE (PAT_CARDIOLOGIST) on PATIENT.", null, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null, null);
        }
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
}
