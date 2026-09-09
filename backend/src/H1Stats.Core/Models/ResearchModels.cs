using System.Globalization;
using System.Text.RegularExpressions;

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
    /// <summary>Unable to determine from available records — do not invent a Met/NotMet answer.</summary>
    public const string Unclear = "UNCLEAR";
}

/// <summary>
/// Compares a stated age against an age-range criterion (e.g. "Age 18-69 years").
/// MET means the stated age falls in that range, for both inclusion and exclusion rows.
/// </summary>
public static class ResearchAgeCriterion
{
    public static ResearchCriterionAssessment Apply(ResearchCriterionAssessment assessment)
    {
        if (!TryParseRange(assessment.Criterion, out var min, out var max))
            return assessment;
        if (!TryParseStatedAge(assessment.Reason, out var age))
            return assessment;

        var inRange = age >= min && age <= max;
        var status = inRange ? CriterionStatus.Met : CriterionStatus.NotMet;
        if (assessment.Status == status)
            return assessment;

        var reason = string.IsNullOrWhiteSpace(assessment.Reason)
            ? $"Age {age} years."
            : assessment.Reason.Trim();
        if (!reason.Contains("[corrected from ", StringComparison.OrdinalIgnoreCase))
            reason = $"{reason} [corrected from {assessment.Status}]";
        return assessment with { Status = status, Reason = reason };
    }

    public static bool TryParseRange(string criterion, out int minInclusive, out int maxInclusive)
    {
        minInclusive = 0;
        maxInclusive = 0;
        if (string.IsNullOrWhiteSpace(criterion)
            || !Regex.IsMatch(criterion, @"\bage\b", RegexOptions.IgnoreCase))
            return false;

        var match = Regex.Match(criterion, @"(\d{1,3})\s*(?:-|–|—|to)\s*(\d{1,3})", RegexOptions.IgnoreCase);
        if (!match.Success)
            match = Regex.Match(criterion, @"between\s+(\d{1,3})\s+and\s+(\d{1,3})", RegexOptions.IgnoreCase);
        if (!match.Success)
            return false;
        if (!int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out minInclusive)
            || !int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out maxInclusive))
            return false;
        if (minInclusive > maxInclusive)
            (minInclusive, maxInclusive) = (maxInclusive, minInclusive);
        return minInclusive is >= 0 and <= 120 && maxInclusive is >= 0 and <= 120;
    }

    public static bool TryParseStatedAge(string? text, out int age)
    {
        age = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var match = Regex.Match(
            text,
            @"\b(?:age|aged)\s*[:\-]?\s*(\d{1,3})\s*(?:years?)?\b",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            match = Regex.Match(
                text,
                @"\b(\d{1,3})\s*(?:-?\s*year(?:s)?[-\s]?old)\b",
                RegexOptions.IgnoreCase);
        }

        return match.Success
            && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out age)
            && age is >= 0 and <= 120;
    }
}

