namespace H1Stats.Core.Models;

/// <summary>
/// One patient with unassigned cardiologist and at least one Initial/Follow-up consult
/// booking in the lookback window (deduped — most recent booking shown).
/// </summary>
public record UnassignedConsultPatientRow(
    string PatientRid,
    string PatientCmId,
    string LastName,
    string FirstName,
    DateOnly? DateOfBirth,
    string LatestAppointmentId,
    DateOnly LatestAppointmentDate,
    string? LatestAppointmentTime,
    string? LatestServiceTitle,
    bool LatestArrived,
    string? LatestAppointmentNote,
    string? RoomName,
    string? SiteName,
    int BookingCount
);

public record UnassignedConsultPatientResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    int Count,
    IReadOnlyList<UnassignedConsultPatientRow> Rows
);
