using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// Screens built-in synthetic notes or a dummy RTF against every active study using the local LLM.
/// </summary>
public sealed class ResearchScreeningService : IResearchScreeningService
{
    private static readonly SyntheticResearchPatient SyntheticPatient = new(
        "SYN-001",
        "Synthetic patient A",
        """
        SYNTHETIC PATIENT — not a real person. Do not treat as a clinical record.

        Research id: SYN-001
        Age: 47 years
        Sex: Female
        Height: 166 cm
        Weight: 92 kg
        BMI: 33.4 kg/m2

        Diabetes: No history of type 1 or type 2 diabetes.
        HbA1c: 5.6% (measured within the last 3 months).

        Weight history: Multiple clinician-supervised dietary weight-loss attempts over 5 years with subsequent regain.
        Motivation: Patient states she is motivated to lose at least 30% of current body weight.

        Medications: No GLP-1, GIP, or amylin receptor agonist therapy in the past 3 years.

        Imaging: No known MRI contraindications.
        """);

    private readonly IResearchLlmClient _llm;
    private readonly IResearchStudyStore _studies;
    private readonly IResearchScreeningResultStore _results;
    private readonly ICm2Repository _cm2;
    private readonly string _contentRoot;

    public ResearchScreeningService(
        IResearchLlmClient llm,
        IResearchStudyStore studies,
        IResearchScreeningResultStore results,
        ICm2Repository cm2,
        string contentRoot)
    {
        _llm = llm;
        _studies = studies;
        _results = results;
        _cm2 = cm2;
        _contentRoot = contentRoot;
    }

    public SyntheticResearchPatient GetSyntheticPatient() => SyntheticPatient;

    public DummyRtfPreview GetDummyRtfPreview()
    {
        if (!DummyResearchRtf.TryLoad(_contentRoot, out var path, out var rtf, out var error))
            return new DummyRtfPreview(DummyResearchRtf.FileName, "", error);

        return new DummyRtfPreview(Path.GetFileName(path), RtfTextExtractor.Extract(rtf));
    }

    public Task<SyntheticScreeningResult> ScreenSyntheticPatientAsync(CancellationToken ct = default) =>
        ScreenPatientAsync(SyntheticPatient, "synthetic-notes", ct);

    public Task<SyntheticScreeningResult> ScreenDummyRtfAsync(CancellationToken ct = default)
    {
        var preview = GetDummyRtfPreview();
        if (!string.IsNullOrWhiteSpace(preview.Error) || string.IsNullOrWhiteSpace(preview.ExtractedText))
        {
            var missing = new SyntheticResearchPatient("SAMPLE-RTF", $"Dummy RTF ({preview.FileName})", "");
            return Task.FromResult(new SyntheticScreeningResult(
                missing, [], preview.Error ?? "Dummy RTF extracted no text.", "dummy-rtf"));
        }

        var patient = new SyntheticResearchPatient(
            "SAMPLE-RTF",
            $"Dummy RTF ({preview.FileName})",
            preview.ExtractedText);
        return ScreenPatientAsync(patient, "dummy-rtf", ct);
    }

    public async Task<ResearchAppointmentScreeningResult> ScreenAppointmentAsync(
        string appointmentId, ScreenAppointmentRequest? request = null, CancellationToken ct = default)
    {
        var consult = await _cm2.GetUpcomingConsultByIdAsync(appointmentId, ct)
            ?? throw new InvalidOperationException("Appointment not found in CM2.");

        var notes = request?.Notes?.Trim() ?? "";
        var source = "pasted-notes";
        if (string.IsNullOrWhiteSpace(notes) && request?.UseSampleNotes == true)
        {
            var preview = GetDummyRtfPreview();
            if (!string.IsNullOrWhiteSpace(preview.Error) || string.IsNullOrWhiteSpace(preview.ExtractedText))
                throw new InvalidOperationException(preview.Error ?? "Sample RTF notes are not available.");
            notes = preview.ExtractedText;
            source = "sample-rtf-standin";
        }

        if (string.IsNullOrWhiteSpace(notes))
        {
            throw new InvalidOperationException(
                "No clinical notes available for this appointment yet. Paste notes or use sample notes until CM2 RTF screening is wired.");
        }

        var patient = new SyntheticResearchPatient(
            consult.PatientCmId,
            consult.PatientName,
            notes);
        var screened = await ScreenPatientAsync(patient, source, ct);
        if (screened.Error is not null && screened.Studies.Count == 0)
            throw new InvalidOperationException(screened.Error);

        var saved = new ResearchAppointmentScreeningResult(
            consult.AppointmentId,
            consult.PatientCmId,
            consult.PatientName,
            DateTime.UtcNow,
            ResearchWorklistStatusMapper.FromStudies(screened.Studies),
            source,
            screened.Studies);
        await _results.UpsertAsync(saved, ct);
        return saved;
    }