/// <summary>
/// Overall study suitability from criterion rows.
/// Age, BMI, diabetes and HbA1c are the deciding criteria: if those are all
/// determined and favourable, remaining UNCLEAR rows still count as likely suitable.
/// </summary>
public static class ResearchOverall
{
    private static readonly Regex AgeWord = new(@"\bage\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex A1cWord = new(@"\ba1c\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string FromCriteria(
        IReadOnlyList<ResearchCriterionAssessment> inclusion,
        IReadOnlyList<ResearchCriterionAssessment> exclusion)
    {
        if (inclusion.Any(a => a.Status == CriterionStatus.NotMet) ||
            exclusion.Any(a => a.Status == CriterionStatus.Met))
            return ResearchSuitability.LikelyNotSuitable;

        if (DecidingCriteriaAreFavourable(inclusion, exclusion))
            return ResearchSuitability.LikelySuitable;

        if (inclusion.Any(a => a.Status == CriterionStatus.Unclear) ||
            exclusion.Any(a => a.Status == CriterionStatus.Unclear))
            return ResearchSuitability.NeedsReview;

        if (inclusion.Count > 0 &&
            inclusion.All(a => a.Status == CriterionStatus.Met) &&
            exclusion.All(a => a.Status == CriterionStatus.NotMet))
            return ResearchSuitability.LikelySuitable;

        return ResearchSuitability.PossiblySuitable;
    }

    public static string BuildSummary(
        string overall,
        IReadOnlyList<ResearchCriterionAssessment> inclusion,
        IReadOnlyList<ResearchCriterionAssessment> exclusion,
        int correctedStatuses = 0,
        int derivedFacts = 0)
    {
        var core = overall switch
        {
            ResearchSuitability.LikelyNotSuitable => HardFailSummary(inclusion, exclusion),
            ResearchSuitability.LikelySuitable => LikelySuitableSummary(inclusion, exclusion),
            ResearchSuitability.NeedsReview =>
                "Needs review: one or more criteria could not be determined from available records.",
            ResearchSuitability.PossiblySuitable =>
                "Possibly suitable; clinician review recommended.",
            _ => "Screening complete.",
        };

        var notes = new List<string>();
        if (correctedStatuses > 0)
            notes.Add($"Adjusted {correctedStatuses} criterion status(es) to match the stated reason.");
        if (derivedFacts > 0)
            notes.Add($"Applied {derivedFacts} calculated BMI / documented therapy finding(s) from the source documents.");

        return notes.Count == 0 ? core : $"{core} {string.Join(" ", notes)}";
    }

    private static string HardFailSummary(
        IReadOnlyList<ResearchCriterionAssessment> inclusion,
        IReadOnlyList<ResearchCriterionAssessment> exclusion)
    {
        var failedInclusion = inclusion.FirstOrDefault(a => a.Status == CriterionStatus.NotMet);
        if (failedInclusion is not null)
        {
            var detail = string.IsNullOrWhiteSpace(failedInclusion.Reason)
                ? failedInclusion.Criterion
                : failedInclusion.Reason.Trim();
            return $"Likely not suitable: inclusion not met — {detail}";
        }

        var hitExclusion = exclusion.FirstOrDefault(a => a.Status == CriterionStatus.Met);
        if (hitExclusion is not null)
        {
            var detail = string.IsNullOrWhiteSpace(hitExclusion.Reason)
                ? hitExclusion.Criterion
                : hitExclusion.Reason.Trim();
            return $"Likely not suitable: exclusion present — {detail}";
        }

        return "Likely not suitable based on the assessed criteria.";
    }

    private static string LikelySuitableSummary(
        IReadOnlyList<ResearchCriterionAssessment> inclusion,
        IReadOnlyList<ResearchCriterionAssessment> exclusion)
    {
        var unclear = inclusion.Concat(exclusion).Any(a => a.Status == CriterionStatus.Unclear);
        return unclear
            ? "Likely suitable: Age, BMI, diabetes and HbA1c are favourable; other criteria remain unclear."
            : "Likely suitable: inclusion criteria are met and exclusion criteria do not apply.";
    }

    public static ResearchAppointmentScreeningResult PromoteSaved(ResearchAppointmentScreeningResult result)
    {
        if (result.Studies.Count == 0)
            return result;

        var changed = false;
        var studies = new List<ResearchStudyScreening>(result.Studies.Count);
        foreach (var study in result.Studies)
        {
            var inclusion = study.Inclusion.Select(ResearchAgeCriterion.Apply).ToList();
            var exclusion = study.Exclusion.Select(ResearchAgeCriterion.Apply).ToList();
            var overall = FromCriteria(inclusion, exclusion);
            var summary = BuildSummary(overall, inclusion, exclusion);
            if (!inclusion.SequenceEqual(study.Inclusion)
                || !exclusion.SequenceEqual(study.Exclusion)
                || overall != study.Overall
                || !string.Equals(summary, study.Summary, StringComparison.Ordinal))
            {
                changed = true;
                studies.Add(study with
                {
                    Inclusion = inclusion,
                    Exclusion = exclusion,
                    Overall = overall,
                    Summary = summary,
                });
            }
            else
            {
                studies.Add(study);
            }
        }

        if (!changed)
            return result;

        return result with
        {
            Studies = studies,
            Status = ResearchWorklistStatusMapper.FromStudies(studies),
        };
    }

    private static bool DecidingCriteriaAreFavourable(
        IReadOnlyList<ResearchCriterionAssessment> inclusion,
        IReadOnlyList<ResearchCriterionAssessment> exclusion)
    {
        var kinds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in inclusion)
        {
            var kind = DecidingKind(item.Criterion);
            if (kind is null)
                continue;
            if (item.Status != CriterionStatus.Met)
                return false;
            kinds.Add(kind);
        }

        foreach (var item in exclusion)
        {
            var kind = DecidingKind(item.Criterion);
            if (kind is null)
                continue;
            if (item.Status != CriterionStatus.NotMet)
                return false;
            kinds.Add(kind);
        }

        return kinds.Contains("age")
            && kinds.Contains("bmi")
            && kinds.Contains("diabetes")
            && kinds.Contains("hba1c");
    }

