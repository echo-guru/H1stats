namespace H1Stats.Core.Models;

/// <summary>Progress bottleneck for a past TEE/IOE diary booking.</summary>
public static class TeeIoeProgressStatus
{
    public const string Booked = "booked";
    public const string Arrived = "arrived";
    public const string NotInSyngo = "not-in-syngo";
    public const string AwaitingReport = "awaiting-report";
    /// <summary>Syngo TEE/IOE study with no matching CM2 diary booking or TEST.</summary>
    public const string NotInCm2 = "not-in-cm2";
}

/// <summary>
/// Past TEE/IOE diary booking that is not yet complete (Syngo study + CM2 TEST report).
/// One row per patient (most recent incomplete booking in range).
/// </summary>
public record TeeIoeProgressRow(
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
    string BookingKind,
    bool InSyngo,
    bool HasReport,
    DateOnly? SyngoStudyDate,
    string? SyngoStudyDescription,
    DateOnly? TestDate,
    string Status,
    int BookingCount
);

public record TeeIoeProgressResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    string? CardiologistId,
    int Count,
    int BookedCount,
    int ArrivedCount,
    int NotInSyngoCount,
    int AwaitingReportCount,
    int NotInCm2Count,
    IReadOnlyList<TeeIoeProgressRow> Rows
);
