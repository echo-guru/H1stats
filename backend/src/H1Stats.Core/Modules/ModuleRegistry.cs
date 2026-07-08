namespace H1Stats.Core.Modules;

/// <summary>Pluggable application module — future modules register without core changes.</summary>
public interface IAppModule
{
    string Id { get; }
    string DisplayName { get; }
    string RoutePrefix { get; }
}

public sealed class ClinicalModule : IAppModule
{
    public string Id => "clinical";
    public string DisplayName => "Clinical";
    public string RoutePrefix => "/clinical";
}

public sealed class AdministrationModule : IAppModule
{
    public string Id => "administration";
    public string DisplayName => "Administration";
    public string RoutePrefix => "/admin";
}

/// <summary>Central registry for module discovery and permission checks.</summary>
public class ModuleRegistry
{
    private readonly IReadOnlyList<IAppModule> _modules;

    public ModuleRegistry(IEnumerable<IAppModule> modules)
    {
        _modules = modules.ToList();
    }

    public IReadOnlyList<IAppModule> GetAll() => _modules;

    public bool IsValidModule(string moduleId) =>
        _modules.Any(m => m.Id.Equals(moduleId, StringComparison.OrdinalIgnoreCase));
}
