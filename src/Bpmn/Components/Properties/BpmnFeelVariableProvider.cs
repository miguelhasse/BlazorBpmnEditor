using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>A variable hint for FEEL expression completion.</summary>
/// <param name="Name">Variable name.</param>
/// <param name="Detail">Short type or detail text.</param>
/// <param name="Info">Additional documentation.</param>
public sealed record BpmnFeelVariable(string Name, string? Detail = null, string? Info = null)
{
    /// <summary>Nested variable members.</summary>
    public IReadOnlyList<BpmnFeelVariable> Entries { get; init; } = [];
}

/// <summary>Supplies expression variable hints for a selected element.</summary>
public interface IBpmnFeelVariableProvider
{
    /// <summary>Resolves variables from the document for the selected element.</summary>
    public ValueTask<IReadOnlyList<BpmnFeelVariable>> GetVariablesAsync(BpmnDocument document, string elementId,
        CancellationToken ct = default);
}

/// <summary>Collects document-wide Zeebe IO targets and result variables without scope inference.</summary>
public sealed class BpmnFeelVariableProvider : IBpmnFeelVariableProvider
{
    private const string ZeebeUri = "http://camunda.org/schema/zeebe/1.0";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<BpmnFeelVariable>> GetVariablesAsync(BpmnDocument document, string elementId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ct.ThrowIfCancellationRequested();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in document.FlowNodes)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var extension in node.Extras.Extensions) Collect(extension, names, ct);
        }
        IReadOnlyList<BpmnFeelVariable> variables = names.Order(StringComparer.Ordinal)
            .Select(name => new BpmnFeelVariable(name)).ToArray();
        return ValueTask.FromResult(variables);
    }

    private static void Collect(BpmnExtensionElement extension, HashSet<string> names, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (extension.Namespace == ZeebeUri)
        {
            if (extension.LocalName == "ioMapping")
            {
                foreach (var child in extension.Children.Where(child => child.Namespace == ZeebeUri && child.LocalName is "input" or "output"))
                    Add(child.Attributes.GetValueOrDefault("target"), names);
            }
            if (extension.LocalName != "taskDefinition") Add(extension.Attributes.GetValueOrDefault("resultVariable"), names);
        }
        foreach (var child in extension.Children) Collect(child, names, ct);
    }

    private static void Add(string? name, HashSet<string> names)
    {
        if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
    }
}
