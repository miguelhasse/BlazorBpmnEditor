using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>Configures and stores element templates for a BPMN editor.</summary>
public sealed class BpmnTemplateOptions
{
    /// <summary>Opt-in standard-entry hiding requested by entriesVisible. Defaults to false.</summary>
    public bool HonorEntriesVisible { get; set; }

    /// <summary>Shows registered compatible templates in the built-in stencil. Defaults to true.</summary>
    public bool ShowInStencil { get; set; } = true;

    /// <summary>Optional engine versions used to filter compatible templates.</summary>
    public Dictionary<string, string> Engines { get; } = new(StringComparer.Ordinal);

    private readonly List<BpmnTemplateLoadError> _loadErrors = [];
    private readonly Lock _gate = new();

    /// <summary>Registry shared by editors using these options.</summary>
    public BpmnElementTemplateRegistry Registry { get; } = new();

    /// <summary>Diagnostics from parse and duplicate-registration failures.</summary>
    public IReadOnlyList<BpmnTemplateLoadError> LoadErrors
    {
        get { lock (_gate) return _loadErrors.ToArray(); }
    }

    /// <summary>Loads and registers templates from JSON text.</summary>
    public BpmnTemplateLoadResult AddTemplates(string json)
    {
        var result = BpmnElementTemplateLoader.Load(json);
        var additions = Registry.AddRange(result.Templates);
        var errors = result.Errors.Concat(additions.Where(a => a.Error is not null).Select(a => a.Error!)).ToArray();
        lock (_gate) _loadErrors.AddRange(errors);
        return new(result.Templates.Where((_, index) => additions[index].Added).ToArray(), errors);
    }

    /// <summary>Loads and registers templates from a JSON stream.</summary>
    public BpmnTemplateLoadResult AddTemplates(Stream stream)
    {
        var result = BpmnElementTemplateLoader.Load(stream);
        var additions = Registry.AddRange(result.Templates);
        var errors = result.Errors.Concat(additions.Where(a => a.Error is not null).Select(a => a.Error!)).ToArray();
        lock (_gate) _loadErrors.AddRange(errors);
        return new(result.Templates.Where((_, index) => additions[index].Added).ToArray(), errors);
    }
}
