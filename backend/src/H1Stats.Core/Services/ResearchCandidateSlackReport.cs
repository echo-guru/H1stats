using System.Globalization;
using System.Text.RegularExpressions;
using H1Stats.Core.Models;

namespace H1Stats.Core.Services;

/// <summary>
/// Builds the Potential Candidate Report markdown used by Copy Results and Send to Slack.
/// Keep in sync with <c>buildCandidateSlack</c> in ResearchHome.tsx.
/// </summary>
public static class ResearchCandidateSlackReport
{
    private static readonly string[] Months =
        ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    public static string Build(IEnumerable<ResearchUpcomingConsult> consults, DateOnly reportDate)
    {
        var linesByDoctor = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var doctorOrder = new List<string>();

        foreach (var row in consults)
        {
            var positive = new List<(string StudyName, string Classification)>();
            foreach (var study in row.Studies ?? [])
            {
                var classification = ClassificationLabel(study);
                if (classification is not null)
                    positive.Add((study.StudyName, classification));
            }

            if (positive.Count == 0)
                continue;

            var heading = DoctorHeading(row.CardiologistName);
            if (!linesByDoctor.TryGetValue(heading, out var lines))
            {
                lines = [];
                linesByDoctor[heading] = lines;
                doctorOrder.Add(heading);
            }

            var when = FormatTime(row.AppointmentTime);
            var name = PatientName(row.PatientName);
            foreach (var item in positive)
            {
                var parts = new List<string> { name };
                if (!string.IsNullOrWhiteSpace(when))
                    parts.Add(when!);
                parts.Add(item.StudyName);
                parts.Add($"**{item.Classification}**");
                lines.Add("- " + string.Join(" — ", parts));
            }
        }

        var partsOut = new List<string>
        {
            $"**Potential Candidate Report - {FormatReportDate(reportDate)}**",
            "",
        };

        if (doctorOrder.Count == 0)
        {
            partsOut.Add("No Suitable Candidates found.");
            return string.Join("\n", partsOut) + "\n";
        }

        foreach (var doctor in doctorOrder)
        {
            partsOut.Add($"**{doctor}**");
            partsOut.AddRange(linesByDoctor[doctor]);
            partsOut.Add("");
        }

        return string.Join("\n", partsOut).TrimEnd() + "\n";
    }

    public static string FormatReportDate(DateOnly date)
    {
        var weekday = date.ToString("dddd", CultureInfo.GetCultureInfo("en-AU"));
        return $"{weekday} {DayOrdinal(date.Day)} {Months[date.Month - 1]} {date.Year}";
    }

    internal static string? ClassifyStudy(ResearchStudyScreening study)
    {
        if (study.Overall == ResearchSuitability.LikelySuitable)
        {
            var unclear = study.Inclusion.Concat(study.Exclusion)
                .Any(row => row.Status == CriterionStatus.Unclear);
            return unclear ? "Likely Suitable" : "Suitable";
        }

        if (study.Overall == ResearchSuitability.PossiblySuitable)
            return "Likely Suitable";
        if (study.Overall == ResearchSuitability.NeedsReview)
            return "Unclear";
        return null;
    }

    internal static string? ClassificationLabel(ResearchStudyScreening study)
    {
        var classification = ClassifyStudy(study);
        if (classification is null)
            return null;
        if (classification != "Unclear")
            return classification;
        var hint = UnclearHint(study);
        return hint is null ? "Unclear" : $"Unclear — {hint}";
    }

    /// <summary>
    /// Brief doctor-facing hints for Unclear rows. Skips motivation / weight-loss attempt / MRI.
    /// </summary>
    internal static string? UnclearHint(ResearchStudyScreening study)
    {
        var hints = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        void Add(string hint)
        {
            if (seen.Add(hint))
                hints.Add(hint);
        }

        foreach (var row in study.Inclusion.Concat(study.Exclusion))
        {
            if (row.Status != CriterionStatus.Unclear)
                continue;
            var c = row.Criterion ?? "";
            if (Regex.IsMatch(c, @"\bmotivat|\bweight-?\s*loss attempt|\bdietary weight|\bmri\b", RegexOptions.IgnoreCase))
                continue;
            if (Regex.IsMatch(c, @"\bhba1c\b", RegexOptions.IgnoreCase))
                Add("Check HbA1c");
            else if (Regex.IsMatch(c, @"\bdiabetes\b", RegexOptions.IgnoreCase))
                Add("Check diabetes");
            else if (Regex.IsMatch(c, @"\bbmi\b|body mass index", RegexOptions.IgnoreCase))
                Add("Check BMI");
            else if (Regex.IsMatch(c, @"\bage\b", RegexOptions.IgnoreCase))
                Add("Check age");
            else if (Regex.IsMatch(c, @"\bknee\b", RegexOptions.IgnoreCase)
                     && Regex.IsMatch(c, @"\boa\b|\bosteoarthritis\b", RegexOptions.IgnoreCase))
                Add("Check knee OA");
            else if (Regex.IsMatch(c, @"\bglp|gip|amylin|tirzepatide|semaglutide|liraglutide|dulaglutide", RegexOptions.IgnoreCase))
                Add("Check GLP-1");
        }

        return hints.Count == 0 ? null : string.Join(", ", hints);
    }

    private static string DayOrdinal(int day)
    {
        var mod100 = day % 100;
        if (mod100 is >= 11 and <= 13)
            return $"{day}th";
        return (day % 10) switch
        {
            1 => $"{day}st",
            2 => $"{day}nd",
            3 => $"{day}rd",
            _ => $"{day}th",
        };
    }

    private static string PatientName(string stored)
    {
        var trimmed = (stored ?? "").Trim();
        var comma = trimmed.IndexOf(',');
        if (comma <= 0)
            return trimmed.ToUpperInvariant();
        var last = trimmed[..comma].Trim().ToUpperInvariant();
        var first = trimmed[(comma + 1)..].Trim();
        return !string.IsNullOrEmpty(first) && !string.IsNullOrEmpty(last)
            ? $"{first} {last}"
            : trimmed;
    }

    private static string DoctorHeading(string? name)
    {
        var raw = (name ?? "").Trim();
        if (raw.Length == 0)
            return "Unassigned";
        return Regex.IsMatch(raw, @"^dr\b", RegexOptions.IgnoreCase) ? raw : $"Dr {raw}";
    }

    private static string? FormatTime(string? time)
    {
        if (string.IsNullOrWhiteSpace(time))
            return null;
        var m = Regex.Match(time.Trim(), @"^(\d{1,2}):(\d{2})");
        if (!m.Success)
            return time.Trim();
        var hour = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        var minute = m.Groups[2].Value;
        var suffix = hour >= 12 ? "pm" : "am";
        hour %= 12;
        if (hour == 0)
            hour = 12;
        return $"{hour}:{minute} {suffix}";
    }
}
