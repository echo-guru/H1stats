using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.Extensions.Options;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// Screens a CM2 consult appointment against every active study using the local LLM.
/// </summary>
public sealed class ResearchScreeningService : IResearchScreeningService
{
    private readonly IResearchLlmClient _llm;
    private readonly IResearchStudyStore _studies;
    private readonly IResearchScreeningResultStore _results;
    private readonly ICm2Repository _cm2;
    private readonly ResearchLlmSettings _llmSettings;

    public ResearchScreeningService(
        IResearchLlmClient llm,
        IResearchStudyStore studies,
        IResearchScreeningResultStore results,
        ICm2Repository cm2,
        IOptions<ResearchLlmSettings> llmOptions)
    {
        _llm = llm;
        _studies = studies;
        _results = results;
        _cm2 = cm2;
        _llmSettings = llmOptions.Value;
    }

    public async Task<ResearchAppointmentScreeningResult> ScreenAppointmentAsync(
        string appointmentId, ScreenAppointmentRequest? request = null, CancellationToken ct = default)
    {
        var consult = await _cm2.GetUpcomingConsultByIdAsync(appointmentId, ct)
            ?? throw new InvalidOperationException("Appointment not found in CM2.");

        var analysisMode = string.IsNullOrWhiteSpace(request?.AnalysisMode)
            ? ResearchAnalysisMode.LatestConsultLetter
            : request!.AnalysisMode!.Trim().ToUpperInvariant();

        if (analysisMode == ResearchAnalysisMode.Expanded)
        {
            throw new InvalidOperationException(
                "Expanded Analysis is not implemented yet. Use the latest consultation letter workflow.");
        }

        if (string.IsNullOrWhiteSpace(consult.PatientRid))
            throw new InvalidOperationException("Appointment is missing PATIENT_RID; cannot look up CM2 documents.");

        var letterRef = await _cm2.GetLatestConsultLetterAsync(
            consult.PatientRid, beforeDate: consult.AppointmentDate, ct);

        // Fallback: absolute latest consult letter if none strictly before the booking.
        letterRef ??= await _cm2.GetLatestConsultLetterAsync(consult.PatientRid, beforeDate: null, ct);

        var documents = new List<ResearchSourceDocument>();
        var parts = new List<(ResearchSourceDocument Doc, string Text)>();

        if (letterRef is not null)
        {
            var letterExtract = ResearchCm2DocumentReader.ReadLetter(_cm2, letterRef);
            documents.Add(letterExtract.Document);
            if (letterExtract.Document.Reviewed && !string.IsNullOrWhiteSpace(letterExtract.ExtractedText))
                parts.Add((letterExtract.Document, letterExtract.ExtractedText!));
        }

        // No usable consult letter — assess from most recent stress echo, then TTE.
        if (parts.Count == 0)
        {
            await TryAddSupplementalEchoReportsAsync(
                consult.PatientRid, documents, parts, ct, includeStress: true, preferStressFirst: true);
        }

        if (parts.Count == 0 && consult.AgeYears is null)
        {
            throw new InvalidOperationException(
                "No previous consultation letter, stress echo, or transthoracic echo report found for this patient in CM2.");
        }

        var primary = parts.Count > 0 ? parts[0].Doc : null;
        var fingerprint = primary is not null
            ? ResearchSourceFileFingerprint.ForSource(primary.SourceRid, primary.RelativePath)
            : $"age-only|{consult.PatientRid}|{consult.AppointmentId}";
        if (request?.ForceRescreen != true && !string.IsNullOrWhiteSpace(fingerprint))
        {
            var existing = await _results.GetAsync(consult.AppointmentId, ct);
            if (existing is not null
                && string.Equals(existing.SourceFileFingerprint, fingerprint, StringComparison.OrdinalIgnoreCase)
                && existing.Studies.Count > 0)
            {
                return existing with { AlreadyReviewedSameSource = true };
            }
        }

        var fullScreen = request?.FullScreen == true;
        var hasLetter = parts.Any(p => p.Doc.Kind == ResearchDocumentKind.ConsultLetter);

        var facts = WithConsultAge(ResearchClinicalFactsExtractor.Extract(parts), consult);
        var activeStudies = (await _studies.ListAsync(ct)).Where(s => s.IsActive).ToList();
        // Echo reports usually carry height/weight. Skip that lookup when letter facts
        // already fail an inclusion criterion (unless this is a full re-screen).
        if (hasLetter && !facts.HasBmi && NeedsMoreDocumentsForInclusion(activeStudies, facts, fullScreen))
        {
            await TryAddSupplementalEchoReportsAsync(
                consult.PatientRid, documents, parts, ct, includeStress: false);
            facts = WithConsultAge(ResearchClinicalFactsExtractor.Extract(parts), consult);
        }

        var notes = parts.Count == 0
            ? BuildAgeOnlyNotes(facts)
            : ResearchCm2DocumentReader.BuildLabeledNotes(parts, facts);
        var source = parts.Count == 0 ? "cm2-age-only" : DescribeSource(parts);
        SyntheticScreeningResult screened;
        if (parts.Count == 0)
        {
            // No letter/echo text — score what CM2 demographics allow; leave the rest UNCLEAR.
            var studyResults = activeStudies.Select(study =>
            {
                var seeded = ApplyDerivedFacts(Blank(study), facts);
                return seeded with
                {
                    Summary =
                        "No previous consultation letter, stress echo, or transthoracic echo report was available. Assessed from CM2 demographics only.",
                    Error = null,
                };
            }).ToList();
            screened = new SyntheticScreeningResult(
                new SyntheticResearchPatient(consult.PatientCmId, consult.PatientName, notes),
                studyResults,
                null,
                source);
        }
        else
        {
            var patient = new SyntheticResearchPatient(consult.PatientCmId, consult.PatientName, notes);
            screened = await ScreenPatientAsync(patient, source, ct, facts, fullScreen, activeStudies);
            if (screened.Error is not null && screened.Studies.Count == 0)
                throw new InvalidOperationException(screened.Error);

            // Remaining reports when a study still has UNCLEAR rows and no inclusion hard-fail.
            if (HasUndeterminedCriteria(screened.Studies))
            {
                var before = parts.Count;
                await TryAddSupplementalEchoReportsAsync(
                    consult.PatientRid, documents, parts, ct, includeStress: true, preferStressFirst: true);

                if (parts.Count > before)
                {
                    facts = WithConsultAge(ResearchClinicalFactsExtractor.Extract(parts), consult);
                    notes = ResearchCm2DocumentReader.BuildLabeledNotes(parts, facts);
                    patient = patient with { Notes = notes };
                    source = DescribeSource(parts);
                    screened = await ScreenPatientAsync(
                        patient, source, ct, facts, fullScreen, activeStudies);
                    if (screened.Error is not null && screened.Studies.Count == 0)
                        throw new InvalidOperationException(screened.Error);
                }
            }
        }

        var withEvidence = AttachDefaultEvidence(screened.Studies, parts);
        var letterDoc = documents.FirstOrDefault(d => d.Kind == ResearchDocumentKind.ConsultLetter);
        var saved = new ResearchAppointmentScreeningResult(
            consult.AppointmentId,
            consult.PatientCmId,
            consult.PatientName,
            DateTime.UtcNow,
            ResearchWorklistStatusMapper.FromStudies(withEvidence),
            source,
            withEvidence,
            consult.PatientRid,
            ResearchAnalysisMode.LatestConsultLetter,
            documents,
            letterRef?.ConsultRid,
            letterRef?.LettersRid ?? primary?.SourceRid,
            letterRef?.LetterDate ?? primary?.SourceDate,
            letterRef?.RelativePath ?? primary?.RelativePath,
            letterDoc?.FileName ?? primary?.FileName,
            fingerprint);
        await _results.UpsertAsync(saved, ct);
        return saved;
    }

