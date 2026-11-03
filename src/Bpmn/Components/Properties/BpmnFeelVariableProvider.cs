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

    /// <summary>Host or model origin of this hint, not a runtime availability guarantee.</summary>
    public string? Source { get; init; }

    /// <summary>Whether this hint was inferred rather than declared by the host.</summary>
    public bool IsInferred { get; init; }
}

/// <summary>Supplies expression variable hints for a selected element.</summary>
public interface IBpmnFeelVariableProvider
{
    /// <summary>Resolves variables from the document for the selected element.</summary>
    public ValueTask<IReadOnlyList<BpmnFeelVariable>> GetVariablesAsync(BpmnDocument document, string elementId,
        CancellationToken ct = default);
}

/// <summary>Collects Zeebe variable hints, optionally restricted to selected-element containment scopes.</summary>
/// <param name="scoped">Restrict hints to owning/ancestor scopes; defaults to document-wide discovery.</param>
public sealed class BpmnFeelVariableProvider(bool scoped = false) : IBpmnFeelVariableProvider
{
    /// <summary>Creates the default document-wide hint provider.</summary>
    public BpmnFeelVariableProvider() : this(false) { }

    private const string ZeebeUri = "http://camunda.org/schema/zeebe/1.0";

    /// <inheritdoc />
    public ValueTask<IReadOnlyList<BpmnFeelVariable>> GetVariablesAsync(BpmnDocument document, string elementId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ct.ThrowIfCancellationRequested();
        var names = new HashSet<string>(StringComparer.Ordinal);
        var scopes = new HashSet<string>(StringComparer.Ordinal);
        var scopeDepth = new Dictionary<string, int>(StringComparer.Ordinal);
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);
        if (scoped)
        {
            var current = document.ElementIndex.GetValueOrDefault(elementId) is BpmnFlowNode selected
                ? selected.ContainerId : elementId;
            while (scopes.Add(current) && document.ElementIndex.GetValueOrDefault(current) is BpmnFlowNode owner)
            {
                scopeDepth[current] = scopeDepth.Count;
                current = owner.ContainerId;
            }
            scopeDepth.TryAdd(current, scopeDepth.Count);
        }
        foreach (var node in document.FlowNodes.OrderByDescending(node => scopeDepth.GetValueOrDefault(node.ContainerId))
                     .ThenBy(node => node.Id == elementId ? 1 : 0).ThenBy(node => node.Id, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            if (scoped && node.Id != elementId && !scopes.Contains(node.ContainerId))
            {
                continue;
            }
            var nodeNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (var extension in node.Extras.Extensions)
            {
                Collect(extension, nodeNames, !scoped || node.Id == elementId, ct);
            }
            foreach (var name in nodeNames)
            {
                names.Add(name);
                origins[name] = node.Id;
            }
        }
        IReadOnlyList<BpmnFeelVariable> variables = names.Order(StringComparer.Ordinal)
            .Select(name => new BpmnFeelVariable(name)
            {
                Source = "BPMN element " + origins[name],
                IsInferred = true,
            }).ToArray();
        return ValueTask.FromResult(variables);
    }

    private static void Collect(BpmnExtensionElement extension, HashSet<string> names, bool includeInputs, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (extension.Namespace == ZeebeUri)
        {
            if (extension.LocalName == "ioMapping")
            {
                foreach (var child in extension.Children.Where(child => child.Namespace == ZeebeUri &&
                    (child.LocalName == "output" || includeInputs && child.LocalName == "input")))
                    Add(child.Attributes.GetValueOrDefault("target"), names);
            }
            if (extension.LocalName != "taskDefinition") Add(extension.Attributes.GetValueOrDefault("resultVariable"), names);
        }
        foreach (var child in extension.Children) Collect(child, names, includeInputs, ct);
    }

    private static void Add(string? name, HashSet<string> names)
    {
        if (!string.IsNullOrWhiteSpace(name)) names.Add(name);
    }
}
