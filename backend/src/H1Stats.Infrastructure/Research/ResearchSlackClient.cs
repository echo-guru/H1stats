using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.Extensions.Options;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// Posts Research candidate reports to a Slack channel via Incoming Webhook.
/// </summary>
public sealed class ResearchSlackClient : IResearchSlackClient
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HttpClient _http;
    private readonly ResearchSlackSettings _settings;

    public ResearchSlackClient(HttpClient http, IOptions<ResearchSlackSettings> options)
    {
        _http = http;
        _settings = options.Value;
        _http.Timeout = TimeSpan.FromSeconds(30);
    }

    public ResearchSlackStatus GetStatus() =>
        new(_settings.IsConfigured,
            _settings.IsConfigured
                ? "Slack webhook is configured."
                : "Slack webhook is not configured. Set ResearchSlack:WebhookUrl or RESEARCH_SLACK_WEBHOOK_URL.");

    public async Task<(bool Ok, string? Error)> PostCandidateReportAsync(
        string markdownText, CancellationToken ct = default)
    {
        if (!_settings.IsConfigured)
            return (false, "Slack webhook is not configured on the H1Stats server.");

        if (string.IsNullOrWhiteSpace(markdownText))
            return (false, "Report text is empty.");

        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                text = ToSlackMrkdwn(markdownText.Trim()),
                mrkdwn = true,
            }, JsonOptions);

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Post, _settings.WebhookUrl.Trim())
            {
                Content = content,
            };
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
            {
                var detail = string.IsNullOrWhiteSpace(body) ? response.ReasonPhrase : body.Trim();
                return (false, $"Slack returned HTTP {(int)response.StatusCode}: {detail}");
            }

            return (true, null);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return (false, "Slack did not respond in time.");
        }
        catch (Exception ex)
        {
            return (false, $"Could not reach Slack: {ex.Message}");
        }
    }

    /// <summary>Convert common Markdown bold to Slack mrkdwn (*bold*).</summary>
    internal static string ToSlackMrkdwn(string markdown) =>
        Regex.Replace(markdown, @"\*\*(.+?)\*\*", "*$1*", RegexOptions.Singleline);
}