    private static string BuildAgeOnlyNotes(ResearchDerivedFacts facts)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("CLINICAL SOURCE DOCUMENTS (CM2). Assess only what is stated below.");
        sb.AppendLine("No previous consultation letter, stress echo, or transthoracic echo report was available.");
        sb.AppendLine("Use derived facts only. Mark every criterion without supporting evidence UNCLEAR.");
        sb.AppendLine();
        sb.Append(ResearchClinicalFactsExtractor.FormatBlock(facts));
        return sb.ToString();
    }

    private async Task TryAddSupplementalEchoReportsAsync(
        string patientRid,
        List<ResearchSourceDocument> documents,
        List<(ResearchSourceDocument Doc, string Text)> parts,
        CancellationToken ct,
        bool includeStress = true,
        bool preferStressFirst = false)
    {
        // TEST_REQUIRED 1 = TTE; 2/3 = exercise / dobutamine stress echo (semantic model).
        async Task AddKindAsync(string kind, int[] codes)
        {
            if (parts.Any(p => p.Doc.Kind == kind) || documents.Any(d => d.Kind == kind))
                return;

            var report = await _cm2.GetLatestInvestigationReportAsync(patientRid, codes, ct);
            if (report is null)
                return;

            var extract = ResearchCm2DocumentReader.ReadStudyReport(_cm2, report, kind);
            documents.Add(extract.Document);
            if (extract.Document.Reviewed && !string.IsNullOrWhiteSpace(extract.ExtractedText))
                parts.Add((extract.Document, extract.ExtractedText!));
        }

        if (preferStressFirst && includeStress)
        {
            await AddKindAsync(ResearchDocumentKind.StressEchoReport, [2, 3]);
            await AddKindAsync(ResearchDocumentKind.TteReport, [1]);
            return;
        }

        await AddKindAsync(ResearchDocumentKind.TteReport, [1]);
        if (includeStress)
            await AddKindAsync(ResearchDocumentKind.StressEchoReport, [2, 3]);
    }

    private static string DescribeSource(IReadOnlyList<(ResearchSourceDocument Doc, string Text)> parts)
    {
        var hasLetter = parts.Any(p => p.Doc.Kind == ResearchDocumentKind.ConsultLetter);
        var hasEcho = parts.Any(p =>
            p.Doc.Kind is ResearchDocumentKind.TteReport or ResearchDocumentKind.StressEchoReport);
        if (hasLetter && hasEcho)
            return "cm2-consult-letter+echo";
        if (hasLetter)
            return "cm2-consult-letter";
        if (hasEcho)
            return "cm2-echo";
        return "cm2";
    }

    private static bool HasUndeterminedCriteria(IReadOnlyList<ResearchStudyScreening> studies) =>
        studies.Any(s =>
            !HasHardFail(s)
            && (s.Inclusion.Any(a => a.Status == CriterionStatus.Unclear)
                || s.Exclusion.Any(a => a.Status == CriterionStatus.Unclear)));

    private static bool HasHardFail(ResearchStudyScreening study) =>
        study.Inclusion.Any(a => a.Status == CriterionStatus.NotMet)
        || study.Exclusion.Any(a => a.Status == CriterionStatus.Met);

    private static bool HasInclusionNotMet(ResearchStudyScreening study) =>
        study.Inclusion.Any(a => a.Status == CriterionStatus.NotMet);

    private static bool NeedsMoreDocumentsForInclusion(
        IReadOnlyList<ResearchStudy> studies,
        ResearchDerivedFacts facts,
        bool fullScreen) =>
        studies.Any(study =>
        {
            var seeded = ApplyDerivedFacts(Blank(study), facts);
            return fullScreen || !HasInclusionNotMet(seeded);
        });

    private static IReadOnlyList<ResearchStudyScreening> AttachDefaultEvidence(
        IReadOnlyList<ResearchStudyScreening> studies,
        IReadOnlyList<(ResearchSourceDocument Doc, string Text)> parts)
    {
        // Prefer letter; otherwise the first reviewed report (stress/TTE fallback).
        var defaultDoc = parts.FirstOrDefault(p => p.Doc.Kind == ResearchDocumentKind.ConsultLetter).Doc
            ?? parts.FirstOrDefault().Doc;
        return studies.Select(study => study with
        {
            Inclusion = study.Inclusion.Select(a => FillEvidence(a, defaultDoc, parts)).ToList(),
            Exclusion = study.Exclusion.Select(a => FillEvidence(a, defaultDoc, parts)).ToList(),
        }).ToList();
    }

    private static ResearchCriterionAssessment FillEvidence(
        ResearchCriterionAssessment a,
        ResearchSourceDocument? defaultDoc,
        IReadOnlyList<(ResearchSourceDocument Doc, string Text)> parts)
    {
        if (!string.IsNullOrWhiteSpace(a.EvidenceFileName))
            return a;

        if (!string.IsNullOrWhiteSpace(a.EvidenceSourceKind))
        {
            var match = parts.Select(p => p.Doc)
                .FirstOrDefault(d => string.Equals(d.Kind, a.EvidenceSourceKind, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return a with
                {
                    EvidenceSourceKind = match.Kind,
                    EvidenceSourceRid = match.SourceRid,
                    EvidenceFileName = match.FileName,
                };
            }
            return a;
        }

        if (a.Status == CriterionStatus.Unclear)
        {
            return a with
            {
                Reason = string.IsNullOrWhiteSpace(a.Reason)
                    ? "Unable to determine from available records"
                    : a.Reason,
            };
        }

        if (defaultDoc is null)
            return a;

        return a with
        {
            EvidenceSourceKind = defaultDoc.Kind,
            EvidenceSourceRid = defaultDoc.SourceRid,
            EvidenceFileName = defaultDoc.FileName,
        };
    }

    private async Task<SyntheticScreeningResult> ScreenPatientAsync(
        SyntheticResearchPatient patient,
        string source,
        CancellationToken ct,
        ResearchDerivedFacts? facts,
        bool fullScreen,
        IReadOnlyList<ResearchStudy>? activeStudies = null)
    {
        var studies = activeStudies is { Count: > 0 }
            ? activeStudies
            : (await _studies.ListAsync(ct)).Where(s => s.IsActive).ToList();
        if (studies.Count == 0)
            return new SyntheticScreeningResult(patient, [], "No active studies to screen against.", source);

        var results = new ResearchStudyScreening[studies.Count];
        ResearchLlmStatus? llmStatus = null;
        for (var i = 0; i < studies.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var study = studies[i];
            var seeded = ApplyDerivedFacts(Blank(study), facts);
            if (!fullScreen && HasInclusionNotMet(seeded))
            {
                results[i] = StopAfterInclusionFail(seeded);
                continue;
            }

            llmStatus ??= await _llm.GetStatusAsync(ct);
            if (!llmStatus.Online)
            {
                return new SyntheticScreeningResult(
                    patient,
                    results.Where(r => r is not null).ToList(),
                    string.IsNullOrWhiteSpace(llmStatus.Message) ? "Local LLM is offline." : llmStatus.Message,
                    source);
            }

            results[i] = await ScreenStudyAsync(patient, study, llmStatus.ModelId, ct, facts, fullScreen);
        }

        return new SyntheticScreeningResult(patient, results.ToList(), null, source);
    }

    private async Task<ResearchStudyScreening> ScreenStudyAsync(
        SyntheticResearchPatient patient,
        ResearchStudy study,
        string? modelId,
        CancellationToken ct,
        ResearchDerivedFacts? facts,
        bool fullScreen)
    {
        var promptPatient = patient with { Notes = FitNotesForLlm(patient.Notes) };
        var messages = new ResearchLlmChatMessage[]
        {
            new("system", ClinicalSystemPrompt),
            new("user", BuildUserPrompt(promptPatient, study, fullScreen)),
        };

        var completion = await _llm.CompleteChatAsync(messages, modelId, ct);
        if (!completion.Ok || string.IsNullOrWhiteSpace(completion.Content))
        {
            var failed = ApplyDerivedFacts(
                Unassessed(study, completion.Error ?? "LLM did not return a screening result."),
                facts);
            return fullScreen || !HasInclusionNotMet(failed) ? failed : StopAfterInclusionFail(failed);
        }

        if (!TryParse(completion.Content, study, out var parsed))
        {
            var failed = ApplyDerivedFacts(
                Unassessed(study, "LLM returned a result that could not be parsed as JSON."),
                facts);
            return fullScreen || !HasInclusionNotMet(failed) ? failed : StopAfterInclusionFail(failed);
        }

        var applied = ApplyDerivedFacts(SanitizeScreening(parsed), facts);
        return fullScreen || !HasInclusionNotMet(applied) ? applied : StopAfterInclusionFail(applied);
    }

    private static ResearchStudyScreening Blank(ResearchStudy study)
    {
        var inclusion = study.InclusionCriteria
            .Select(c => new ResearchCriterionAssessment(c, CriterionStatus.Unclear, "Unable to determine from available records"))
            .ToList();
        var exclusion = study.ExclusionCriteria
            .Select(c => new ResearchCriterionAssessment(c, CriterionStatus.Unclear, "Unable to determine from available records"))
            .ToList();
        return new ResearchStudyScreening(
            study.Id, study.Name, ComputeOverall(inclusion, exclusion), "", inclusion, exclusion, null);
    }

    private const string StoppedAfterInclusionReason =
        "Not assessed — screening stopped after an inclusion criterion was not met.";

    private static ResearchStudyScreening StopAfterInclusionFail(ResearchStudyScreening screening)
    {
        var inclusion = screening.Inclusion.Select(a =>
            a.Status is CriterionStatus.Met or CriterionStatus.NotMet
                ? a
                : a with { Status = CriterionStatus.Unclear, Reason = StoppedAfterInclusionReason }).ToList();
        var exclusion = screening.Exclusion.Select(a =>
            a.Status is CriterionStatus.Met or CriterionStatus.NotMet
                ? a
                : a with { Status = CriterionStatus.Unclear, Reason = StoppedAfterInclusionReason }).ToList();
        var overall = ComputeOverall(inclusion, exclusion);
        var summary = string.IsNullOrWhiteSpace(screening.Summary)
            ? "Screening stopped after an inclusion criterion was not met."
            : $"{screening.Summary.Trim()} Screening stopped after an inclusion criterion was not met.";
        return screening with
        {
            Overall = overall,
            Summary = summary,
            Inclusion = inclusion,
            Exclusion = exclusion,
            Error = null,
        };
    }

    private static ResearchStudyScreening Unassessed(ResearchStudy study, string error)
    {
        var inclusion = study.InclusionCriteria
            .Select(c => new ResearchCriterionAssessment(c, CriterionStatus.Unclear, "Unable to determine from available records"))
            .ToList();
        var exclusion = study.ExclusionCriteria
            .Select(c => new ResearchCriterionAssessment(c, CriterionStatus.Unclear, "Unable to determine from available records"))
            .ToList();
        return new ResearchStudyScreening(
            study.Id,
            study.Name,
            ComputeOverall(inclusion, exclusion),
            error,
            inclusion,
            exclusion,
            error);
    }

    private const string ClinicalSystemPrompt =
        """
        You are a clinical research screening assistant for Hearts 1st.
        Assess only the provided CM2 source documents against the listed study criteria.
        Do not invent facts that are not supported by those documents.
        If a criterion is not addressed, or the documents are insufficient, mark it UNCLEAR
        with reason exactly: Unable to determine from available records.
        Never infer Met or NotMet from silence or typical practice.

        Source documents are labeled with kind, date, path, and fileName. Prefer citing the
        specific source that supports each criterion via evidenceSource.

        Derived facts may include age, height, weight, calculated BMI, and named GLP-1/GIP/amylin
        agents extracted from those same documents. Use them.

        Age:
        - If derived facts include age, use that value.
        - If an inclusion age range is stated (for example 18-69) and the age is outside
          that range, mark the criterion NOT_MET. Do not mark MET for an out-of-range age.

        BMI:
        - If BMI is stated, use it.
        - If BMI is not stated but height and weight are present, calculate BMI =
          weight_kg / (height_m)^2 and use that. Do not mark a BMI criterion UNCLEAR
          just because the letters do not print the word BMI.

        GLP-1 / GIP / amylin therapy:
        - Current documented use of Mounjaro/tirzepatide, Ozempic/Wegovy/semaglutide,
          Saxenda/Victoza/liraglutide, Trulicity/dulaglutide, or similar agents is recent
          therapy. For an exclusion of recent GLP-1/GIP/amylin therapy within one year,
          current use is MET (the exclusion is present).
        - Do not mark that exclusion NOT_MET because the notes describe current therapy
          rather than a previous completed course.

        Inclusion criteria polarity:
        - MET = the documents show the patient satisfies the inclusion requirement.
        - NOT_MET = the documents show the patient fails the inclusion requirement.

        Exclusion criteria polarity:
        - MET = the exclusion IS present (patient should be excluded).
        - NOT_MET = the exclusion does NOT apply based on the documents.

        The status field MUST agree with the reason field.
        Return JSON only, with no markdown and no extra commentary.
        """;

    private static string BuildUserPrompt(SyntheticResearchPatient patient, ResearchStudy study, bool fullScreen)
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
        text.AppendLine("Source documents:");
        text.AppendLine(patient.Notes);
        text.AppendLine(
            """
            Return JSON with this shape:
            {
              "overall": "LIKELY_SUITABLE | POSSIBLY_SUITABLE | NEEDS_REVIEW | LIKELY_NOT_SUITABLE",
              "summary": "one or two sentences",
              "inclusion": [{"criterion": "exact inclusion text", "status": "MET|NOT_MET|UNCLEAR", "reason": "short evidence", "evidenceSource": "CONSULT_LETTER|TTE_REPORT|STRESS_ECHO_REPORT"}],
              "exclusion": [{"criterion": "exact exclusion text", "status": "MET|NOT_MET|UNCLEAR", "reason": "short evidence", "evidenceSource": "CONSULT_LETTER|TTE_REPORT|STRESS_ECHO_REPORT"}]
            }
            Status and reason must agree.
            For UNCLEAR use reason "Unable to determine from available records".
            If height and weight are in the documents, calculate BMI and apply BMI criteria.
            If an inclusion age range is stated and the age is outside it, that criterion is NOT_MET.
            Current Mounjaro/tirzepatide or other GLP-1/GIP/amylin agonists count as recent therapy.
            For exclusions: a value below the stated threshold is NOT_MET (exclusion does not apply).
            Use LIKELY_NOT_SUITABLE if any inclusion is NOT_MET or any exclusion is MET.
            Use LIKELY_SUITABLE if Age, BMI, diabetes and HbA1c are all determined and favourable
            (inclusion MET / exclusion NOT_MET), even if the remaining criteria are UNCLEAR.
            Use LIKELY_SUITABLE if every inclusion is MET and every exclusion is NOT_MET.
            Use NEEDS_REVIEW if Age, BMI, diabetes or HbA1c is UNCLEAR and there is no hard fail.
            """);
        if (fullScreen)
        {
            text.AppendLine("This is a full re-screen. Assess every inclusion and exclusion criterion.");
        }
        else
        {
            text.AppendLine(
                "Assess inclusion criteria in listed order. If any inclusion is NOT_MET, do not assess further inclusion rows or any exclusion; mark those UNCLEAR with reason \"Not assessed — screening stopped after an inclusion criterion was not met.\"");
        }

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
        result = Unassessed(study, "Unparsed");
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
        var summary = ResearchOverall.BuildSummary(overall, inclusion, exclusion, correctedStatuses: corrected);

        return screening with
        {
            Overall = overall,
            Summary = summary,
            Inclusion = inclusion,
            Exclusion = exclusion,
        };
    }

    /// <summary>
    /// Overrides BMI and GLP-1/GIP/amylin rows using height/weight/drugs extracted from the RTFs.
    /// Current Mounjaro (tirzepatide) counts as recent GLP-1/GIP therapy.
    /// </summary>
    private static ResearchStudyScreening ApplyDerivedFacts(
        ResearchStudyScreening screening,
        ResearchDerivedFacts? facts)
    {
        if (facts is null)
            return screening;

        var changed = 0;
        var inclusion = screening.Inclusion
            .Select(a => ApplyDerivedFact(a, isExclusion: false, facts, ref changed))
            .ToList();
        var exclusion = screening.Exclusion
            .Select(a => ApplyDerivedFact(a, isExclusion: true, facts, ref changed))
            .ToList();

        var corrected = inclusion.Concat(exclusion).Count(a =>
            a.Reason.Contains("[corrected from ", StringComparison.OrdinalIgnoreCase));
        var overall = ComputeOverall(inclusion, exclusion);
        var summary = ResearchOverall.BuildSummary(
            overall, inclusion, exclusion, correctedStatuses: corrected, derivedFacts: changed);

        return screening with
        {
            Overall = overall,
            Summary = summary,
            Inclusion = inclusion,
            Exclusion = exclusion,
        };
    }

    private static ResearchCriterionAssessment ApplyDerivedFact(
        ResearchCriterionAssessment assessment,
        bool isExclusion,
        ResearchDerivedFacts facts,
        ref int changed)
    {
        if (facts.HasAge
            && ResearchAgeCriterion.TryParseRange(assessment.Criterion, out var minAge, out var maxAge))
        {
            var age = facts.AgeYears!.Value;
            var inRange = age >= minAge && age <= maxAge;
            var status = inRange ? CriterionStatus.Met : CriterionStatus.NotMet;
            var next = assessment with
            {
                Status = status,
                Reason = inRange
                    ? $"Age {age} years."
                    : $"Age {age} years is outside {minAge}–{maxAge} years.",
                EvidenceFileName = facts.AgeSource is not null && facts.AgeSource != "CM2 date of birth"
                    ? facts.AgeSource
                    : assessment.EvidenceFileName,
            };
            if (next.Status != assessment.Status
                || !string.Equals(next.Reason, assessment.Reason, StringComparison.Ordinal))
                changed++;
            return next;
        }

        if (facts.HasBmi && IsBmiCriterion(assessment.Criterion)
            && TryParseNumericThreshold(assessment.Criterion, out var op, out var threshold))
        {
            var bmi = facts.Bmi!.Value;
            var meets = CompareThreshold(bmi, op, threshold);
            var status = meets ? CriterionStatus.Met : CriterionStatus.NotMet;
            var how = facts.BmiCalculated
                ? $"calculated from height {FormatFactNumber(facts.HeightCm)} cm and weight {FormatFactNumber(facts.WeightKg)} kg"
                : "stated in the source documents";
            var next = assessment with
            {
                Status = status,
                Reason = $"BMI {bmi:0.0} kg/m2 ({how}).",
                EvidenceFileName = facts.BmiSource ?? assessment.EvidenceFileName,
            };
            if (next.Status != assessment.Status
                || !string.Equals(next.Reason, assessment.Reason, StringComparison.Ordinal))
                changed++;
            return next;
        }

        if (facts.HasGlp1Therapy && IsGlp1TherapyCriterion(assessment.Criterion))
        {
            var agents = string.Join(", ", facts.Glp1Agents);
            var absenceRequired = RequiresTherapyAbsence(assessment.Criterion);
            string status;
            string reason;
            if (isExclusion)
            {
                // Exclusion of recent GLP-1/GIP/amylin: current documented use = exclusion present.
                status = absenceRequired ? CriterionStatus.NotMet : CriterionStatus.Met;
                reason = absenceRequired
                    ? $"{agents} documented, so an absence-of-therapy exclusion does not apply."
                    : $"{agents} documented. Current use counts as recent GLP-1/GIP/amylin therapy within the past year.";
            }
            else
            {
                status = absenceRequired ? CriterionStatus.NotMet : CriterionStatus.Met;
                reason = absenceRequired
                    ? $"{agents} documented, so the patient is not GLP-1/GIP/amylin naive."
                    : $"{agents} documented.";
            }

            var next = assessment with
            {
                Status = status,
                Reason = reason,
                EvidenceFileName = facts.Glp1Source ?? assessment.EvidenceFileName,
            };
            if (next.Status != assessment.Status
                || !string.Equals(next.Reason, assessment.Reason, StringComparison.Ordinal))
                changed++;
            return next;
        }

        return assessment;
    }

    private static bool IsBmiCriterion(string criterion) =>
        Regex.IsMatch(criterion, @"\b(bmi|body mass index)\b", RegexOptions.IgnoreCase);

    private static bool IsGlp1TherapyCriterion(string criterion) =>
        Regex.IsMatch(
            criterion,
            @"\b(glp-?\s*1|gip|amylin|tirzepatide|semaglutide|liraglutide|dulaglutide|incretin)\b",
            RegexOptions.IgnoreCase);

    private static bool RequiresTherapyAbsence(string criterion) =>
        Regex.IsMatch(
            criterion,
            @"\b(naive|no (current |recent )?(glp|gip|amylin)|without (glp|gip|amylin)|not (on|receiving))\b",
            RegexOptions.IgnoreCase);

    private static bool TryParseNumericThreshold(string criterion, out string op, out double threshold)
    {
        op = ">=";
        threshold = 0;
        var match = Regex.Match(
            criterion,
            @"(greater(?:\s+than)?\s+or\s+equal(?:\s+to)?|at\s+least|minimum|≥|>=|greater than|more than|over|above|>|less(?:\s+than)?\s+or\s+equal(?:\s+to)?|at\s+most|maximum|≤|<=|less than|under|below|<)\s*(\d{2}(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        if (!match.Success)
        {
            var fallback = Regex.Match(criterion, @"(\d{2}(?:\.\d+)?)");
            if (!fallback.Success
                || !double.TryParse(fallback.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out threshold))
                return false;
            op = ">=";
            return threshold is >= 10 and <= 90;
        }

        var rawOp = match.Groups[1].Value.ToLowerInvariant();
        op = rawOp switch
        {
            ">" or "greater than" or "more than" or "over" or "above" => ">",
            "<" or "less than" or "under" or "below" => "<",
            "≤" or "<=" or "at most" or "maximum" => "<=",
            _ when rawOp.Contains("less", StringComparison.Ordinal) => "<=",
            _ => ">=",
        };
        return double.TryParse(match.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out threshold)
            && threshold is >= 10 and <= 90;
    }

    private static bool CompareThreshold(double value, string op, double threshold) => op switch
    {
        ">" => value > threshold,
        "<" => value < threshold,
        "<=" => value <= threshold,
        _ => value >= threshold,
    };

    private static string FormatFactNumber(double? value) =>
        value is null
            ? "?"
            : value.Value % 1 == 0
                ? value.Value.ToString("0")
                : value.Value.ToString("0.#");

    private static ResearchCriterionAssessment SanitizeAssessment(ResearchCriterionAssessment assessment)
    {
        assessment = ResearchAgeCriterion.Apply(assessment);
        if (ResearchAgeCriterion.TryParseRange(assessment.Criterion, out _, out _)
            && ResearchAgeCriterion.TryParseStatedAge(assessment.Reason, out _))
            return assessment;

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

        // Named current GLP-1/GIP agents mean recent therapy is documented. Do not let
        // "no mention of prior therapy" flip that to NOT_MET.
        var namesCurrentGlp1 = Regex.IsMatch(
            text,
            @"\b(mounjaro|zepbound|tirzepatide|ozempic|wegovy|rybelsus|semaglutide|saxenda|victoza|liraglutide|trulicity|dulaglutide|byetta|bydureon|exenatide|retatrutide|cagrilintide|pramlintide|symlin)\b",
            RegexOptions.IgnoreCase);

        // Strong "does not apply / below threshold / absent" signals → NOT_MET
        if (Regex.IsMatch(text,
                @"\b(below|under|less than|does not meet|do not meet|not meet|not met|no (type|history|evidence|mention|known)|not (diagnosed|stated|present|triggered|explicit)|absent|does not apply|no diabetes)\b",
                RegexOptions.IgnoreCase))
        {
            if (namesCurrentGlp1 && Regex.IsMatch(text, @"\bno mention of prior\b", RegexOptions.IgnoreCase))
                return CriterionStatus.Met;
            return CriterionStatus.NotMet;
        }

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

    private static ResearchDerivedFacts WithConsultAge(ResearchDerivedFacts facts, ResearchUpcomingConsult consult)
        => facts.WithAge(consult.AgeYears, consult.AgeYears is null ? null : "CM2 date of birth");

    private string FitNotesForLlm(string notes)
    {
        if (string.IsNullOrEmpty(notes))
            return notes;

        // Rough English token estimate (~4 chars/token). Leave room for system prompt,
        // study criteria, and the JSON completion inside the llama.cpp context window.
        var context = Math.Clamp(_llmSettings.ContextSize, 2048, 128_000);
        var budgetChars = Math.Max(4000, (context - 2800) * 4);
        if (notes.Length <= budgetChars)
            return notes;

        return notes[..budgetChars].TrimEnd()
            + "\n\n[Source text truncated to fit the local LLM context window.]";
    }

    private static string ComputeOverall(
        IReadOnlyList<ResearchCriterionAssessment> inclusion,
        IReadOnlyList<ResearchCriterionAssessment> exclusion) =>
        ResearchOverall.FromCriteria(inclusion, exclusion);

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
            var evidence = ReadString(item, "evidenceSource")
                ?? ReadString(item, "evidenceSourceKind");
            items.Add(new ResearchCriterionAssessment(
                criterion,
                NormalizeCriterionStatus(ReadString(item, "status")),
                ReadString(item, "reason") ?? "",
                string.IsNullOrWhiteSpace(evidence) ? null : evidence.Trim().ToUpperInvariant()));
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
