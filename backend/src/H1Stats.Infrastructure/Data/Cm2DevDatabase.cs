using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Oracle.ManagedDataAccess.Client;

namespace H1Stats.Infrastructure.Data;

/// <summary>SandBox-only CM2 DEV Oracle connection (SID CM2DEV).</summary>
public sealed class Cm2DevDatabase : ICm2DevDatabase
{
    private readonly IDatabaseConnectionService _db;

    public Cm2DevDatabase(IDatabaseConnectionService db) => _db = db;

    public string Schema => _db.GetLiveSettings().EffectiveCm2DevSchema;

    public string EndpointLabel
    {
        get
        {
            var s = _db.GetLiveSettings();
            return $"{s.EffectiveCm2DevSid} @ {s.EffectiveCm2DevServer}:{s.EffectiveCm2DevPort}";
        }
    }

    public string ConnectionString => _db.GetLiveSettings().Cm2DevOracleConnectionString;

    public async Task<DatabaseConnectionTestResult> TestConnectionAsync(CancellationToken ct = default)
    {
        try
        {
            await using var conn = new OracleConnection(ConnectionString);
            await conn.OpenAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT 1 FROM DUAL";
            await cmd.ExecuteScalarAsync(ct);
            return new DatabaseConnectionTestResult(EndpointLabel, true, "Connected", DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            return new DatabaseConnectionTestResult(EndpointLabel, false, ex.Message, DateTime.UtcNow);
        }
    }

    public Task<IReadOnlyList<SiteOption>> GetSitesAsync(CancellationToken ct = default) =>
        PinkRecallWorklistStore.GetSitesAsync(ConnectionString, Schema, ct);

    public Task<PinkRecallWorklistResult> GetUnarrivedPinkRecallsAsync(
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        string? site = null,
        string? cardiologistId = null,
        CancellationToken ct = default) =>
        PinkRecallWorklistStore.GetUnarrivedPinkRecallsAsync(
            ConnectionString, Schema, dateFrom, dateTo, site, cardiologistId, ct);

    public Task<UnactionedConsultReferralResult> GetUnactionedConsultReferralsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default) =>
        UnactionedConsultReferralStore.GetUnactionedConsultReferralsAsync(
            ConnectionString, Schema, dateFrom, dateTo, cardiologistId, ct);

    public Task<(StudyReportFile? File, string? Error)> GetUnactionedConsultReferralFileAsync(
        string documentId, CancellationToken ct = default) =>
        UnactionedConsultReferralStore.GetConsultReferralPdfAsync(
            ConnectionString, Schema, documentId, _db.GetLiveSettings().Cm2ReportRoot, ct);

    public Task<MissingCtcaDocResult> GetMissingCtcaDocsAsync(
        DateOnly dateFrom,
        DateOnly dateTo,
        string? cardiologistId = null,
        CancellationToken ct = default) =>
        MissingCtcaDocStore.GetMissingCtcaDocsAsync(
            ConnectionString, Schema, dateFrom, dateTo, cardiologistId, ct);

    public Task<(bool Ok, string? Error)> UpdateAppointmentNoteAsync(
        string appointmentId, string? note, CancellationToken ct = default) =>
        PinkRecallWorklistStore.UpdateAppointmentNoteAsync(
            ConnectionString, Schema, appointmentId, note, ct);

    public Task<(bool Ok, string? Error, string? ServiceTypeId, string? ServiceTitle)> UpdateAppointmentServiceTypeAsync(
        string appointmentId, string serviceTypeId, CancellationToken ct = default) =>
        PinkRecallWorklistStore.UpdateAppointmentServiceTypeAsync(
            ConnectionString, Schema, appointmentId, serviceTypeId, ct);

    public Task<(bool Ok, string? Error)> ArriveAppointmentAsync(
        string appointmentId, CancellationToken ct = default) =>
        PinkRecallWorklistStore.ArriveAppointmentAsync(ConnectionString, Schema, appointmentId, ct);

    public Task<(bool Ok, string? Error, DateOnly? Date, string? Time)> UpdateAppointmentScheduleAsync(
        string appointmentId, string date, string? time, CancellationToken ct = default) =>
        PinkRecallWorklistStore.UpdateAppointmentScheduleAsync(
            ConnectionString, Schema, appointmentId, date, time, ct);

    public Task<AvailableDiarySlotsResult> GetAvailableDiarySlotsAsync(
        string appointmentId, string date, CancellationToken ct = default) =>
        PinkRecallWorklistStore.GetAvailableDiarySlotsAsync(
            ConnectionString, Schema, appointmentId, date, ct);

    public Task<UnassignedConsultPatientResult> GetUnassignedConsultPatientsAsync(
        int lookbackMonths = 12, CancellationToken ct = default) =>
        UnassignedConsultStore.GetUnassignedConsultPatientsAsync(
            ConnectionString, Schema, lookbackMonths, ct);

    public Task<(bool Ok, string? Error, string? CardiologistId, string? CardiologistName)> AssignPatientCardiologistAsync(
        string patientRid, string cardiologistId, CancellationToken ct = default) =>
        UnassignedConsultStore.AssignPatientCardiologistAsync(
            ConnectionString, Schema, patientRid, cardiologistId, ct);
}