    private static string? DecidingKind(string criterion)
    {
        if (string.IsNullOrWhiteSpace(criterion))
            return null;

        var text = criterion.ToLowerInvariant();
        if (text.Contains("bmi"))
            return "bmi";
        if (text.Contains("hba1c") || text.Contains("hb a1c") || A1cWord.IsMatch(text))
            return "hba1c";
        if (text.Contains("diabetes"))
            return "diabetes";
        if (AgeWord.IsMatch(text))
            return "age";
        return null;
    }
}

/// <summary>MVP default vs future Expanded Analysis of broader CM2 material.</summary>
public static class ResearchAnalysisMode
{
    /// <summary>Most recent previous consultation letter; optional TTE/stress only when needed.</summary>
    public const string LatestConsultLetter = "LATEST_CONSULT_LETTER";

    /// <summary>Reserved for later: older letters, DOCUMENTS, other reports.</summary>
    public const string Expanded = "EXPANDED";
}

/// <summary>Kinds of CM2 source files that may be reviewed during screening.</summary>
public static class ResearchDocumentKind
{
    public const string ConsultLetter = "CONSULT_LETTER";
    public const string TteReport = "TTE_REPORT";
    public const string StressEchoReport = "STRESS_ECHO_REPORT";
}

public readonly record struct ResearchLlmChatMessage(string Role, string Content);

public record ResearchLlmChatResult(bool Ok, string? Content, string? Error, string? ModelId);

/// <summary>Notes payload used when calling the local LLM. Not a persisted patient record.</summary>
public record SyntheticResearchPatient(string Id, string DisplayName, string Notes);

public record ResearchCriterionAssessment(
    string Criterion,
    string Status,
    string Reason,
    string? EvidenceSourceKind = null,
    string? EvidenceSourceRid = null,
    string? EvidenceFileName = null
);

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
    string Source = "cm2-consult-letter"
);

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
    string? PatientRid = null,
    DateTime? ScreenedAt = null,
    IReadOnlyList<ResearchStudyScreening>? Studies = null,
    IReadOnlyList<ResearchSourceDocument>? DocumentsReviewed = null,
    string? PrimaryLettersRid = null,
    string? PrimaryLetterPath = null,
    DateOnly? PrimaryLetterDate = null,
    string? SourceFileFingerprint = null,
    int? AgeYears = null
);

public record ResearchUpcomingConsultsResult(
    DateOnly DateFrom,
    DateOnly DateTo,
    string? CardiologistId,
    int Count,
    IReadOnlyList<ResearchUpcomingConsult> Consults
);

/// <summary>
/// A CM2 file reviewed (or attempted) during screening. Original files are always read-only.
/// </summary>
public record ResearchSourceDocument(
    string Kind,
    string? PatientRid,
    string? SourceRid,
    DateOnly? SourceDate,
    string? RelativePath,
    string? FileName,
    string? Title,
    bool Reviewed,
    string? ExtractError = null,
    string? ConsultRid = null,
    int? LetterType = null,
    int? TestRequired = null
);

/// <summary>CM2 CONSULT + LETTERS metadata for the most recent previous consultation letter.</summary>
public record ResearchCm2LetterRef(
    string PatientRid,
    string ConsultRid,
    string LettersRid,
    DateOnly? LetterDate,
    int? LetterType,
    int? ConsultType,
    string? LetterTitle,
    string RelativePath
);

