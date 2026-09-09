using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.Extensions.Options;

namespace H1Stats.Infrastructure.Llm;

/// <summary>
/// Authenticated llama.cpp client. Status uses GET /v1/models.
/// Chat completions (future screening) always disable Qwen thinking mode.
/// </summary>
public sealed class ResearchLlmClient : IResearchLlmClient
{
    private const int StatusTimeoutSeconds = 15;

    private readonly HttpClient _http;
    private readonly ResearchLlmSettings _settings;

    public ResearchLlmClient(HttpClient http, IOptions<ResearchLlmSettings> options)
    {
        _http = http;
        _settings = options.Value;
        var timeout = Math.Clamp(_settings.TimeoutSeconds, 1, 3600);
        _http.Timeout = TimeSpan.FromSeconds(timeout);
    }

    public async Task<ResearchLlmStatus> GetStatusAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.NormalizedBaseUrl))
            return Offline("LLM base URL is not configured on the H1Stats server.");

        if (!_settings.HasApiKey)
            return Offline("LLM API key is not configured on the H1Stats server.");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(StatusTimeoutSeconds));

        try
        {
            using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/v1/models");
            using var response = await _http.SendAsync(request, timeoutCts.Token);
            var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return Offline("LLM rejected the API key.");
            }

            if (!response.IsSuccessStatusCode)
                return Offline($"LLM returned HTTP {(int)response.StatusCode}.");

            var modelId = ReadPrimaryModelId(body);
            if (string.IsNullOrWhiteSpace(modelId))
                return Offline("LLM responded but listed no models.");

            var display = FormatDisplayName(modelId);
            return new ResearchLlmStatus(
                true,
                $"Local LLM: Online — {display}",
                modelId,
                _settings.ContextSize,
                string.Empty);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Offline("LLM did not respond in time.");
        }
        catch (HttpRequestException)
        {
            return Offline("Cannot reach the local LLM.");
        }
        catch (Exception)
        {
            return Offline("LLM status check failed.");
        }
    }

    public async Task<ResearchLlmChatResult> CompleteChatAsync(
        IReadOnlyList<ResearchLlmChatMessage> messages,
        string? modelId = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.NormalizedBaseUrl))
            return new ResearchLlmChatResult(false, null, "LLM base URL is not configured on the H1Stats server.", null);

        if (!_settings.HasApiKey)
            return new ResearchLlmChatResult(false, null, "LLM API key is not configured on the H1Stats server.", null);

        try
        {
            var model = string.IsNullOrWhiteSpace(modelId) ? await ResolveModelIdAsync(ct) : modelId.Trim();
            if (string.IsNullOrWhiteSpace(model))
                return new ResearchLlmChatResult(false, null, "LLM listed no models.", null);

            var body = CreateChatCompletionBody(model, messages);
            using var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            using var request = CreateAuthenticatedRequest(HttpMethod.Post, "/v1/chat/completions", content);
            using var response = await _http.SendAsync(request, ct);
            var payload = await response.Content.ReadAsStringAsync(ct);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized ||
                response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new ResearchLlmChatResult(false, null, "LLM rejected the API key.", model);
            }

            if (!response.IsSuccessStatusCode)
                return new ResearchLlmChatResult(false, null, $"LLM returned HTTP {(int)response.StatusCode}.", model);

            var text = ReadAssistantContent(payload);
            if (string.IsNullOrWhiteSpace(text))
                return new ResearchLlmChatResult(false, null, "LLM returned an empty completion.", model);

            return new ResearchLlmChatResult(true, text, null, model);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return new ResearchLlmChatResult(
                false,
                null,
                $"LLM did not respond in time (timed out after {_settings.TimeoutSeconds} seconds). This is the llama.cpp completion, not a CM2 file fetch.",
                modelId);
        }
        catch (HttpRequestException ex)
        {
            var detail = string.IsNullOrWhiteSpace(ex.InnerException?.Message)
                ? ex.Message
                : $"{ex.Message} ({ex.InnerException.Message})";
            return new ResearchLlmChatResult(false, null, $"Cannot reach the local LLM: {detail}", modelId);
        }
        catch (IOException ex)
        {
            return new ResearchLlmChatResult(
                false,
                null,
                $"LLM connection dropped while reading the completion: {ex.Message}",
                modelId);
        }
        catch (Exception ex)
        {
            return new ResearchLlmChatResult(false, null, $"LLM completion failed: {ex.Message}", modelId);
        }
    }

    /// <summary>Builds a chat-completions JSON body with Qwen thinking disabled.</summary>
    public static JsonObject CreateChatCompletionBody(string model, IEnumerable<ResearchLlmChatMessage> messages)
    {
        var messageArray = new JsonArray();
        foreach (var message in messages)
        {
            messageArray.Add(new JsonObject
            {
                ["role"] = message.Role,
                ["content"] = message.Content,
            });
        }

        return new JsonObject
        {
            ["model"] = model,
            ["messages"] = messageArray,
            ["temperature"] = 0,
            ["max_tokens"] = 1536,
            ["chat_template_kwargs"] = new JsonObject
            {
                ["enable_thinking"] = false,
            },
        };
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string relativePath, HttpContent? content = null)
    {
        var url = $"{_settings.NormalizedBaseUrl}{relativePath}";
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _settings.ApiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (content is not null && content.Headers.ContentType is null)
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = Encoding.UTF8.WebName };
        return request;
    }

    private ResearchLlmStatus Offline(string message) =>
        new(false, "Local LLM: Offline", null, _settings.ContextSize, message);

    private async Task<string?> ResolveModelIdAsync(CancellationToken ct)
    {
        using var request = CreateAuthenticatedRequest(HttpMethod.Get, "/v1/models");
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(StatusTimeoutSeconds));
        using var response = await _http.SendAsync(request, timeoutCts.Token);
        if (!response.IsSuccessStatusCode)
            return null;
        var body = await response.Content.ReadAsStringAsync(timeoutCts.Token);
        return ReadPrimaryModelId(body);
    }

    private static string? ReadAssistantContent(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        if (!document.RootElement.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var choice in choices.EnumerateArray())
        {
            if (choice.TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                return content.GetString();
            }
        }

        return null;
    }

    private static string? ReadPrimaryModelId(string json)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = document.RootElement;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            return ReadModelName(data);

        if (root.TryGetProperty("models", out var models) && models.ValueKind == JsonValueKind.Array)
            return ReadModelName(models);

        return null;
    }

    private static string? ReadModelName(JsonElement array)
    {
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var key in new[] { "id", "name", "model" })
            {
                if (item.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    var text = value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                        return text;
                }
            }
        }

        return null;
    }

    internal static string FormatDisplayName(string modelId)
    {
        var name = modelId.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
            name = name[(slash + 1)..];

        if (name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            name = name[..^5];

        if (Regex.IsMatch(name, @"qwen\s*3.*8\s*b", RegexOptions.IgnoreCase))
            return "Qwen3 8B";

        name = Regex.Replace(name, @"[-_.]?(?:IQ|Q|F|BF)\d+(?:_[A-Z0-9]+)*$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"[-_]+", " ").Trim();
        return string.IsNullOrWhiteSpace(name) ? modelId : name;
    }
}
