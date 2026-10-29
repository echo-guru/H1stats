using System.Text.Json;
using H1Stats.Core.Configuration;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// Effective on/off for weekday auto-screen + Slack. Appsettings is the default;
/// a small App_Data override lets Research staff toggle without restarting for config edits
/// (API still needs to be running to honour the change on the next schedule check).
/// </summary>
public sealed class ResearchDailyAutomationStore : IResearchDailyAutomationStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private readonly IOptionsMonitor<ResearchDailySlackSettings> _settings;
    private readonly string _overridePath;

    public ResearchDailyAutomationStore(
        IOptionsMonitor<ResearchDailySlackSettings> settings,
        IHostEnvironment env)
    {
        _settings = settings;
        _overridePath = Path.Combine(env.ContentRootPath, "App_Data", "research-daily-automation.json");
    }

    public ResearchDailyAutomationStatus GetStatus()
    {
        lock (_gate)
        {
            var settings = _settings.CurrentValue;
            var overrideEnabled = ReadOverrideUnlocked();
            var enabled = overrideEnabled ?? settings.Enabled;
            var source = overrideEnabled is null ? "config" : "override";
            var message = enabled
                ? $"Weekday auto-screen & Slack is ON ({settings.Hour:00}:{settings.Minute:00} local)."
                : "Weekday auto-screen & Slack is OFF. Manual Screen all / Send to Slack still work.";
            return new ResearchDailyAutomationStatus(
                enabled,
                Math.Clamp(settings.Hour, 0, 23),
                Math.Clamp(settings.Minute, 0, 59),
                settings.WeekdaysOnly,
                settings.TimeZoneId,
                source,
                message);
        }
    }

    public bool IsEnabled => GetStatus().Enabled;

    public ResearchDailyAutomationStatus SetEnabled(bool enabled)
    {
        lock (_gate)
        {
            var dir = Path.GetDirectoryName(_overridePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            var payload = JsonSerializer.Serialize(new OverrideFile(enabled), JsonOptions);
            File.WriteAllText(_overridePath, payload);
            return GetStatus();
        }
    }

    private bool? ReadOverrideUnlocked()
    {
        try
        {
            if (!File.Exists(_overridePath))
                return null;
            var raw = File.ReadAllText(_overridePath);
            var parsed = JsonSerializer.Deserialize<OverrideFile>(raw, JsonOptions);
            return parsed?.Enabled;
        }
        catch
        {
            return null;
        }
    }

    private sealed record OverrideFile(bool Enabled);
}
