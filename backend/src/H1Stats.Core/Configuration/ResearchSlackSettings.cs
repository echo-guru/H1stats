namespace H1Stats.Core.Configuration;

/// <summary>
/// Slack Incoming Webhook for Research candidate reports.
/// Create a Slack app Incoming Webhook aimed at the target channel, then set the URL
/// via <c>RESEARCH_SLACK_WEBHOOK_URL</c> or <c>ResearchSlack:WebhookUrl</c>.
/// Never expose the webhook URL to the browser.
/// </summary>
public class ResearchSlackSettings
{
    public const string SectionName = "ResearchSlack";
    public const string EnvWebhookUrl = "RESEARCH_SLACK_WEBHOOK_URL";

    /// <summary>Incoming Webhook URL for the destination channel. Empty until configured.</summary>
    public string WebhookUrl { get; set; } = "";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(WebhookUrl)
        && Uri.TryCreate(WebhookUrl.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    public static void ApplyEnvironmentOverrides(ResearchSlackSettings settings)
    {
        var url = Environment.GetEnvironmentVariable(EnvWebhookUrl);
        if (!string.IsNullOrWhiteSpace(url))
            settings.WebhookUrl = url.Trim();
    }
}
