using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using H1Stats.Core.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Oracle.ManagedDataAccess.Client;
using System.Text.RegularExpressions;

namespace H1Stats.Infrastructure.Data;

/// <summary>
/// Past TEE/IOE diary bookings (CM2) matched to Syngo studies (H1PACS) and CM2 TEST reports.
/// Complete rows (study on Syngo and TEST.REPORT_PATH present) are omitted.
/// </summary>
public sealed class TeeIoeProgressRepository : ITeeIoeProgressRepository
{
    /// <summary>Match a Syngo study / CM2 TEST to a booking when dated on the appointment day through this many days after.</summary>
    private const int MatchWindowDays = 7;

    private readonly DatabaseSettings _settings;

    public TeeIoeProgressRepository(IOptions<DatabaseSettings> options) =>
        _settings = options.Value;

    private string Schema =>
        string.IsNullOrWhiteSpace(_settings.Cm2Schema)
            ? "HEARTS1ST"
            : _settings.Cm2Schema.Trim().ToUpperInvariant();

    public async Task<TeeIoeProgressResult> GetReportAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default)
    {
        (dateFrom, dateTo) = ClampToPast(dateFrom, dateTo);
        var oracleCardioId = Cm2CardiologistMapping.ResolveOracleId(cardiologistId);

        if (dateFrom > dateTo)
            return EmptyResult(dateFrom, dateTo, oracleCardioId);

        var bookings = await LoadBookingsAsync(dateFrom, dateTo, cardiologistId, ct);
        var tests = await LoadTeeTestsAsync(
            dateFrom.AddDays(-MatchWindowDays), dateTo.AddDays(MatchWindowDays), ct);
        var studies = await LoadSyngoStudiesAsync(dateFrom, dateTo.AddDays(MatchWindowDays), ct);

        var usedStudyKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var incomplete = new List<TeeIoeProgressRow>();

        foreach (var booking in bookings)
        {
            var test = FindMatchingTest(tests, booking);
            var study = FindMatchingStudy(studies, booking);
            var hasReport = test is { HasReport: true };
            var inSyngo = study is not null;
            if (study is not null)
                usedStudyKeys.Add(StudyKey(study));

            if (inSyngo && hasReport)
                continue;

            string status;
            if (inSyngo)
                status = TeeIoeProgressStatus.AwaitingReport;
            else if (hasReport)
                status = TeeIoeProgressStatus.NotInSyngo;
            else if (booking.Arrived)
                status = TeeIoeProgressStatus.Arrived;
            else
                status = TeeIoeProgressStatus.Booked;

            incomplete.Add(ToRow(booking, study, test, inSyngo, hasReport, status, 1));
        }

        var unmatchedStudies = studies
            .Where(s => s.StudyDate >= dateFrom && s.StudyDate <= dateTo && !usedStudyKeys.Contains(StudyKey(s)))
            .ToList();

        var patientByCmId = await LoadPatientsByCmIdsAsync(
            unmatchedStudies.Select(s => s.PatientId).Where(id => !string.IsNullOrWhiteSpace(id)),
            ct);

        foreach (var study in unmatchedStudies)
        {
            var test = FindMatchingTestForStudy(tests, study);
            if (test is { HasReport: true })
                continue;

            var patient = FindPatient(patientByCmId, study);
            if (!PassesCardiologistFilter(cardiologistId, patient?.CardiologistId))
                continue;

            var kind = BookingKindFromSyngo(study.StudyDescription);
            var hasTest = test is not null;
            var status = hasTest
                ? TeeIoeProgressStatus.AwaitingReport
                : TeeIoeProgressStatus.NotInCm2;

            var cardioId = patient?.CardiologistId;
            incomplete.Add(new TeeIoeProgressRow(
                $"syngo:{study.PatientId}:{study.StudyDate:yyyyMMdd}",
                patient?.PatientRid ?? "",
                FirstNonBlank(study.PatientId, patient?.PatientCmId),
                FirstNonBlank(study.LastName, patient?.LastName),
                FirstNonBlank(study.FirstName, patient?.FirstName),
                study.DateOfBirth ?? patient?.DateOfBirth,
                cardioId,
                string.IsNullOrWhiteSpace(cardioId)
                    ? null
                    : Cm2CardiologistMapping.ResolveDisplayName(cardioId),
                study.StudyDate,
                null,
                false,
                hasTest
                    ? "Syngo study with a CM2 TEST but no report yet."
                    : "Syngo study with no matching CM2 diary booking or TEST.",
                null,
                null,
                hasTest
                    ? "CM2 TEST — no report"
                    : "Syngo study — no CM2 booking",
                kind,
                true,
                false,
                study.StudyDate,
                study.StudyDescription,
                test?.TestDate,
                status,
                1));
        }

        var rows = incomplete
            .GroupBy(PatientGroupKey)
            .Select(g =>
            {
                var latest = g
                    .OrderByDescending(r => r.AppointmentDate)
                    .ThenByDescending(r => r.AppointmentId, StringComparer.Ordinal)
                    .First();
                return latest with { BookingCount = g.Count() };
            })
            .OrderByDescending(r => r.AppointmentDate)
            .ThenBy(r => r.LastName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.FirstName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.PatientRid, StringComparer.Ordinal)
            .ToList();

        return new TeeIoeProgressResult(
            dateFrom,
            dateTo,
            string.IsNullOrWhiteSpace(oracleCardioId) ? null : oracleCardioId,
            rows.Count,
            rows.Count(r => r.Status == TeeIoeProgressStatus.Booked),
            rows.Count(r => r.Status == TeeIoeProgressStatus.Arrived),
            rows.Count(r => r.Status == TeeIoeProgressStatus.NotInSyngo),
            rows.Count(r => r.Status == TeeIoeProgressStatus.AwaitingReport),
            rows.Count(r => r.Status == TeeIoeProgressStatus.NotInCm2),
            rows);
    }

    private async Task<List<BookingRecord>> LoadBookingsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId,
        CancellationToken ct)
    {
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

        var occIds = string.Join(", ", Cm2DrSummaryMapping.TeeIoeOccServiceIds);

        var sql = $"""
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
              A.SCHEDULE_OCC_SERVICE_RID AS OccServiceRid
            FROM {Schema}.SCHEDULE_APPOINTMENT A
            JOIN {Schema}.PATIENT P
              ON P.PATIENT_RID = A.PATIENT_RID
            JOIN {Schema}.SCHEDULE_OCC_SERVICE S
              ON S.SCHEDULE_OCC_SERVICE_RID = A.SCHEDULE_OCC_SERVICE_RID
            LEFT JOIN {Schema}.SCHEDULE_ROOM R
              ON R.SCHEDULE_ROOM_RID = A.SCHEDULE_ROOM_RID
            LEFT JOIN {Schema}.SCHEDULE_SITE ST
              ON ST.SCHEDULE_SITE_RID = R.SCHEDULE_SITE_RID
            WHERE A.SCHEDULE_OCC_SERVICE_RID IN ({occIds})
              AND A.APPOINTMENT_DATE >= :DateFrom
              AND A.APPOINTMENT_DATE < :DateToExclusive
              AND UPPER(TRIM(NVL(P.PAT_FIRSTNAME, ' '))) <> 'AADMIN'
              {cardioFilter}
            ORDER BY A.APPOINTMENT_DATE DESC, A.SCHEDULE_APPOINTMENT_RID DESC
            """;

        var rows = new List<BookingRecord>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value =
            dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);

        if (!string.IsNullOrWhiteSpace(cardioFilter)
            && !Cm2CardiologistMapping.IsUnassignedFilter(oracleCardioId))
        {
            cmd.Parameters.Add("CardiologistId", OracleDbType.Decimal).Value =
                Convert.ToDecimal(oracleCardioId);
        }

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var appointmentId = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(appointmentId))
                continue;

            var lastName = reader.IsDBNull(3) ? "" : reader.GetString(3).Trim();
            if (lastName.Length > 0)
                lastName = lastName.ToUpperInvariant();
            var firstName = StripLeadingSalutation(reader.IsDBNull(4) ? "" : reader.GetString(4).Trim());

            var cardioRaw = reader.IsDBNull(6) ? null : reader.GetString(6).Trim();
            var cardioId = Cm2CardiologistMapping.NormalizeId(cardioRaw);
            if (string.IsNullOrWhiteSpace(cardioId)
                || Cm2CardiologistMapping.IsUndefinedId(cardioId)
                || cardioId == "0")
            {
                cardioId = "";
            }

            var appointmentDateRaw = reader.IsDBNull(7) ? null : reader.GetValue(7);
            var appointmentDate = ReadDateOnly(appointmentDateRaw) ?? dateFrom;

            var occRid = reader.IsDBNull(14) ? 0 : Convert.ToInt32(reader.GetDecimal(14));
            var serviceTitle = reader.IsDBNull(13) ? "" : reader.GetString(13).Trim();
            if (string.IsNullOrWhiteSpace(serviceTitle))
                serviceTitle = Cm2DrSummaryMapping.IsIoeOccService(occRid)
                    ? "Intra-operative Transoesophageal Echo"
                    : "Transoesophageal Echo";

            var note = reader.IsDBNull(10) ? null : reader.GetString(10).Trim();
            if (string.IsNullOrWhiteSpace(note)) note = null;
            var room = reader.IsDBNull(11) ? null : reader.GetString(11).Trim();
            if (string.IsNullOrWhiteSpace(room)) room = null;
            var site = reader.IsDBNull(12) ? null : reader.GetString(12).Trim();
            if (string.IsNullOrWhiteSpace(site)) site = null;

            rows.Add(new BookingRecord(
                appointmentId,
                reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                reader.IsDBNull(2) ? "" : reader.GetString(2).Trim(),
                lastName,
                firstName,
                reader.IsDBNull(5) ? null : ReadDateOnly(reader.GetValue(5)),
                string.IsNullOrWhiteSpace(cardioId) ? null : cardioId,
                string.IsNullOrWhiteSpace(cardioId) ? null : Cm2CardiologistMapping.ResolveDisplayName(cardioId),
                appointmentDate,
                FormatAppointmentTime(reader.IsDBNull(8) ? null : reader.GetValue(8))
                    ?? FormatAppointmentTimeFromDate(appointmentDateRaw),
                !reader.IsDBNull(9) && Convert.ToInt32(reader.GetDecimal(9)) != 0,
                note,
                room,
                site,
                serviceTitle,
                Cm2DrSummaryMapping.IsIoeOccService(occRid) ? "IOE" : "TEE"));
        }

        return rows;
    }

    private async Task<List<TestRecord>> LoadTeeTestsAsync(
        DateOnly dateFrom, DateOnly dateTo, CancellationToken ct)
    {
        var sql = $"""
            SELECT
              TRIM(TO_CHAR(T.PATIENT_RID)) AS PatientRid,
              NVL(NULLIF(TRIM(TO_CHAR(P.PAT_ID)), ''), '') AS PatientCmId,
              TRIM(NVL(P.PAT_LASTNAME, '')) AS LastName,
              TRIM(NVL(P.PAT_FIRSTNAME, '')) AS FirstName,
              P.PAT_DOB,
              T.TEST_DATE,
              TRIM(NVL(T.REPORT_PATH, '')) AS ReportPath
            FROM {Schema}.TEST T
            LEFT JOIN {Schema}.PATIENT P
              ON P.PATIENT_RID = T.PATIENT_RID
            WHERE T.TEST_REQUIRED = :TestRequired
              AND T.TEST_DATE >= :DateFrom
              AND T.TEST_DATE < :DateToExclusive
            """;

        var rows = new List<TestRecord>();
        await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.BindByName = true;
        cmd.CommandText = sql;
        cmd.Parameters.Add("TestRequired", OracleDbType.Decimal).Value =
            Cm2DrSummaryMapping.TransoesophagealEcho;
        cmd.Parameters.Add("DateFrom", OracleDbType.Date).Value = dateFrom.ToDateTime(TimeOnly.MinValue);
        cmd.Parameters.Add("DateToExclusive", OracleDbType.Date).Value =
            dateTo.AddDays(1).ToDateTime(TimeOnly.MinValue);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var patientRid = reader.IsDBNull(0) ? "" : reader.GetString(0).Trim();
            if (string.IsNullOrWhiteSpace(patientRid))
                continue;
            var lastName = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim().ToUpperInvariant();
            var firstName = StripLeadingSalutation(reader.IsDBNull(3) ? "" : reader.GetString(3).Trim());
            var testDate = ReadDateOnly(reader.IsDBNull(5) ? null : reader.GetValue(5));
            if (testDate is null)
                continue;
            var path = reader.IsDBNull(6) ? "" : reader.GetString(6).Trim();
            rows.Add(new TestRecord(
                patientRid,
                reader.IsDBNull(1) ? "" : reader.GetString(1).Trim(),
                lastName,
                firstName,
                reader.IsDBNull(4) ? null : ReadDateOnly(reader.GetValue(4)),
                testDate.Value,
                !string.IsNullOrWhiteSpace(path)));
        }

        return rows;
    }

    private async Task<List<SyngoStudyRecord>> LoadSyngoStudiesAsync(
        DateOnly dateFrom, DateOnly dateTo, CancellationToken ct)
    {
        var sql = $"""
            SELECT
                NULLIF(LTRIM(RTRIM(d.PATIENT_ID)), '') AS PatientId,
                NULLIF(LTRIM(RTRIM(d.PATIENT_NAME)), '') AS PatientName,
                NULLIF(LTRIM(RTRIM(d.PATIENT_BIRTH_DATE)), '') AS BirthDate,
                NULLIF(LTRIM(RTRIM(d.STUDY_DATE)), '') AS StudyDate,
                NULLIF(LTRIM(RTRIM(d.STUDY_DESCRIPTION)), '') AS StudyDescription
            FROM [{_settings.MvfDatabase}].dbo.DOSR_STUDY d
            WHERE d.STUDY_DATE >= @DateFrom
              AND d.STUDY_DATE <= @DateTo
              AND (
                    NULLIF(LTRIM(RTRIM(d.STUDY_DESCRIPTION)), '') IN (N'TEE', N'IOE', N'TEE UD')
                 OR d.STUDY_DESCRIPTION LIKE N'%TEE%'
                 OR d.STUDY_DESCRIPTION LIKE N'%IOE%'
              )
            """;

        var rows = new List<SyngoStudyRecord>();
        await using var conn = new SqlConnection(_settings.MvfConnectionString);
        await conn.OpenAsync(ct);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("@DateFrom", dateFrom.ToString("yyyyMMdd"));
        cmd.Parameters.AddWithValue("@DateTo", dateTo.ToString("yyyyMMdd"));

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var studyDateRaw = reader.IsDBNull(3) ? null : reader.GetString(3);
            var studyDate = ParseSyngoDate(studyDateRaw);
            if (studyDate is null)
                continue;

            var (lastName, firstName) = ParseSyngoName(reader.IsDBNull(1) ? null : reader.GetString(1));
            rows.Add(new SyngoStudyRecord(
                NormalizePatientId(reader.IsDBNull(0) ? null : reader.GetString(0)),
                lastName,
                firstName,
                ParseSyngoDate(reader.IsDBNull(2) ? null : reader.GetString(2)),
                studyDate.Value,
                reader.IsDBNull(4) ? null : NullIfBlank(reader.GetString(4))));
        }

        return rows;
    }

    private static TestRecord? FindMatchingTest(List<TestRecord> tests, BookingRecord booking)
    {
        var windowEnd = booking.AppointmentDate.AddDays(MatchWindowDays);
        return tests
            .Where(t =>
                string.Equals(t.PatientRid, booking.PatientRid, StringComparison.Ordinal)
                && t.TestDate >= booking.AppointmentDate
                && t.TestDate <= windowEnd)
            .OrderByDescending(t => t.HasReport)
            .ThenBy(t => t.TestDate)
            .FirstOrDefault();
    }

    private static SyngoStudyRecord? FindMatchingStudy(List<SyngoStudyRecord> studies, BookingRecord booking)
    {
        var windowEnd = booking.AppointmentDate.AddDays(MatchWindowDays);
        var inWindow = studies.Where(s =>
            s.StudyDate >= booking.AppointmentDate && s.StudyDate <= windowEnd);

        var byId = NormalizePatientId(booking.PatientCmId);
        if (!string.IsNullOrWhiteSpace(byId))
        {
            var idMatch = inWindow
                .Where(s => string.Equals(s.PatientId, byId, StringComparison.OrdinalIgnoreCase))
                .OrderBy(s => s.StudyDate)
                .FirstOrDefault();
            if (idMatch is not null)
                return idMatch;
        }

        if (string.IsNullOrWhiteSpace(booking.LastName) || booking.DateOfBirth is null)
            return null;

        return inWindow
            .Where(s =>
                s.DateOfBirth == booking.DateOfBirth
                && string.Equals(s.LastName, booking.LastName, StringComparison.OrdinalIgnoreCase)
                && FirstNameMatches(s.FirstName, booking.FirstName))
            .OrderBy(s => s.StudyDate)
            .FirstOrDefault();
    }

    private static TestRecord? FindMatchingTestForStudy(List<TestRecord> tests, SyngoStudyRecord study)
    {
        var windowStart = study.StudyDate.AddDays(-MatchWindowDays);
        var windowEnd = study.StudyDate.AddDays(MatchWindowDays);
        var inWindow = tests.Where(t => t.TestDate >= windowStart && t.TestDate <= windowEnd);

        var byId = NormalizePatientId(study.PatientId);
        if (!string.IsNullOrWhiteSpace(byId))
        {
            var idMatch = inWindow
                .Where(t => string.Equals(NormalizePatientId(t.PatientCmId), byId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(t => t.HasReport)
                .ThenBy(t => t.TestDate)
                .FirstOrDefault();
            if (idMatch is not null)
                return idMatch;
        }

        if (string.IsNullOrWhiteSpace(study.LastName) || study.DateOfBirth is null)
            return null;

        return inWindow
            .Where(t =>
                t.DateOfBirth == study.DateOfBirth
                && string.Equals(t.LastName, study.LastName, StringComparison.OrdinalIgnoreCase)
                && FirstNameMatches(t.FirstName, study.FirstName))
            .OrderByDescending(t => t.HasReport)
            .ThenBy(t => t.TestDate)
            .FirstOrDefault();
    }

    private async Task<Dictionary<string, PatientRecord>> LoadPatientsByCmIdsAsync(
        IEnumerable<string> rawIds, CancellationToken ct)
    {
        var ids = rawIds
            .Select(NormalizePatientId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var map = new Dictionary<string, PatientRecord>(StringComparer.OrdinalIgnoreCase);
        if (ids.Count == 0)
            return map;

        for (var offset = 0; offset < ids.Count; offset += 200)
        {
            var chunk = ids.Skip(offset).Take(200).ToList();
            var names = string.Join(", ", chunk.Select((_, i) => $":Pid{i}"));
            var sql = $"""
                SELECT
                  TRIM(TO_CHAR(P.PATIENT_RID)) AS PatientRid,
                  NVL(NULLIF(TRIM(TO_CHAR(P.PAT_ID, 'FM999999999999')), ''), '') AS PatientCmId,
                  TRIM(NVL(P.PAT_LASTNAME, '')) AS LastName,
                  TRIM(NVL(P.PAT_FIRSTNAME, '')) AS FirstName,
                  P.PAT_DOB,
                  TRIM(TO_CHAR(P.PAT_CARDIOLOGIST)) AS CardiologistId
                FROM {Schema}.PATIENT P
                WHERE NULLIF(TRIM(TO_CHAR(P.PAT_ID, 'FM999999999999')), '') IS NOT NULL
                  AND TRIM(TO_CHAR(P.PAT_ID, 'FM999999999999')) IN ({names})
                """;

            await using var conn = new OracleConnection(_settings.Cm2OracleConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.BindByName = true;
            cmd.CommandText = sql;
            for (var i = 0; i < chunk.Count; i++)
                cmd.Parameters.Add($"Pid{i}", OracleDbType.Varchar2).Value = chunk[i];

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var cmId = NormalizePatientId(reader.IsDBNull(1) ? null : reader.GetString(1));
                if (string.IsNullOrWhiteSpace(cmId) || map.ContainsKey(cmId))
                    continue;

                var cardioRaw = reader.IsDBNull(5) ? null : reader.GetString(5).Trim();
                var cardioId = Cm2CardiologistMapping.NormalizeId(cardioRaw);
                if (string.IsNullOrWhiteSpace(cardioId)
                    || Cm2CardiologistMapping.IsUndefinedId(cardioId)
                    || cardioId == "0")
                {
                    cardioId = "";
                }

                var lastName = reader.IsDBNull(2) ? "" : reader.GetString(2).Trim();
                if (lastName.Length > 0)
                    lastName = lastName.ToUpperInvariant();

                map[cmId] = new PatientRecord(
                    reader.IsDBNull(0) ? "" : reader.GetString(0).Trim(),
                    cmId,
                    lastName,
                    StripLeadingSalutation(reader.IsDBNull(3) ? "" : reader.GetString(3).Trim()),
                    reader.IsDBNull(4) ? null : ReadDateOnly(reader.GetValue(4)),
                    string.IsNullOrWhiteSpace(cardioId) ? null : cardioId);
            }
        }

        return map;
    }

    private static PatientRecord? FindPatient(
        Dictionary<string, PatientRecord> byCmId, SyngoStudyRecord study)
    {
        var id = NormalizePatientId(study.PatientId);
        if (!string.IsNullOrWhiteSpace(id) && byCmId.TryGetValue(id, out var hit))
            return hit;
        return null;
    }

    private static bool PassesCardiologistFilter(string? filter, string? cardioId)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return true;
        if (Cm2CardiologistMapping.IsUnassignedFilter(filter))
            return string.IsNullOrWhiteSpace(cardioId);
        var wanted = Cm2CardiologistMapping.ResolveOracleId(filter);
        return string.Equals(cardioId, wanted, StringComparison.OrdinalIgnoreCase);
    }

    private static string PatientGroupKey(TeeIoeProgressRow row)
    {
        if (!string.IsNullOrWhiteSpace(row.PatientRid))
            return "rid:" + row.PatientRid;
        var id = NormalizePatientId(row.PatientCmId);
        if (!string.IsNullOrWhiteSpace(id))
            return "id:" + id;
        return $"row:{row.AppointmentId}";
    }

    private static string StudyKey(SyngoStudyRecord study) =>
        $"{NormalizePatientId(study.PatientId)}|{study.StudyDate:yyyyMMdd}|{study.StudyDescription}";

    private static string BookingKindFromSyngo(string? description)
    {
        var d = (description ?? "").Trim();
        if (d.Contains("IOE", StringComparison.OrdinalIgnoreCase))
            return "IOE";
        return "TEE";
    }

    private static string FirstNonBlank(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) ? a.Trim() : (b ?? "").Trim();

    private static TeeIoeProgressRow ToRow(
        BookingRecord booking,
        SyngoStudyRecord? study,
        TestRecord? test,
        bool inSyngo,
        bool hasReport,
        string status,
        int bookingCount) =>
        new(
            booking.AppointmentId,
            booking.PatientRid,
            booking.PatientCmId,
            booking.LastName,
            booking.FirstName,
            booking.DateOfBirth,
            booking.CardiologistId,
            booking.CardiologistName,
            booking.AppointmentDate,
            booking.AppointmentTime,
            booking.Arrived,
            booking.AppointmentNote,
            booking.RoomName,
            booking.SiteName,
            booking.ServiceTitle,
            booking.BookingKind,
            inSyngo,
            hasReport,
            study?.StudyDate,
            study?.StudyDescription,
            test?.TestDate,
            status,
            bookingCount);

    internal static (DateOnly From, DateOnly To) ClampToPast(DateOnly dateFrom, DateOnly dateTo)
    {
        if (dateTo < dateFrom)
            (dateFrom, dateTo) = (dateTo, dateFrom);

        var yesterday = DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
        if (dateTo > yesterday)
            dateTo = yesterday;
        return (dateFrom, dateTo);
    }

    private static TeeIoeProgressResult EmptyResult(DateOnly from, DateOnly to, string? cardioId) =>
        new(from, to, string.IsNullOrWhiteSpace(cardioId) ? null : cardioId, 0, 0, 0, 0, 0, 0, []);

    private static bool FirstNameMatches(string? syngoFirst, string bookingFirst)
    {
        var a = (syngoFirst ?? "").Trim();
        var b = (bookingFirst ?? "").Trim();
        if (a.Length == 0 || b.Length == 0)
            return false;
        return a.StartsWith(b, StringComparison.OrdinalIgnoreCase)
            || b.StartsWith(a, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePatientId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "";
        var s = raw.Trim();
        if (s.EndsWith(".0", StringComparison.Ordinal))
            s = s[..^2];
        s = s.TrimStart('0');
        return s.Length == 0 ? "0" : s;
    }

    private static (string LastName, string FirstName) ParseSyngoName(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return ("", "");
        var s = raw.Trim();
        if (s.Contains('^', StringComparison.Ordinal))
        {
            var parts = s.Split('^');
            return (
                (parts.ElementAtOrDefault(0) ?? "").Trim().ToUpperInvariant(),
                StripLeadingSalutation((parts.ElementAtOrDefault(1) ?? "").Trim()));
        }

        var comma = s.IndexOf(',');
        if (comma >= 0)
        {
            return (
                s[..comma].Trim().ToUpperInvariant(),
                StripLeadingSalutation(s[(comma + 1)..].Trim()));
        }

        return (s.ToUpperInvariant(), "");
    }

    private static DateOnly? ParseSyngoDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digits.Length < 8)
            return ReadDateOnly(raw);
        if (DateOnly.TryParseExact(digits[..8], "yyyyMMdd", out var parsed))
            return parsed;
        return ReadDateOnly(raw);
    }

    private static string StripLeadingSalutation(string firstName)
    {
        var s = firstName.Trim();
        for (var i = 0; i < 5; i++)
        {
            var next = Regex.Replace(
                s, @"^(?:Mr|Mrs|Ms|Miss|Dr|Prof)\.?\s+", "", RegexOptions.IgnoreCase);
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
        if (value is DateOnly d)
            return d;
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

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record BookingRecord(
        string AppointmentId,
        string PatientRid,
        string PatientCmId,
        string LastName,
        string FirstName,
        DateOnly? DateOfBirth,
        string? CardiologistId,
        string? CardiologistName,
        DateOnly AppointmentDate,
        string? AppointmentTime,
        bool Arrived,
        string? AppointmentNote,
        string? RoomName,
        string? SiteName,
        string ServiceTitle,
        string BookingKind);

    private sealed record TestRecord(
        string PatientRid,
        string PatientCmId,
        string LastName,
        string FirstName,
        DateOnly? DateOfBirth,
        DateOnly TestDate,
        bool HasReport);

    private sealed record PatientRecord(
        string PatientRid,
        string PatientCmId,
        string LastName,
        string FirstName,
        DateOnly? DateOfBirth,
        string? CardiologistId);

    private sealed record SyngoStudyRecord(
        string PatientId,
        string LastName,
        string FirstName,
        DateOnly? DateOfBirth,
        DateOnly StudyDate,
        string? StudyDescription);
}
