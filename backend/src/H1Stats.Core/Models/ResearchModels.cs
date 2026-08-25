namespace H1Stats.Core.Models;

/// <summary>
/// A clinical research study used for local LLM screening.
/// There is no hard cap — the first cohort is expected to be about 6–8 active studies and will grow.
/// </summary>
public record ResearchStudy(
    string Id,
    string Name,
    string Description,
    IReadOnlyList<string> InclusionCriteria,
    IReadOnlyList<string> ExclusionCriteria,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt
);

public record SaveResearchStudyRequest(
    string Name,
    string? Description,
    IReadOnlyList<string>? InclusionCriteria,
    IReadOnlyList<string>? ExclusionCriteria,
    bool IsActive = true
);

public static class ResearchSuitability
{
    public const string LikelySuitable = "LIKELY_SUITABLE";
    public const string PossiblySuitable = "POSSIBLY_SUITABLE";
    public const string NeedsReview = "NEEDS_REVIEW";
    public const string LikelyNotSuitable = "LIKELY_NOT_SUITABLE";
}

public static class CriterionStatus
{
    public const string Met = "MET";
    public const string NotMet = "NOT_MET";
    public const string Unclear = "UNCLEAR";
}

public readonly record struct ResearchLlmChatMessage(string Role, string Content);

public record ResearchLlmChatResult(bool Ok, string? Content, string? Error, string? ModelId);

/// <summary>Made-up notes used only to prove H1Stats → Qwen screening. Not a real patient.</summary>
public record SyntheticResearchPatient(string Id, string DisplayName, string Notes);

public record ResearchCriterionAssessment(string Criterion, string Status, string Reason);

public record ResearchStudyScreening(
    string StudyId,
    string StudyName,
    string Overall,
    string Summary,
    IReadOnlyList<ResearchCriterionAssessment> Inclusion,
    IReadOnlyList<ResearchCriterionAssessment> Exclusion,
    string? Error
);

public record SyntheticScreeningResult(
    SyntheticResearchPatient Patient,
    IReadOnlyList<ResearchStudyScreening> Studies,
    string? Error,
    string Source = "synthetic-notes"
);

public record DummyRtfPreview(string FileName, string ExtractedText, string? Error = null);

/// <summary>Row status on the Research Patient Finder worklist.</summary>
public static class ResearchWorklistStatus
{
    public const string NotAssessed = "NOT_ASSESSED";
    public const string Suitable = "SUITABLE";
    public const string NotSuitable = "NOT_SUITABLE";
    public const string Unclear = "UNCLEAR";
}

/// <summary>
/// Upcoming consult booking from CM2 diary (SCHEDULE_APPOINTMENT).
/// Same join pattern as GPH SMS module; one row per appointment RID.
/// </summary>
public record ResearchUpcomingConsult(
    string AppointmentId,
    string PatientCmId,
    string PatientName,
    DateOnly AppointmentDate,
    string? AppointmentTime,
    string ServiceTitle,
    string? SiteName,
    string? CardiologistId,
    string? CardiologistName,
    string? AppointmentNote,
    string Status,
    DateTime? ScreenedAt = null,
    IReadOnlyList<ResearchStudyScreening>? Studies = null
);

public record ResearchUpcomingConsultsResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    string? CardiologistId,
    int Count,
    IReadOnlyList<ResearchUpcomingConsult> Consults
);

/// <summary>Persisted LLM screening for one diary appointment against active studies.</summary>
public record ResearchAppointmentScreeningResult(
    string AppointmentId,
    string PatientCmId,
    string PatientName,
    DateTime ScreenedAt,
    string Status,
    string Source,
    IReadOnlyList<ResearchStudyScreening> Studies
);

public record ScreenAppointmentRequest(string? Notes = null, bool UseSampleNotes = false);

/// <summary>Maps study-level suitability to a worklist badge.</summary>
public static class ResearchWorklistStatusMapper
{
    public static string FromStudies(IReadOnlyList<ResearchStudyScreening>? studies)
    {
        if (studies is null || studies.Count == 0)
            return ResearchWorklistStatus.NotAssessed;

        if (studies.Any(s => s.Overall == ResearchSuitability.LikelyNotSuitable))
            return ResearchWorklistStatus.NotSuitable;

        if (studies.Any(s =>
                s.Overall == ResearchSuitability.NeedsReview
                || !string.IsNullOrWhiteSpace(s.Error)))
            return ResearchWorklistStatus.Unclear;

        if (studies.Any(s =>
                s.Overall is ResearchSuitability.LikelySuitable
                    or ResearchSuitability.PossiblySuitable))
            return ResearchWorklistStatus.Suitable;

        return ResearchWorklistStatus.Unclear;
    }
}
