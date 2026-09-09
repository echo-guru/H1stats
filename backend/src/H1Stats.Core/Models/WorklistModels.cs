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

/// <summary>
/// Imported consult-referral document (DOCUMENTS, category 4, title Cons Ref)
/// with no unarrived Cardiology/Admin recall for that patient.
/// </summary>
public record UnactionedConsultReferralRow(
    string DocumentId,
    string PatientRid,
    string PatientCmId,
    string LastName,
    string FirstName,
    DateOnly? DateOfBirth,
    string? CardiologistId,
    string? CardiologistName,
    DateOnly DocumentDate,
    string? DocumentTitle,
    string? DocumentPath,
    int DocumentCategory,
    DateOnly? UpcomingConsultDate,
    string? UpcomingConsultTitle,
    string? UpcomingConsultNote
);

public record UnactionedConsultReferralResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    string? CardiologistId,
    int Count,
    IReadOnlyList<UnactionedConsultReferralRow> Rows
);

/// <summary>
/// Past CT Coronary Angiogram diary booking with no imported test/results (DOC_CATEGORY 1) CTCA document.
/// One row per patient (most recent unmatched booking).
/// </summary>
public record MissingCtcaDocRow(
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
    int BookingCount
);

public record MissingCtcaDocResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    string? CardiologistId,
    int Count,
    IReadOnlyList<MissingCtcaDocRow> Rows
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