    private async Task<SyntheticScreeningResult> ScreenPatientAsync(
        SyntheticResearchPatient patient,
        string source,
        CancellationToken ct)
    {
        var status = await _llm.GetStatusAsync(ct);
        if (!status.Online)
        {
            return new SyntheticScreeningResult(
                patient,
                [],
                string.IsNullOrWhiteSpace(status.Message) ? "Local LLM is offline." : status.Message,
                source);
        }

        var studies = (await _studies.ListAsync(ct)).Where(s => s.IsActive).ToList();
        if (studies.Count == 0)
            return new SyntheticScreeningResult(patient, [], "No active studies to screen against.", source);

        var results = new List<ResearchStudyScreening>();
        foreach (var study in studies)
        {
            ct.ThrowIfCancellationRequested();
            results.Add(await ScreenStudyAsync(patient, study, status.ModelId, ct));
        }

        return new SyntheticScreeningResult(patient, results, null, source);
    }

    private async Task<ResearchStudyScreening> ScreenStudyAsync(
        SyntheticResearchPatient patient,
        ResearchStudy study,
        string? modelId,
        CancellationToken ct)
    {
        var messages = new ResearchLlmChatMessage[]
        {
            new("system", SystemPrompt),
            new("user", BuildUserPrompt(patient, study)),
        };

        var completion = await _llm.CompleteChatAsync(messages, modelId, ct);
        if (!completion.Ok || string.IsNullOrWhiteSpace(completion.Content))
        {
            return Failed(study, completion.Error ?? "LLM did not return a screening result.");
        }

        if (!TryParse(completion.Content, study, out var parsed))
            return Failed(study, "LLM returned a result that could not be parsed as JSON.");

        return SanitizeScreening(parsed);
    }

    private static ResearchStudyScreening Failed(ResearchStudy study, string error) =>
        new(study.Id, study.Name, ResearchSuitability.NeedsReview, error, [], [], error);

    private const string SystemPrompt =
        """
        You are a clinical research screening assistant for Hearts 1st.
        Assess only the provided notes against the listed study criteria.
        The notes are synthetic and not from a real patient. Do not invent facts.
        If a criterion is not addressed in the notes, mark it UNCLEAR.

        Inclusion criteria polarity:
        - MET = the patient satisfies the inclusion requirement.
        - NOT_MET = the notes show the patient fails the inclusion requirement.
        Example: inclusion "Age 18-69 years", notes say age 68 → status MET, reason "Age is 68 years."

        Exclusion criteria polarity (easy to get wrong — read carefully):
        - MET = the exclusion IS present (patient should be excluded).
        - NOT_MET = the exclusion does NOT apply (patient is fine on this point).
        Example: exclusion "HbA1c greater or equal to 6.5%", notes say HbA1c 6.0% → status NOT_MET, reason "HbA1c is 6.0%, below 6.5%."
        Example: exclusion "Type 1 or Type 2 diabetes", notes say no diabetes → status NOT_MET.

        The status field MUST agree with the reason field. Never mark MET if the reason says the value is below a threshold, absent, or not met.
        Return JSON only, with no markdown and no extra commentary.
        """;

