using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Resolves template entry routing tokens into undoable property edits.</summary>
public sealed class BpmnTemplateBindingCommandFactory(BpmnElementTemplateRegistry registry) : IBpmnBindingCommandFactory
{
    /// <summary>Creates a factory using configured component options.</summary>
    public BpmnTemplateBindingCommandFactory(IOptions<BpmnOptions> options) : this(options.Value.Templates.Registry) { }
    /// <inheritdoc />
    public string BindingType => "template";
    /// <inheritdoc />
    public BpmnEditCommand? Create(string elementId, BpmnPropertyEntry entry, string? value,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows)
    {
        var parameters = entry.Binding?.Parameters;
        if (rows is not null || parameters is null || !parameters.TryGetValue("templateId", out var id) ||
            !parameters.TryGetValue("propertyKey", out var key)) return null;
        int? version = int.TryParse(parameters.GetValueOrDefault("version"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
        return registry.TryGetExact(id, version) is { } template ? new SetTemplatePropertyCommand(elementId, template, key, value) : null;
    }
}