/// <summary>CM2 TEST + REPORT_PATH metadata for an investigation report (TTE / stress echo).</summary>
public record ResearchCm2StudyReportRef(
    string PatientRid,
    string TestRid,
    DateOnly? TestDate,
    int TestRequired,
    string InvestigationName,
    string RelativePath
);

/// <summary>In-memory extract from a CM2 RTF/report. Never writes back to the source path.</summary>
public record ResearchDocumentExtract(
    ResearchSourceDocument Document,
    string? ExtractedText
);

/// <summary>Persisted LLM screening for one diary appointment against active studies.</summary>
public record ResearchAppointmentScreeningResult(
    string AppointmentId,
    string PatientCmId,
    string PatientName,
    DateTime ScreenedAt,
    string Status,
    string Source,
    IReadOnlyList<ResearchStudyScreening> Studies,
    string? PatientRid = null,
    string AnalysisMode = ResearchAnalysisMode.LatestConsultLetter,
    IReadOnlyList<ResearchSourceDocument>? DocumentsReviewed = null,
    string? PrimaryConsultRid = null,
    string? PrimaryLettersRid = null,
    DateOnly? PrimaryLetterDate = null,
    string? PrimaryLetterPath = null,
    string? PrimaryLetterFileName = null,
    /// <summary>Stable id for the primary letter file already reviewed (lettersRid|relativePath).</summary>
    string? SourceFileFingerprint = null,
    bool AlreadyReviewedSameSource = false
);

public record ScreenAppointmentRequest(
    bool ForceRescreen = false,
    /// <summary>Defaults to LATEST_CONSULT_LETTER. EXPANDED is reserved for later.</summary>
    string? AnalysisMode = null,
    /// <summary>
    /// When false (default, including batch), stop after any inclusion criterion is NOT_MET.
    /// When true (Re-screen), assess every inclusion and exclusion criterion.
    /// </summary>
    bool FullScreen = false
);

public record OverrideResearchSuitabilityRequest(
    string StudyId,
    string Overall
);

public record PostResearchSlackReportRequest(
    string Text
);

public static class ResearchScreeningJobStatus
{
    public const string Idle = "IDLE";
    public const string Running = "RUNNING";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
    public const string Failed = "FAILED";
}

public static class ResearchScreeningJobItemStatus
{
    public const string Pending = "PENDING";
    public const string Running = "RUNNING";
    public const string Done = "DONE";
    public const string Skipped = "SKIPPED";
    public const string Failed = "FAILED";
}

public record StartResearchScreeningJobRequest(
    IReadOnlyList<string>? AppointmentIds,
    bool ForceRescreen = false
);

/// <summary>In-memory background batch that screens upcoming consults one at a time.</summary>
public record ResearchScreeningJob(
    string Id,
    string Status,
    int Total,
    int Completed,
    int Skipped,
    int Failed,
    bool ForceRescreen,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    string? CurrentAppointmentId,
    string? Error,
    IReadOnlyList<ResearchScreeningJobItem> Items
)
{
    public static ResearchScreeningJob Idle() =>
        new("", ResearchScreeningJobStatus.Idle, 0, 0, 0, 0, false, null, null, null, null, []);

    public bool IsActive => Status == ResearchScreeningJobStatus.Running;
}

public record ResearchScreeningJobItem(
    string AppointmentId,
    string Status,
    string? Error = null,
    string? WorklistStatus = null,
    DateTime? ScreenedAt = null,
    bool AlreadyReviewedSameSource = false
);

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

public static class ResearchSourceFileFingerprint
{
    public static string? ForLetter(string? lettersRid, string? relativePath) =>
        ForSource(lettersRid, relativePath);

    public static string? ForSource(string? sourceRid, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(sourceRid) || string.IsNullOrWhiteSpace(relativePath))
            return null;
        return $"{sourceRid.Trim()}|{NormalizePath(relativePath)}";
    }

    public static string NormalizePath(string path) =>
        path.Replace('/', '\\').Trim().TrimStart('\\');
}
