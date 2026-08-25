using System.Text.Json;
using H1Stats.Core.Interfaces;
using H1Stats.Core.Models;

namespace H1Stats.Infrastructure.Research;

/// <summary>
/// JSON-backed study store. Replace with a database later as the study catalogue grows.
/// </summary>
public sealed class ResearchStudyStore : IResearchStudyStore
{
    private const int MaxNameLength = 200;
    private const int MaxDescriptionLength = 8000;
    private const int MaxCriterionLength = 2000;
    private const int MaxCriteriaPerList = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly object _lock = new();
    private readonly string _filePath;
    private List<ResearchStudy> _studies;

    public ResearchStudyStore(string filePath)
    {
        _filePath = filePath;
        _studies = Load();
    }

    public Task<IReadOnlyList<ResearchStudy>> ListAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ResearchStudy>>(
                _studies.OrderByDescending(s => s.IsActive).ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList());
        }
    }

    public Task<ResearchStudy?> GetAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            return Task.FromResult(_studies.FirstOrDefault(s => s.Id == id));
        }
    }

    public Task<(ResearchStudy? Study, string? Error)> CreateAsync(
        SaveResearchStudyRequest request, CancellationToken ct = default)
    {
        var (normalized, error) = Normalize(request, existingId: null);
        if (error is not null)
            return Task.FromResult<(ResearchStudy?, string?)>((null, error));

        var now = DateTime.UtcNow;
        var study = normalized! with
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = now,
            UpdatedAt = now,
        };

        lock (_lock)
        {
            if (NameTaken(study.Name, exceptId: null))
                return Task.FromResult<(ResearchStudy?, string?)>((null, "A study with this name already exists"));

            _studies.Add(study);
            PersistUnlocked();
        }

        return Task.FromResult<(ResearchStudy?, string?)>((study, null));
    }

    public Task<(ResearchStudy? Study, string? Error)> UpdateAsync(
        string id, SaveResearchStudyRequest request, CancellationToken ct = default)
    {
        var (normalized, error) = Normalize(request, existingId: id);
        if (error is not null)
            return Task.FromResult<(ResearchStudy?, string?)>((null, error));

        lock (_lock)
        {
            var index = _studies.FindIndex(s => s.Id == id);
            if (index < 0)
                return Task.FromResult<(ResearchStudy?, string?)>((null, "Study not found"));

            if (NameTaken(normalized!.Name, exceptId: id))
                return Task.FromResult<(ResearchStudy?, string?)>((null, "A study with this name already exists"));

            var updated = normalized with
            {
                Id = id,
                CreatedAt = _studies[index].CreatedAt,
                UpdatedAt = DateTime.UtcNow,
            };
            _studies[index] = updated;
            PersistUnlocked();
            return Task.FromResult<(ResearchStudy?, string?)>((updated, null));
        }
    }

    public Task<(bool Deleted, string? Error)> DeleteAsync(string id, CancellationToken ct = default)
    {
        lock (_lock)
        {
            var index = _studies.FindIndex(s => s.Id == id);
            if (index < 0)
                return Task.FromResult<(bool, string?)>((false, "Study not found"));

            _studies.RemoveAt(index);
            PersistUnlocked();
            return Task.FromResult<(bool, string?)>((true, null));
        }
    }

    private bool NameTaken(string name, string? exceptId) =>
        _studies.Any(s =>
            (exceptId is null || s.Id != exceptId)
            && s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static (ResearchStudy? Study, string? Error) Normalize(SaveResearchStudyRequest request, string? existingId)
    {
        var name = request.Name?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(name))
            return (null, "Study name is required");
        if (name.Length > MaxNameLength)
            return (null, $"Study name must be {MaxNameLength} characters or fewer");

        var description = (request.Description ?? "").Trim();
        if (description.Length > MaxDescriptionLength)
            return (null, $"Description must be {MaxDescriptionLength} characters or fewer");

        var inclusion = NormalizeCriteria(request.InclusionCriteria, "inclusion");
        if (inclusion.Error is not null)
            return (null, inclusion.Error);

        var exclusion = NormalizeCriteria(request.ExclusionCriteria, "exclusion");
        if (exclusion.Error is not null)
            return (null, exclusion.Error);

        if (inclusion.Items.Count == 0 && exclusion.Items.Count == 0)
            return (null, "Add at least one inclusion or exclusion criterion");

        var now = DateTime.UtcNow;
        return (new ResearchStudy(
            existingId ?? "",
            name,
            description,
            inclusion.Items,
            exclusion.Items,
            request.IsActive,
            now,
            now), null);
    }

    private static (IReadOnlyList<string> Items, string? Error) NormalizeCriteria(
        IReadOnlyList<string>? raw, string kind)
    {
        var items = (raw ?? [])
            .Select(item => item?.Trim() ?? "")
            .Where(item => item.Length > 0)
            .ToList();

        if (items.Count > MaxCriteriaPerList)
            return ([], $"A study can have at most {MaxCriteriaPerList} {kind} criteria");

        if (items.Any(item => item.Length > MaxCriterionLength))
            return ([], $"Each {kind} criterion must be {MaxCriterionLength} characters or fewer");

        return (items, null);
    }

    private List<ResearchStudy> Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return [];

            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<List<ResearchStudy>>(json, JsonOptions);
            return loaded?
                .Where(s => !string.IsNullOrWhiteSpace(s.Id) && !string.IsNullOrWhiteSpace(s.Name))
                .ToList() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void PersistUnlocked()
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(_studies, JsonOptions);
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }
}
