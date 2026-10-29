using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using H1Stats.Core.Models;

namespace H1Stats.Infrastructure.Research;

public sealed class ResearchDerivedFacts
{
    public double? HeightCm { get; init; }
    public string? HeightSource { get; init; }
    public double? WeightKg { get; init; }
    public string? WeightSource { get; init; }
    public double? Bmi { get; init; }
    public bool BmiCalculated { get; init; }
    public string? BmiSource { get; init; }
    public IReadOnlyList<string> Glp1Agents { get; init; } = [];
    public string? Glp1Source { get; init; }
    public int? AgeYears { get; init; }
    public string? AgeSource { get; init; }

    public bool HasBmi => Bmi is >= 10 and <= 90;
    public bool HasGlp1Therapy => Glp1Agents.Count > 0;
    public bool HasAge => AgeYears is >= 0 and <= 120;

    public ResearchDerivedFacts WithAge(int? years, string? source)
    {
        if (years is null)
            return this;
        return new ResearchDerivedFacts
        {
            HeightCm = HeightCm,
            HeightSource = HeightSource,
            WeightKg = WeightKg,
            WeightSource = WeightSource,
            Bmi = Bmi,
            BmiCalculated = BmiCalculated,
            BmiSource = BmiSource,
            Glp1Agents = Glp1Agents,
            Glp1Source = Glp1Source,
            AgeYears = years,
            AgeSource = source,
        };
    }
}

/// <summary>
/// Pulls height, weight, BMI and GLP-1/GIP/amylin agents from extracted RTF text
/// so screening does not depend on the letters using the word "BMI".
/// </summary>
public static class ResearchClinicalFactsExtractor
{
    private static readonly (string Pattern, string Label)[] Glp1Agents =
    [
        ("mounjaro", "Mounjaro (tirzepatide)"),
        ("zepbound", "Zepbound (tirzepatide)"),
        ("tirzepatide", "tirzepatide"),
        ("ozempic", "Ozempic (semaglutide)"),
        ("wegovy", "Wegovy (semaglutide)"),
        ("rybelsus", "Rybelsus (semaglutide)"),
        ("semaglutide", "semaglutide"),
        ("saxenda", "Saxenda (liraglutide)"),
        ("victoza", "Victoza (liraglutide)"),
        ("liraglutide", "liraglutide"),
        ("trulicity", "Trulicity (dulaglutide)"),
        ("dulaglutide", "dulaglutide"),
        ("byetta", "Byetta (exenatide)"),
        ("bydureon", "Bydureon (exenatide)"),
        ("exenatide", "exenatide"),
        ("retatrutide", "retatrutide"),
        ("cagrilintide", "cagrilintide"),
        ("pramlintide", "pramlintide"),
        ("symlin", "Symlin (pramlintide)"),
    ];

    /// <summary>
    /// Brand/generic names recognised for GLP-1 / GIP / amylin therapy screening.
    /// </summary>
    public static string AgentSearchPhrases =>
        "Ozempic | semaglutide | Wegovy | Rybelsus | Trulicity | dulaglutide | Victoza | Saxenda | liraglutide | Mounjaro | tirzepatide | Zepbound | Byetta | Bydureon | exenatide | pramlintide | Symlin";

    private readonly record struct FactHit(double Value, string Source, DateOnly? Date);