    private static string BuildUserPrompt(SyntheticResearchPatient patient, ResearchStudy study)
    {
        var text = new StringBuilder();
        text.AppendLine($"Study name: {study.Name}");
        if (!string.IsNullOrWhiteSpace(study.Description))
        {
            text.AppendLine("Study description:");
            text.AppendLine(study.Description);
        }

        text.AppendLine("Inclusion criteria:");
        AppendCriteria(text, study.InclusionCriteria);
        text.AppendLine("Exclusion criteria:");
        AppendCriteria(text, study.ExclusionCriteria);
        text.AppendLine("Patient notes:");
        text.AppendLine(patient.Notes);
        text.AppendLine(
            """
            Return JSON with this shape:
            {
              "overall": "LIKELY_SUITABLE | POSSIBLY_SUITABLE | NEEDS_REVIEW | LIKELY_NOT_SUITABLE",
              "summary": "one or two sentences",
              "inclusion": [{"criterion": "exact inclusion text", "status": "MET|NOT_MET|UNCLEAR", "reason": "short evidence from the notes"}],
              "exclusion": [{"criterion": "exact exclusion text", "status": "MET|NOT_MET|UNCLEAR", "reason": "short evidence from the notes"}]
            }
            Status and reason must agree.
            For exclusions: a value below the stated threshold is NOT_MET (exclusion does not apply).
            Use LIKELY_SUITABLE only if every inclusion is MET and every exclusion is NOT_MET.
            Use LIKELY_NOT_SUITABLE if any inclusion is NOT_MET or any exclusion is MET.
            Use NEEDS_REVIEW if any criterion is UNCLEAR and there is no hard fail.
            """);
        return text.ToString();
    }

    private static void AppendCriteria(StringBuilder text, IReadOnlyList<string> items)
    {
        if (items.Count == 0)
        {
            text.AppendLine("- (none)");
            return;
        }

        foreach (var item in items)
            text.AppendLine($"- {item}");
    }

    private static bool TryParse(string raw, ResearchStudy study, out ResearchStudyScreening result)
    {
        result = Failed(study, "Unparsed");
        var json = ExtractJson(raw);
        if (json is null)
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var overall = NormalizeOverall(ReadString(root, "overall"));
            var summary = ReadString(root, "summary") ?? "No summary returned.";
            var inclusion = ReadAssessments(root, "inclusion", study.InclusionCriteria);
            var exclusion = ReadAssessments(root, "exclusion", study.ExclusionCriteria);
            result = new ResearchStudyScreening(study.Id, study.Name, overall, summary, inclusion, exclusion, null);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>
    /// Fixes status/reason contradictions from the LLM and recomputes overall from the cleaned rows.
    /// </summary>
    private static ResearchStudyScreening SanitizeScreening(ResearchStudyScreening screening)
    {
        var inclusion = screening.Inclusion.Select(SanitizeAssessment).ToList();
        var exclusion = screening.Exclusion.Select(SanitizeAssessment).ToList();
        var corrected = inclusion.Concat(exclusion).Count(a =>
            a.Reason.Contains("[corrected from ", StringComparison.OrdinalIgnoreCase));

        var overall = ComputeOverall(inclusion, exclusion);
        var summary = screening.Summary;
        if (corrected > 0)
        {
            summary = string.IsNullOrWhiteSpace(summary)
                ? $"Adjusted {corrected} criterion status(es) to match the stated reason."
                : $"{summary.Trim()} Adjusted {corrected} criterion status(es) to match the stated reason.";
        }

        return screening with
        {
            Overall = overall,
            Summary = summary,
            Inclusion = inclusion,
            Exclusion = exclusion,
        };
    }

    private static ResearchCriterionAssessment SanitizeAssessment(ResearchCriterionAssessment assessment)
    {
        var status = assessment.Status;
        if (status == CriterionStatus.Unclear)
            return assessment;

        var reason = assessment.Reason ?? "";
        var implied = InferStatusFromReason(reason);
        if (implied is null || implied == status)
            return assessment;

        // Prefer the status implied by the reason text when it clearly conflicts.
        return assessment with
        {
            Status = implied,
            Reason = $"{reason.Trim()} [corrected from {status}]",
        };
    }

    /// <summary>
    /// Infers MET / NOT_MET from reason wording when polarity is clear; otherwise null.
    /// Same implication for inclusion and exclusion: MET = condition/threshold present, NOT_MET = not present.
    /// </summary>
    private static string? InferStatusFromReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return null;

        var text = reason.Trim();

        // Strong "does not apply / below threshold / absent" signals → NOT_MET
        if (Regex.IsMatch(text,
                @"\b(below|under|less than|does not meet|do not meet|not meet|not met|no (type|history|evidence|mention|known)|not (diagnosed|stated|present|triggered|explicit)|absent|does not apply|no diabetes)\b",
                RegexOptions.IgnoreCase))
            return CriterionStatus.NotMet;

        // "Within range" for age-style criteria → MET
        if (Regex.IsMatch(text, @"\bwithin (the )?(age )?range\b", RegexOptions.IgnoreCase))
            return CriterionStatus.Met;

        // Strong affirmative signals → MET
        if (Regex.IsMatch(text,
                @"\b(meets|satisfies|is present|has (type\s*[12]|diabetes)|above|greater than or equal|≥|>=)\b",
                RegexOptions.IgnoreCase))
            return CriterionStatus.Met;

        return null;
    }

