namespace H1Stats.Core.Configuration;

/// <summary>
/// Weekday morning automation: screen today's Research consults and post the candidate report to Slack.
/// Uses Brisbane local time by default (no DST). Requires <see cref="ResearchSlackSettings.WebhookUrl"/>.
/// </summary>
public class ResearchDailySlackSettings
{
    public const string SectionName = "ResearchDailySlack";
    public const string EnvEnabled = "RESEARCH_DAILY_SLACK_ENABLED";

    /// <summary>When true, the API runs the daily screen + Slack post on a schedule.</summary>
    public bool Enabled { get; set; } = false;

    /// <summary>Local hour (0–23) in <see cref="TimeZoneId"/>. Default 6am.</summary>
    public int Hour { get; set; } = 6;

    /// <summary>Local minute (0–59). Default 0.</summary>
    public int Minute { get; set; } = 0;

    /// <summary>When true, skip Saturday and Sunday.</summary>
    public bool WeekdaysOnly { get; set; } = true;

    /// <summary>
    /// Windows: <c>E. Australia Standard Time</c> (Brisbane). Linux: <c>Australia/Brisbane</c>.
    /// </summary>
    public string TimeZoneId { get; set; } = "E. Australia Standard Time";

    public static void ApplyEnvironmentOverrides(ResearchDailySlackSettings settings)
    {
        var enabled = Environment.GetEnvironmentVariable(EnvEnabled);
        if (string.Equals(enabled, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase))
            settings.Enabled = true;
        else if (string.Equals(enabled, "0", StringComparison.OrdinalIgnoreCase)
            || string.Equals(enabled, "false", StringComparison.OrdinalIgnoreCase))
            settings.Enabled = false;
    }

    public TimeZoneInfo ResolveTimeZone()
    {
        foreach (var id in new[] { TimeZoneId, "E. Australia Standard Time", "Australia/Brisbane" })
        {
            if (string.IsNullOrWhiteSpace(id)) continue;
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        // Fixed UTC+10 fallback (Brisbane has no DST).
        return TimeZoneInfo.CreateCustomTimeZone("H1Stats-Brisbane", TimeSpan.FromHours(10), "Brisbane", "Brisbane");
    }
}