    public static ResearchDerivedFacts Extract(IReadOnlyList<(ResearchSourceDocument Doc, string Text)> parts)
    {
        var heights = new List<FactHit>();
        var weights = new List<FactHit>();
        double? explicitBmi = null;
        string? explicitBmiSource = null;
        var agents = new List<string>();
        string? glp1Source = null;
        var ages = new List<FactHit>();

        foreach (var (doc, text) in parts)
        {
            var source = doc.FileName ?? doc.Kind;
            var normalized = Normalize(text);

            // Echo headers often concatenate vitals: "186cm107kgBSA2.31m2"
            if (TryFindHtWtPair(normalized, out var pairHeight, out var pairWeight))
            {
                heights.Add(new FactHit(pairHeight, source, doc.SourceDate));
                weights.Add(new FactHit(pairWeight, source, doc.SourceDate));
            }

            if (TryFindHeightCm(normalized, out var h))
                heights.Add(new FactHit(h, source, doc.SourceDate));

            if (TryFindWeightKg(normalized, out var w))
                weights.Add(new FactHit(w, source, doc.SourceDate));

            if (explicitBmi is null && TryFindExplicitBmi(normalized, out var statedBmi))
            {
                explicitBmi = statedBmi;
                explicitBmiSource = source;
            }

            if (TryFindAgeYears(normalized, out var ageYears))
                ages.Add(new FactHit(ageYears, source, doc.SourceDate));

            foreach (var (pattern, label) in Glp1Agents)
            {
                if (agents.Contains(label))
                    continue;
                if (!MentionsAgent(normalized, pattern))
                    continue;
                agents.Add(label);
                glp1Source ??= source;
            }
        }

        var weight = Latest(weights);
        FactHit height = default;
        if (weight.Source is not null)
            height = heights.LastOrDefault(h => h.Source == weight.Source);
        if (height.Source is null)
            height = Latest(heights);

        double? bmi = explicitBmi;
        var calculated = false;
        string? bmiSource = explicitBmiSource;
        if (bmi is null && height.Source is not null && weight.Source is not null)
        {
            var metres = height.Value / 100.0;
            bmi = Math.Round(weight.Value / (metres * metres), 1, MidpointRounding.AwayFromZero);
            calculated = true;
            bmiSource = height.Source == weight.Source
                ? height.Source
                : $"{height.Source} / {weight.Source}";
        }

        var age = ages.FirstOrDefault(a => a.Value is >= 18 and <= 110);
        if (age.Source is null)
            age = Latest(ages);

        return new ResearchDerivedFacts
        {
            HeightCm = height.Source is null ? null : height.Value,
            HeightSource = height.Source,
            WeightKg = weight.Source is null ? null : weight.Value,
            WeightSource = weight.Source,
            Bmi = bmi,
            BmiCalculated = calculated,
            BmiSource = bmiSource,
            Glp1Agents = agents,
            Glp1Source = glp1Source,
            AgeYears = age.Source is null ? null : (int)Math.Round(age.Value, MidpointRounding.AwayFromZero),
            AgeSource = age.Source,
        };
    }

    private static FactHit Latest(List<FactHit> hits)
    {
        if (hits.Count == 0)
            return default;
        return hits
            .OrderByDescending(h => h.Date ?? DateOnly.MinValue)
            .ThenByDescending(_ => 0)
            .First();
    }

    public static string FormatBlock(ResearchDerivedFacts facts)
    {
        if (!facts.HasBmi && facts.HeightCm is null && facts.WeightKg is null && !facts.HasGlp1Therapy && !facts.HasAge)
            return "";

        var sb = new StringBuilder();
        sb.AppendLine("===== DERIVED FACTS =====");
        sb.AppendLine("Values computed from the source documents. Use these for age, BMI and medication criteria.");
        if (facts.HasAge)
            sb.AppendLine($"- Age: {facts.AgeYears} years" + SourceSuffix(facts.AgeSource));
        if (facts.HeightCm is double height)
            sb.AppendLine($"- Height: {FormatNumber(height)} cm" + SourceSuffix(facts.HeightSource));
        if (facts.WeightKg is double weight)
            sb.AppendLine($"- Weight: {FormatNumber(weight)} kg" + SourceSuffix(facts.WeightSource));
        if (facts.Bmi is double bmi)
        {
            var how = facts.BmiCalculated
                ? $"calculated as weight_kg / height_m^2 from height {FormatNumber(facts.HeightCm ?? 0)} cm and weight {FormatNumber(facts.WeightKg ?? 0)} kg"
                : "stated in the source";
            sb.AppendLine($"- BMI: {bmi:0.0} kg/m2 ({how})" + SourceSuffix(facts.BmiSource));
        }
        if (facts.HasGlp1Therapy)
        {
            sb.AppendLine(
                $"- GLP-1/GIP/amylin therapy in use (named agent): {string.Join(", ", facts.Glp1Agents)}"
                + SourceSuffix(facts.Glp1Source));
            sb.AppendLine(
                "  Count only current or prior documented use of a named agent. Discussion, counselling,");
            sb.AppendLine(
                "  or a recommendation to start therapy does NOT count as having taken the therapy.");
        }
        sb.AppendLine("===== DERIVED FACTS END =====");
        sb.AppendLine();
        return sb.ToString();
    }