    private static string ComputeOverall(
        IReadOnlyList<ResearchCriterionAssessment> inclusion,
        IReadOnlyList<ResearchCriterionAssessment> exclusion)
    {
        if (inclusion.Any(a => a.Status == CriterionStatus.NotMet) ||
            exclusion.Any(a => a.Status == CriterionStatus.Met))
            return ResearchSuitability.LikelyNotSuitable;

        if (inclusion.Any(a => a.Status == CriterionStatus.Unclear) ||
            exclusion.Any(a => a.Status == CriterionStatus.Unclear))
            return ResearchSuitability.NeedsReview;

        if (inclusion.Count > 0 &&
            inclusion.All(a => a.Status == CriterionStatus.Met) &&
            exclusion.All(a => a.Status == CriterionStatus.NotMet))
            return ResearchSuitability.LikelySuitable;

        return ResearchSuitability.PossiblySuitable;
    }

    private static IReadOnlyList<ResearchCriterionAssessment> ReadAssessments(
        JsonElement root, string property, IReadOnlyList<string> fallback)
    {
        var items = new List<ResearchCriterionAssessment>();
        if (!root.TryGetProperty(property, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return fallback.Select(criterion =>
                new ResearchCriterionAssessment(criterion, CriterionStatus.Unclear, "Not assessed.")).ToList();
        }

        foreach (var item in array.EnumerateArray())
        {
            var criterion = ReadString(item, "criterion") ?? "";
            if (string.IsNullOrWhiteSpace(criterion))
                continue;
            items.Add(new ResearchCriterionAssessment(
                criterion,
                NormalizeCriterionStatus(ReadString(item, "status")),
                ReadString(item, "reason") ?? ""));
        }

        return items;
    }

    private static string? ExtractJson(string raw)
    {
        var text = Regex.Replace(raw, @"<think>[\s\S]*?</think>", "", RegexOptions.IgnoreCase).Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline >= 0)
                text = text[(firstNewline + 1)..];
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
                text = text[..fence];
            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return null;
        return text[start..(end + 1)];
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string NormalizeOverall(string? value)
    {
        var key = (value ?? "").Trim().ToUpperInvariant().Replace(' ', '_');
        return key switch
        {
            ResearchSuitability.LikelySuitable => ResearchSuitability.LikelySuitable,
            ResearchSuitability.PossiblySuitable => ResearchSuitability.PossiblySuitable,
            ResearchSuitability.LikelyNotSuitable => ResearchSuitability.LikelyNotSuitable,
            _ => ResearchSuitability.NeedsReview,
        };
    }

    private static string NormalizeCriterionStatus(string? value)
    {
        var key = (value ?? "").Trim().ToUpperInvariant().Replace(' ', '_');
        return key switch
        {
            CriterionStatus.Met => CriterionStatus.Met,
            CriterionStatus.NotMet => CriterionStatus.NotMet,
            "PRESENT" => CriterionStatus.Met,
            "ABSENT" => CriterionStatus.NotMet,
            "TRIGGERED" => CriterionStatus.Met,
            "NOT_TRIGGERED" => CriterionStatus.NotMet,
            _ => CriterionStatus.Unclear,
        };
    }
}
