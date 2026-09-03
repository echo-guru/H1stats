using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Oracle.ManagedDataAccess.Client;

namespace H1Stats.Infrastructure.Data;

/// <summary>
/// Pink recall worklist queries against any CM2 Oracle connection (production or CM2DEV).
/// </summary>
public static class PinkRecallWorklistStore
{
    /// <summary>Cardiology Recall — pink diary colour (255,180,255) in CM2.</summary>
    private const int PinkRecallOccServiceRid = Cm2RecallServiceTypes.CardiologyRecallId;

    public static async Task<IReadOnlyList<SiteOption>> GetSitesAsync(
        string connectionString, string schema, CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        try
        {
            var sql = $"""
                SELECT TRIM(TO_CHAR(SCHEDULE_SITE_RID)) AS Id,
                       NULLIF(TRIM(SITE_NAME), '') AS DisplayName
                FROM {schema}.SCHEDULE_SITE
                ORDER BY SITE_ORDER, SITE_NAME
                """;

            var list = new List<SiteOption>();
            await using var conn = new OracleConnection(connectionString);
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

    public static async Task<PinkRecallWorklistResult> GetUnarrivedPinkRecallsAsync(
        string connectionString,
        string schema,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? site = null,
        string? cardiologistId = null,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        DateOnly? from = dateFrom;
        DateOnly? to = dateTo;
        if (from.HasValue && to.HasValue && to.Value < from.Value)
            (from, to) = (to, from);

        var siteCode = Cm2SiteMapping.ResolveCode(site);
        var siteFilter = string.IsNullOrWhiteSpace(siteCode)
            ? string.Empty
            : "AND ST.SCHEDULE_SITE_RID = :Site";

        var oracleCardioId = Cm2CardiologistMapping.ResolveOracleId(cardiologistId);
        string cardioFilter;
        if (Cm2CardiologistMapping.IsUnassignedFilter(cardiologistId))
        {
            // No named H1 cardiologist: null, 0, or CM2 "Undefined Dr" (id 1).
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

        var dateFilter = from.HasValue && to.HasValue
            ? """
              AND A.APPOINTMENT_DATE >= :DateFrom
              AND A.APPOINTMENT_DATE < :DateToExclusive
              """
            : string.Empty;

        var sql = $"""
            SELECT
                TRIM(TO_CHAR(A.SCHEDULE_APPOINTMENT_RID)) AS AppointmentId,
                TRIM(NVL(P.PAT_LASTNAME, '')) AS LastName,
                TRIM(NVL(P.PAT_FIRSTNAME, '')) AS FirstName,
                NVL(NULLIF(TRIM(TO_CHAR(P.PAT_ID)), ''), NULLIF(TRIM(TO_CHAR(P.PATIENT_RID)), '')) AS PatientCmId,
                P.PAT_DOB,
                TRIM(TO_CHAR(P.PAT_CARDIOLOGIST)) AS CardiologistId,
                TRIM(NVL(R.ROOM_NAME, '')) AS RoomName,
                TRIM(NVL(ST.SITE_NAME, '')) AS SiteName,
                A.APPOINTMENT_DATE,
                A.APPOINTMENT_TIME,
                TRIM(NVL(A.APPOINTMENT_NOTE, '')) AS AppointmentNote,
                TRIM(TO_CHAR(S.SCHEDULE_OCC_SERVICE_RID)) AS ServiceTypeId,
                TRIM(NVL(S.OCC_SERVICE_TITLE, '')) AS ServiceTitle
            FROM {schema}.SCHEDULE_APPOINTMENT A
            JOIN {schema}.PATIENT P
              ON P.PATIENT_RID = A.PATIENT_RID
            LEFT JOIN {schema}.SCHEDULE_OCC_SERVICE S
              ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
            LEFT JOIN {schema}.SCHEDULE_ROOM R
              ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
            LEFT JOIN {schema}.SCHEDULE_SITE ST
              ON ST.SCHEDULE_SITE_RID = R.SCHEDULE_SITE_RID
            WHERE S.SCHEDULE_OCC_SERVICE_RID = :PinkRecallOccServiceRid
              AND NVL(A.APPOINTMENT_ARRIVED, 0) = 0
              AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
              {dateFilter}
              {siteFilter}
              {cardioFilter}
            ORDER BY A.APPOINTMENT_DATE, A.APPOINTMENT_TIME, P.PAT_LASTNAME, P.PAT_FIRSTNAME, A.SCHEDULE_APPOINTMENT_RID
            """;

        var rows = new List<PinkRecallWorklistRow>();
        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("PinkRecallOccServiceRid", OracleDbType.Decimal).Value = PinkRecallOccServiceRid;

        if (!string.IsNullOrWhiteSpace(dateFilter) && from.HasValue && to.HasValue)
        {
            cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = from.Value.ToDateTime(TimeOnly.MinValue);
            cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value =
                to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
        }

        if (!string.IsNullOrWhiteSpace(siteCode))
            cmd.Parameters.Add("Site", OracleDbType.Decimal).Value = Convert.ToDecimal(siteCode);

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

            var lastName = reader.IsDBNull(1) ? "" : reader.GetString(1).Trim();
            var firstName = StripLeadingSalutation(reader.IsDBNull(2) ? "" : reader.GetString(2).Trim());
            if (lastName.Length > 0)
                lastName = lastName.ToUpperInvariant();

            var patientCmId = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
            var dob = reader.IsDBNull(4) ? null : ReadDateOnly(reader.GetValue(4));

            var cardioRaw = reader.IsDBNull(5) ? null : reader.GetString(5).Trim();
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

            var roomRaw = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();
            var roomName = string.IsNullOrWhiteSpace(roomRaw) ? null : roomRaw;
            var siteRaw = reader.IsDBNull(7) ? null : reader.GetString(7).Trim();
            var siteName = string.IsNullOrWhiteSpace(siteRaw) ? null : siteRaw;

            var appointmentDateRaw = reader.IsDBNull(8) ? null : reader.GetValue(8);
            var appointmentDate = ReadDateOnly(appointmentDateRaw)
                ?? from
                ?? DateOnly.FromDateTime(DateTime.Today);
            var appointmentTime = FormatAppointmentTime(reader.IsDBNull(9) ? null : reader.GetValue(9))
                ?? FormatAppointmentTimeFromDate(appointmentDateRaw);

            var note = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
            if (string.IsNullOrWhiteSpace(note))
                note = null;

            var serviceTypeId = reader.IsDBNull(11) ? "" : reader.GetString(11).Trim();
            var serviceTitle = reader.IsDBNull(12) ? "" : reader.GetString(12).Trim();
            if (string.IsNullOrWhiteSpace(serviceTitle))
                serviceTitle = Cm2RecallServiceTypes.ResolveTitle(
                    int.TryParse(serviceTypeId, out var sid) ? sid : 0) ?? "Recall";

            rows.Add(new PinkRecallWorklistRow(
                appointmentId,
                lastName,
                firstName,
                patientCmId,
                dob,
                string.IsNullOrWhiteSpace(cardioId) ? null : cardioId,
                cardioName,
                roomName,
                siteName,
                appointmentDate,
                appointmentTime,
                note,
                serviceTypeId,
                serviceTitle));
        }

        return new PinkRecallWorklistResult(
            from,
            to,
            string.IsNullOrWhiteSpace(siteCode) ? null : Cm2SiteMapping.ResolveDisplayName(siteCode),
            string.IsNullOrWhiteSpace(cardioFilter)
                ? null
                : Cm2CardiologistMapping.IsUnassignedFilter(oracleCardioId)
                    ? Cm2CardiologistMapping.UnassignedFilterId
                    : oracleCardioId,
            rows.Count,
            rows);
    }

    public static async Task<(bool Ok, string? Error)> UpdateAppointmentNoteAsync(
        string connectionString,
        string schema,
        string appointmentId,
        string? note,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (string.IsNullOrWhiteSpace(appointmentId)
            || !decimal.TryParse(appointmentId.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            return (false, "Invalid appointment id");

        var trimmed = note?.Trim() ?? "";
        if (trimmed.Length > 2000)
            return (false, "Note cannot exceed 2000 characters");

        object noteValue = string.IsNullOrEmpty(trimmed) ? DBNull.Value : trimmed;

        var sql = $"""
            UPDATE {schema}.SCHEDULE_APPOINTMENT
               SET APPOINTMENT_NOTE = :Note
             WHERE SCHEDULE_APPOINTMENT_RID = :AppointmentRid
            """;

        try
        {
            await using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = sql;
            cmd.Parameters.Add("Note", OracleDbType.Varchar2, 2000).Value = noteValue;
            cmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected == 0)
                return (false, "Appointment not found");

            return (true, null);
        }
        catch (OracleException ex) when (ex.Number == 1031)
        {
            return (false, "Oracle user lacks UPDATE on appointment notes. Ask DBA to grant UPDATE (APPOINTMENT_NOTE) on SCHEDULE_APPOINTMENT.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<(bool Ok, string? Error, string? ServiceTypeId, string? ServiceTitle)> UpdateAppointmentServiceTypeAsync(
        string connectionString,
        string schema,
        string appointmentId,
        string serviceTypeId,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (string.IsNullOrWhiteSpace(appointmentId)
            || !decimal.TryParse(appointmentId.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            return (false, "Invalid appointment id", null, null);

        if (!int.TryParse(serviceTypeId?.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var occRid)
            || !Cm2RecallServiceTypes.IsAllowedChange(occRid))
            return (false, "Service type must be Cardiology Recall or Admin Recall", null, null);

        var title = Cm2RecallServiceTypes.ResolveTitle(occRid) ?? occRid.ToString();

        var sql = $"""
            UPDATE {schema}.SCHEDULE_APPOINTMENT
               SET SCHEDULE_OCC_SERVICE_RID = :ServiceTypeId
             WHERE SCHEDULE_APPOINTMENT_RID = :AppointmentRid
            """;

        try
        {
            await using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = sql;
            cmd.Parameters.Add("ServiceTypeId", OracleDbType.Decimal).Value = occRid;
            cmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected == 0)
                return (false, "Appointment not found", null, null);

            return (true, null, occRid.ToString(System.Globalization.CultureInfo.InvariantCulture), title);
        }
        catch (OracleException ex) when (ex.Number == 1031)
        {
            return (false, "Oracle user lacks UPDATE on appointment service type. Ask DBA to grant UPDATE (SCHEDULE_OCC_SERVICE_RID) on SCHEDULE_APPOINTMENT.", null, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null, null);
        }
    }

    /// <summary>Marks the diary booking arrived (APPOINTMENT_ARRIVED = 1).</summary>
    public static async Task<(bool Ok, string? Error)> ArriveAppointmentAsync(
        string connectionString,
        string schema,
        string appointmentId,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (string.IsNullOrWhiteSpace(appointmentId)
            || !decimal.TryParse(appointmentId.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            return (false, "Invalid appointment id");

        var sql = $"""
            UPDATE {schema}.SCHEDULE_APPOINTMENT
               SET APPOINTMENT_ARRIVED = 1
             WHERE SCHEDULE_APPOINTMENT_RID = :AppointmentRid
            """;

        try
        {
            await using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = sql;
            cmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;

            var affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected == 0)
                return (false, "Appointment not found");

            return (true, null);
        }
        catch (OracleException ex) when (ex.Number == 1031)
        {
            return (false, "Oracle user lacks UPDATE on appointment arrived. Ask DBA to grant UPDATE (APPOINTMENT_ARRIVED) on SCHEDULE_APPOINTMENT.");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Moves a diary booking. CM2 stores the clock time on APPOINTMENT_DATE (APPOINTMENT_TIME is unused).
    /// Duration is in 15-minute diary slots; rejects moves that overlap another booking in the same room.
    /// </summary>
    public static async Task<(bool Ok, string? Error, DateOnly? Date, string? Time)> UpdateAppointmentScheduleAsync(
        string connectionString,
        string schema,
        string appointmentId,
        string date,
        string? time,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (string.IsNullOrWhiteSpace(appointmentId)
            || !decimal.TryParse(appointmentId.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
            return (false, "Invalid appointment id", null, null);

        if (!DateOnly.TryParse(date?.Trim(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dateOnly))
            return (false, "Invalid appointment date", null, null);

        TimeOnly timeOnly = TimeOnly.MinValue;
        var timeTrimmed = time?.Trim() ?? "";
        if (!string.IsNullOrEmpty(timeTrimmed))
        {
            if (!TimeOnly.TryParseExact(timeTrimmed, ["HH:mm", "H:mm", "HH:mm:ss"],
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out timeOnly))
                return (false, "Invalid appointment time (use HH:mm)", null, null);
        }

        var when = dateOnly.ToDateTime(timeOnly);
        var displayTime = timeOnly == TimeOnly.MinValue
            ? null
            : $"{timeOnly.Hour:D2}:{timeOnly.Minute:D2}";

        try
        {
            await using var conn = new OracleConnection(connectionString);
            await conn.OpenAsync(ct);

            decimal? roomRid = null;
            var durationSlots = 1;
            await using (var loadCmd = conn.CreateCommand())
            {
                loadCmd.BindByName = true;
                loadCmd.CommandText = $"""
                    SELECT SCHEDULE_ROOM_RID, NVL(APPOINTMENT_DURATION, 1)
                      FROM {schema}.SCHEDULE_APPOINTMENT
                     WHERE SCHEDULE_APPOINTMENT_RID = :AppointmentRid
                    """;
                loadCmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;
                await using var reader = await loadCmd.ExecuteReaderAsync(ct);
                if (!await reader.ReadAsync(ct))
                    return (false, "Appointment not found", null, null);

                if (!reader.IsDBNull(0))
                    roomRid = reader.GetDecimal(0);
                durationSlots = Math.Max(1, Convert.ToInt32(reader.GetDecimal(1)));
            }

            var newEnd = when.AddMinutes(durationSlots * DiarySlotMinutes);

            if (roomRid.HasValue)
            {
                await using var clashCmd = conn.CreateCommand();
                clashCmd.BindByName = true;
                // Overlap: other.start < newEnd AND other.end > newStart
                // other.end = other.start + NVL(duration,1) * 15 minutes
                clashCmd.CommandText = $"""
                    SELECT * FROM (
                      SELECT TO_CHAR(O.APPOINTMENT_DATE, 'HH24:MI') AS StartTime,
                             TO_CHAR(
                               O.APPOINTMENT_DATE
                               + (NVL(O.APPOINTMENT_DURATION, 1) * :SlotMinutes / 1440),
                               'HH24:MI') AS EndTime,
                             NVL(O.APPOINTMENT_DURATION, 1) AS DurationSlots,
                             TRIM(NVL(R.ROOM_NAME, '')) AS RoomName,
                             TRIM(NVL(P.PAT_LASTNAME, '')) AS LastName,
                             TRIM(NVL(P.PAT_FIRSTNAME, '')) AS FirstName,
                             TRIM(NVL(S.OCC_SERVICE_TITLE, '')) AS ServiceTitle
                        FROM {schema}.SCHEDULE_APPOINTMENT O
                        LEFT JOIN {schema}.SCHEDULE_ROOM R
                          ON R.SCHEDULE_ROOM_RID = O.SCHEDULE_ROOM_RID
                        LEFT JOIN {schema}.PATIENT P
                          ON P.PATIENT_RID = O.PATIENT_RID
                        LEFT JOIN {schema}.SCHEDULE_OCC_SERVICE S
                          ON S.SCHEDULE_OCC_SERVICE_RID = O.SCHEDULE_OCC_SERVICE_RID
                       WHERE O.SCHEDULE_ROOM_RID = :RoomRid
                         AND O.SCHEDULE_APPOINTMENT_RID <> :AppointmentRid
                         AND O.APPOINTMENT_DATE < :NewEnd
                         AND O.APPOINTMENT_DATE
                             + (NVL(O.APPOINTMENT_DURATION, 1) * :SlotMinutes / 1440) > :NewStart
                       ORDER BY O.APPOINTMENT_DATE
                    ) WHERE ROWNUM = 1
                    """;
                clashCmd.Parameters.Add("RoomRid", OracleDbType.Decimal).Value = roomRid.Value;
                clashCmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;
                clashCmd.Parameters.Add("NewEnd", OracleDbType.Date).Value = newEnd;
                clashCmd.Parameters.Add("NewStart", OracleDbType.Date).Value = when;
                clashCmd.Parameters.Add("SlotMinutes", OracleDbType.Decimal).Value = DiarySlotMinutes;

                await using var clashReader = await clashCmd.ExecuteReaderAsync(ct);
                if (await clashReader.ReadAsync(ct))
                {
                    var startTime = clashReader.IsDBNull(0) ? "?" : clashReader.GetString(0);
                    var endTime = clashReader.IsDBNull(1) ? "?" : clashReader.GetString(1);
                    var otherSlots = clashReader.IsDBNull(2) ? 1 : Convert.ToInt32(clashReader.GetDecimal(2));
                    var roomName = clashReader.IsDBNull(3) || string.IsNullOrWhiteSpace(clashReader.GetString(3))
                        ? "that room"
                        : clashReader.GetString(3).Trim();
                    var last = clashReader.IsDBNull(4) ? "" : clashReader.GetString(4).Trim();
                    var first = clashReader.IsDBNull(5) ? "" : clashReader.GetString(5).Trim();
                    var service = clashReader.IsDBNull(6) ? "" : clashReader.GetString(6).Trim();
                    var who = string.Join(" ", new[] { last.ToUpperInvariant(), first }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    if (string.IsNullOrWhiteSpace(who))
                        who = "another patient";
                    var serviceBit = string.IsNullOrWhiteSpace(service) ? "" : $" ({service})";

                    var ownEnd = newEnd.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    var ownStart = when.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
                    return (false,
                        $"Clash: {roomName} is occupied {startTime}–{endTime} by {who}{serviceBit} ({otherSlots} slot{(otherSlots == 1 ? "" : "s")}). Your move {ownStart}–{ownEnd} overlaps that booking.",
                        null, null);
                }
            }

            await using var updateCmd = conn.CreateCommand();
            updateCmd.BindByName = true;
            updateCmd.CommandText = $"""
                UPDATE {schema}.SCHEDULE_APPOINTMENT
                   SET APPOINTMENT_DATE = :AppointmentDate
                 WHERE SCHEDULE_APPOINTMENT_RID = :AppointmentRid
                """;
            updateCmd.Parameters.Add("AppointmentDate", OracleDbType.Date).Value = when;
            updateCmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;

            var affected = await updateCmd.ExecuteNonQueryAsync(ct);
            if (affected == 0)
                return (false, "Appointment not found", null, null);

            return (true, null, dateOnly, displayTime);
        }
        catch (OracleException ex) when (ex.Number == 1031)
        {
            return (false, "Oracle user lacks UPDATE on appointment date. Ask DBA to grant UPDATE (APPOINTMENT_DATE) on SCHEDULE_APPOINTMENT.", null, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message, null, null);
        }
    }

    /// <summary>
    /// Free start times in the appointment's room on a given day that fit its duration
    /// without overlapping other bookings (duration counted in 15-minute diary slots).
    /// </summary>
    public static async Task<AvailableDiarySlotsResult> GetAvailableDiarySlotsAsync(
        string connectionString,
        string schema,
        string appointmentId,
        string date,
        CancellationToken ct = default)
    {
        schema = NormalizeSchema(schema);
        if (string.IsNullOrWhiteSpace(appointmentId)
            || !decimal.TryParse(appointmentId.Trim(), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var rid))
        {
            return new AvailableDiarySlotsResult(
                appointmentId ?? "", date ?? "", null, null, 1, DiarySlotMinutes, null, [],
                "Invalid appointment id");
        }

        if (!DateOnly.TryParse(date?.Trim(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var dateOnly))
        {
            return new AvailableDiarySlotsResult(
                rid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                date ?? "", null, null, 1, DiarySlotMinutes, null, [],
                "Invalid date");
        }

        var dayStart = dateOnly.ToDateTime(new TimeOnly(DiaryDayStartHour, 0));
        var dayEnd = dateOnly.ToDateTime(new TimeOnly(DiaryDayEndHour, 0));

        await using var conn = new OracleConnection(connectionString);
        await conn.OpenAsync(ct);

        decimal? roomRid = null;
        string? roomName = null;
        var durationSlots = 1;
        string? currentTime = null;

        await using (var loadCmd = conn.CreateCommand())
        {
            loadCmd.BindByName = true;
            loadCmd.CommandText = $"""
                SELECT A.SCHEDULE_ROOM_RID,
                       NVL(A.APPOINTMENT_DURATION, 1),
                       A.APPOINTMENT_DATE,
                       TRIM(NVL(R.ROOM_NAME, ''))
                  FROM {schema}.SCHEDULE_APPOINTMENT A
                  LEFT JOIN {schema}.SCHEDULE_ROOM R
                    ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
                 WHERE A.SCHEDULE_APPOINTMENT_RID = :AppointmentRid
                """;
            loadCmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;
            await using var reader = await loadCmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
            {
                return new AvailableDiarySlotsResult(
                    rid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                    null, null, 1, DiarySlotMinutes, null, [],
                    "Appointment not found");
            }

            if (!reader.IsDBNull(0))
                roomRid = reader.GetDecimal(0);
            durationSlots = Math.Max(1, Convert.ToInt32(reader.GetDecimal(1)));
            if (!reader.IsDBNull(2))
            {
                var when = reader.GetDateTime(2);
                if (DateOnly.FromDateTime(when) == dateOnly && (when.Hour != 0 || when.Minute != 0))
                    currentTime = $"{when.Hour:D2}:{when.Minute:D2}";
            }
            if (!reader.IsDBNull(3))
            {
                var name = reader.GetString(3).Trim();
                roomName = string.IsNullOrWhiteSpace(name) ? null : name;
            }
        }

        if (!roomRid.HasValue)
        {
            return new AvailableDiarySlotsResult(
                rid.ToString(System.Globalization.CultureInfo.InvariantCulture),
                dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
                null, roomName, durationSlots, DiarySlotMinutes, currentTime, [],
                "Appointment has no room assigned — cannot list free slots");
        }

        var occupied = new List<(DateTime Start, DateTime End)>();
        await using (var occCmd = conn.CreateCommand())
        {
            occCmd.BindByName = true;
            occCmd.CommandText = $"""
                SELECT A.APPOINTMENT_DATE, NVL(A.APPOINTMENT_DURATION, 1)
                  FROM {schema}.SCHEDULE_APPOINTMENT A
                 WHERE A.SCHEDULE_ROOM_RID = :RoomRid
                   AND A.SCHEDULE_APPOINTMENT_RID <> :AppointmentRid
                   AND A.APPOINTMENT_DATE >= :DayStart
                   AND A.APPOINTMENT_DATE < :DayEndExclusive
                 ORDER BY A.APPOINTMENT_DATE
                """;
            occCmd.Parameters.Add("RoomRid", OracleDbType.Decimal).Value = roomRid.Value;
            occCmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;
            occCmd.Parameters.Add("DayStart", OracleDbType.Date).Value = dayStart;
            // Include bookings that start before midnight next day; long blocks may start earlier
            // but still occupy into this day — widen lower bound by max reasonable duration.
            occCmd.Parameters.Add("DayEndExclusive", OracleDbType.Date).Value = dayEnd;

            await using var occReader = await occCmd.ExecuteReaderAsync(ct);
            while (await occReader.ReadAsync(ct))
            {
                if (occReader.IsDBNull(0))
                    continue;
                var start = occReader.GetDateTime(0);
                var slots = Math.Max(1, Convert.ToInt32(occReader.GetDecimal(1)));
                occupied.Add((start, start.AddMinutes(slots * DiarySlotMinutes)));
            }
        }

        // Also catch long bookings that started before dayStart but still occupy into the day.
        await using (var spillCmd = conn.CreateCommand())
        {
            spillCmd.BindByName = true;
            spillCmd.CommandText = $"""
                SELECT A.APPOINTMENT_DATE, NVL(A.APPOINTMENT_DURATION, 1)
                  FROM {schema}.SCHEDULE_APPOINTMENT A
                 WHERE A.SCHEDULE_ROOM_RID = :RoomRid
                   AND A.SCHEDULE_APPOINTMENT_RID <> :AppointmentRid
                   AND A.APPOINTMENT_DATE < :DayStart
                   AND A.APPOINTMENT_DATE
                       + (NVL(A.APPOINTMENT_DURATION, 1) * :SlotMinutes / 1440) > :DayStart
                """;
            spillCmd.Parameters.Add("RoomRid", OracleDbType.Decimal).Value = roomRid.Value;
            spillCmd.Parameters.Add("AppointmentRid", OracleDbType.Decimal).Value = rid;
            spillCmd.Parameters.Add("DayStart", OracleDbType.Date).Value = dayStart;
            spillCmd.Parameters.Add("SlotMinutes", OracleDbType.Decimal).Value = DiarySlotMinutes;
            await using var spillReader = await spillCmd.ExecuteReaderAsync(ct);
            while (await spillReader.ReadAsync(ct))
            {
                if (spillReader.IsDBNull(0))
                    continue;
                var start = spillReader.GetDateTime(0);
                var slots = Math.Max(1, Convert.ToInt32(spillReader.GetDecimal(1)));
                occupied.Add((start, start.AddMinutes(slots * DiarySlotMinutes)));
            }
        }

        var needMinutes = durationSlots * DiarySlotMinutes;
        var free = new List<string>();
        for (var t = dayStart; t.AddMinutes(needMinutes) <= dayEnd; t = t.AddMinutes(DiarySlotMinutes))
        {
            var end = t.AddMinutes(needMinutes);
            var clash = occupied.Any(o => o.Start < end && o.End > t);
            if (!clash)
                free.Add($"{t.Hour:D2}:{t.Minute:D2}");
        }

        // Keep current time selectable if this booking is already on that day (even if logic
        // somehow excluded it — exclude-self should already include it).
        if (!string.IsNullOrWhiteSpace(currentTime)
            && !free.Contains(currentTime, StringComparer.Ordinal)
            && TimeOnly.TryParseExact(currentTime, "HH:mm",
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var keep))
        {
            var keepStart = dateOnly.ToDateTime(keep);
            var keepEnd = keepStart.AddMinutes(needMinutes);
            if (keepStart >= dayStart && keepEnd <= dayEnd
                && !occupied.Any(o => o.Start < keepEnd && o.End > keepStart))
            {
                free.Add(currentTime);
                free.Sort(StringComparer.Ordinal);
            }
        }

        string? message = null;
        if (free.Count == 0)
            message = $"No free {durationSlots}-slot openings in {roomName ?? "this room"} on {dateOnly:dd/MM/yyyy} ({DiaryDayStartHour:00}:00–{DiaryDayEndHour:00}:00).";

        return new AvailableDiarySlotsResult(
            rid.ToString(System.Globalization.CultureInfo.InvariantCulture),
            dateOnly.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            roomRid.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            roomName,
            durationSlots,
            DiarySlotMinutes,
            currentTime,
            free,
            message);
    }

    /// <summary>CM2 diary slot length used by APPOINTMENT_DURATION.</summary>
    private const int DiarySlotMinutes = 15;
    private const int DiaryDayStartHour = 7;
    private const int DiaryDayEndHour = 20;

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
            digits = Convert.ToInt32(Convert.ToDecimal(value)).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        else
        {
            digits = System.Text.RegularExpressions.Regex.Replace(Convert.ToString(value) ?? "", @"\D", "");
        }

        if (digits.Length is < 3 or > 4)
            return null;
        if (digits.Length == 3)
            digits = "0" + digits;
        if (!int.TryParse(digits.AsSpan(0, 2), out var hour) || !int.TryParse(digits.AsSpan(2, 2), out var minute))
            return null;
        if (hour is < 0 or > 23 || minute is < 0 or > 59)
            return null;
        return $"{hour:D2}:{minute:D2}";
    }

    private static string? FormatAppointmentTimeFromDate(object? value)
    {
        if (value is DateTime dt && (dt.Hour != 0 || dt.Minute != 0))
            return $"{dt.Hour:D2}:{dt.Minute:D2}";
        return null;
    }
}