    private static string Normalize(string text) =>
        text.Replace('\u00a0', ' ').Replace('\t', ' ');

    private static bool TryFindHtWtPair(string text, out double cm, out double kg)
    {
        cm = 0;
        kg = 0;
        var match = Regex.Match(
            text,
            @"(?<![\d.])(\d{2,3}(?:\.\d+)?)\s*cm\s*(\d{2,3}(?:\.\d+)?)\s*kgs?\b",
            RegexOptions.IgnoreCase);
        return match.Success
            && TryRange(match.Groups[1].Value, 100, 230, out cm)
            && TryRange(match.Groups[2].Value, 30, 300, out kg);
    }

    private static bool TryFindHeightCm(string text, out double cm)
    {
        cm = 0;
        var cmMatch = Regex.Match(
            text,
            @"\b(?:height|ht)\b(?:\s*\([^)]+\))?\s*[:\-]?\s*(\d{2,3}(?:\.\d+)?)\s*cm\b",
            RegexOptions.IgnoreCase);
        if (cmMatch.Success && TryRange(cmMatch.Groups[1].Value, 100, 230, out cm))
            return true;

        var metres = Regex.Match(
            text,
            @"\b(?:height|ht)\b(?:\s*\([^)]+\))?\s*[:\-]?\s*(1\.\d{1,2})\s*m(?:etres?)?\b",
            RegexOptions.IgnoreCase);
        if (metres.Success && TryRange(metres.Groups[1].Value, 1.2, 2.3, out var m))
        {
            cm = Math.Round(m * 100, 1, MidpointRounding.AwayFromZero);
            return true;
        }

        var htWt = Regex.Match(
            text,
            @"\bht\s*/\s*wt\b\s*[:\-]?\s*(\d{2,3}(?:\.\d+)?)\s*/\s*(\d{2,3}(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        if (htWt.Success && TryRange(htWt.Groups[1].Value, 100, 230, out cm))
            return true;

        return false;
    }

    private static bool TryFindWeightKg(string text, out double kg)
    {
        kg = 0;
        var kgMatch = Regex.Match(
            text,
            @"\b(?:weight|wt)\b(?:\s*\([^)]+\))?\s*[:\-]?\s*(\d{2,3}(?:\.\d+)?)\s*kgs?\b",
            RegexOptions.IgnoreCase);
        if (kgMatch.Success && TryRange(kgMatch.Groups[1].Value, 30, 300, out kg))
            return true;

        var lbMatch = Regex.Match(
            text,
            @"\b(?:weight|wt)\b(?:\s*\([^)]+\))?\s*[:\-]?\s*(\d{2,3}(?:\.\d+)?)\s*(?:lb|lbs|pounds?)\b",
            RegexOptions.IgnoreCase);
        if (lbMatch.Success && TryRange(lbMatch.Groups[1].Value, 66, 660, out var lb))
        {
            kg = Math.Round(lb * 0.45359237, 1, MidpointRounding.AwayFromZero);
            return true;
        }

        var htWt = Regex.Match(
            text,
            @"\bht\s*/\s*wt\b\s*[:\-]?\s*(\d{2,3}(?:\.\d+)?)\s*/\s*(\d{2,3}(?:\.\d+)?)",
            RegexOptions.IgnoreCase);
        if (htWt.Success && TryRange(htWt.Groups[2].Value, 30, 300, out kg))
            return true;

        return false;
    }

    private static bool TryFindAgeYears(string text, out double years)
    {
        years = 0;
        var match = Regex.Match(
            text,
            @"\b(\d{1,3})\s*(?:-?\s*year(?:s)?[-\s]?old)\b",
            RegexOptions.IgnoreCase);
        if (match.Success && TryRange(match.Groups[1].Value, 18, 110, out years))
            return true;

        match = Regex.Match(
            text,
            @"\b(?:age|aged)\s*[:\-]?\s*(\d{1,3})\s*(?:years?)?\b",
            RegexOptions.IgnoreCase);
        return match.Success && TryRange(match.Groups[1].Value, 18, 110, out years);
    }

    private static bool TryFindExplicitBmi(string text, out double bmi)
    {
        bmi = 0;
        var match = Regex.Match(
            text,
            @"\b(?:bmi|body mass index)\b(?:\s*\([^)]+\))?\s*[:\-]?\s*(\d{2}(?:\.\d+)?)\b",
            RegexOptions.IgnoreCase);
        return match.Success && TryRange(match.Groups[1].Value, 10, 90, out bmi);
    }

    private static bool MentionsAgent(string text, string pattern)
    {
        foreach (Match match in Regex.Matches(text, $@"\b{Regex.Escape(pattern)}\b", RegexOptions.IgnoreCase))
        {
            if (IsActualTherapyMention(text, match))
                return true;
        }

        return false;
    }

    /// <summary>
    /// True when the agent name appears as documented use, not only as a discussion or recommendation.
    /// </summary>
    public static bool IsActualTherapyMention(string text, Match match)
    {
        var start = Math.Max(0, match.Index - 120);
        var end = Math.Min(text.Length, match.Index + match.Length + 80);
        var window = text[start..end];

        // Explicit non-use / cessation nearby
        if (Regex.IsMatch(
                window,
                @"\b(not on|never (been )?on|no longer|ceased|stopped|discontinued|denied|no evidence of|has not (been )?(started|commenced|taken)|not (been )?prescribed)\b",
                RegexOptions.IgnoreCase))
            return false;

        // Discussion / recommendation to start — do not treat as having taken the therapy
        if (Regex.IsMatch(
                window,
                @"\b(recommend(?:ed|ing|ation)?|consider(?:ed|ing|ation)?|discuss(?:ed|ing|ion)?|suggest(?:ed|ing|ion)?|propos(?:e|ed|ing)|plan(?:s|ned|ning)?\s+to|will\s+(?:be\s+)?(?:start|commence|trial|initiate)|(?:to|should|could|may|might)\s+(?:start|commence|initiate|trial|begin)|start(?:ing)?\s+(?:her|him|them|the\s+patient)\s+on|candidate\s+for|may\s+benefit|offer(?:ed|ing)?|think(?:ing)?\s+about|talk(?:ed|ing)?\s+about|option\s+of|possible\s+(?:role\s+for|use\s+of)|counsell?(?:ed|ing)?|advised?\s+to\s+start|keen\s+to\s+start|interested\s+in)\b",
                RegexOptions.IgnoreCase))
            return false;

        return true;
    }

    /// <summary>True when reason text names a recognised GLP-1/GIP/amylin agent.</summary>
    public static bool ReasonNamesGlp1Agent(string? reason) =>
        !string.IsNullOrWhiteSpace(reason)
        && Regex.IsMatch(
            reason,
            @"\b(mounjaro|zepbound|tirzepatide|ozempic|wegovy|rybelsus|semaglutide|saxenda|victoza|liraglutide|trulicity|dulaglutide|byetta|bydureon|exenatide|retatrutide|cagrilintide|pramlintide|symlin)\b",
            RegexOptions.IgnoreCase);

    /// <summary>True when reason describes discussion/recommendation rather than documented use.</summary>
    public static bool ReasonIsDiscussionOnly(string? reason) =>
        !string.IsNullOrWhiteSpace(reason)
        && Regex.IsMatch(
            reason,
            @"\b(recommend(?:ed|ing|ation)?|consider(?:ed|ing)?|discuss(?:ed|ing|ion)?|suggest(?:ed|ing)?|propos(?:e|ed)|plan(?:s|ned|ning)? to|will (?:be )?(?:start|commence|trial)|(?:to|should) start|candidate for|may benefit|counsell?(?:ed|ing)?|advised to start|interested in|option of)\b",
            RegexOptions.IgnoreCase)
        && !Regex.IsMatch(
            reason,
            @"\b(currently (?:on|taking)|taking|prescribed|continues on|remains on|commenced on|started on|has been on|was (?:on|taking)|recent(?:ly)? (?:used|taken|started)|history of (?:use|therapy))\b",
            RegexOptions.IgnoreCase);

    private static bool TryRange(string raw, double min, double max, out double value)
    {
        value = 0;
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return false;
        if (parsed < min || parsed > max)
            return false;
        value = parsed;
        return true;
    }

    private static string FormatNumber(double value) =>
        value % 1 == 0 ? value.ToString("0", CultureInfo.InvariantCulture) : value.ToString("0.#", CultureInfo.InvariantCulture);

    private static string SourceSuffix(string? source) =>
        string.IsNullOrWhiteSpace(source) ? "" : $" [{source}]";
}
