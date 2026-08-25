using System.Text.Json;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// JSON-backed appointment screening results. Keyed by SCHEDULE_APPOINTMENT_RID.
/// </summary>
public sealed class ResearchScreeningResultStore : IResearchScreeningResultStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly object _lock = new();
    private readonly string _filePath;
    private Dictionary<string, ResearchAppointmentScreeningResult> _byAppointmentId;

    public ResearchScreeningResultStore(string filePath)
    {
        _filePath = filePath;
        _byAppointmentId = Load();
    }

    public Task<ResearchAppointmentScreeningResult?> GetAsync(string appointmentId, CancellationToken ct = default)
    {
        var key = NormalizeId(appointmentId);
        if (key is null)
            return Task.FromResult<ResearchAppointmentScreeningResult?>(null);

        lock (_lock)
        {
            return Task.FromResult(
                _byAppointmentId.TryGetValue(key, out var result) ? result : null);
        }
    }

    public Task<IReadOnlyDictionary<string, ResearchAppointmentScreeningResult>> GetManyAsync(
        IEnumerable<string> appointmentIds, CancellationToken ct = default)
    {
        var keys = appointmentIds
            .Select(NormalizeId)
            .Where(k => k is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        lock (_lock)
        {
            var map = new Dictionary<string, ResearchAppointmentScreeningResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                if (_byAppointmentId.TryGetValue(key, out var result))
                    map[key] = result;
            }
            return Task.FromResult<IReadOnlyDictionary<string, ResearchAppointmentScreeningResult>>(map);
        }
    }

    public Task UpsertAsync(ResearchAppointmentScreeningResult result, CancellationToken ct = default)
    {
        var key = NormalizeId(result.AppointmentId)
            ?? throw new ArgumentException("Appointment id is required.", nameof(result));

        var saved = result with
        {
            AppointmentId = key,
            Status = ResearchWorklistStatusMapper.FromStudies(result.Studies),
            Studies = result.Studies.ToList(),
        };

        lock (_lock)
        {
            _byAppointmentId[key] = saved;
            PersistUnlocked();
        }

        return Task.CompletedTask;
    }

    public Task<(bool Deleted, string? Error)> DeleteAsync(string appointmentId, CancellationToken ct = default)
    {
        var key = NormalizeId(appointmentId);
        if (key is null)
            return Task.FromResult<(bool, string?)>((false, "Appointment id is required"));

        lock (_lock)
        {
            if (!_byAppointmentId.Remove(key))
                return Task.FromResult<(bool, string?)>((false, "Screening result not found"));
            PersistUnlocked();
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    private Dictionary<string, ResearchAppointmentScreeningResult> Load()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            if (!File.Exists(_filePath))
                return new Dictionary<string, ResearchAppointmentScreeningResult>(StringComparer.OrdinalIgnoreCase);

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json))
                return new Dictionary<string, ResearchAppointmentScreeningResult>(StringComparer.OrdinalIgnoreCase);

            var list = JsonSerializer.Deserialize<List<ResearchAppointmentScreeningResult>>(json, JsonOptions)
                ?? [];
            return list
                .Where(r => !string.IsNullOrWhiteSpace(r.AppointmentId))
                .GroupBy(r => NormalizeId(r.AppointmentId)!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderByDescending(x => x.ScreenedAt).First(),
                    StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, ResearchAppointmentScreeningResult>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void PersistUnlocked()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var list = _byAppointmentId.Values
            .OrderByDescending(r => r.ScreenedAt)
            .ToList();
        var json = JsonSerializer.Serialize(list, JsonOptions);
        var temp = _filePath + ".tmp";
        File.WriteAllText(temp, json);
        File.Copy(temp, _filePath, overwrite: true);
        File.Delete(temp);
    }

    private static string? NormalizeId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return null;
        return id.Trim();
    }
}
