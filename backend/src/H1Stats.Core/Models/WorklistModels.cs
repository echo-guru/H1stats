namespace H1Stats.Core.Models;

/// <summary>
/// Unarrived pink (Cardiology) recall row from the CM2 diary.
/// Pink = SCHEDULE_OCC_SERVICE colour 255,180,255 → Cardiology Recall (RID 41).
/// Unarrived = APPOINTMENT_ARRIVED is null or 0.
/// </summary>
public record PinkRecallWorklistRow(
    string AppointmentId,
    string LastName,
    string FirstName,
    string PatientCmId,
    DateOnly? DateOfBirth,
    string? CardiologistId,
    string? CardiologistName,
    string? RoomName,
    string? SiteName,
    DateOnly AppointmentDate,
    string? AppointmentTime,
    string? AppointmentNote,
    string ServiceTypeId,
    string ServiceTitle
);

public record PinkRecallWorklistResult(
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? SiteFilter,
    string? CardiologistId,
    int Count,
    IReadOnlyList<PinkRecallWorklistRow> Rows
);

/// <summary>Free diary start times for an appointment's room/day/duration (15-min slots).</summary>
public record AvailableDiarySlotsResult(
    string AppointmentId,
    string Date,
    string? RoomId,
    string? RoomName,
    int DurationSlots,
    int SlotMinutes,
    string? CurrentTime,
    IReadOnlyList<string> Slots,
    string? Message
);
