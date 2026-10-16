using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Creates editor commands for registered entry binding kinds.</summary>
public interface IBpmnBindingCommandFactory
{
    /// <summary>The binding discriminator this factory handles.</summary>
    public string BindingType { get; }
    /// <summary>Creates an edit command, or null when this binding cannot be handled.</summary>
    public BpmnEditCommand? Create(string elementId, BpmnPropertyEntry entry, string? value,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows);
}

/// <summary>Converts snapshot-bound property edits into undoable editor commands.</summary>
public static class BpmnPropertyEntryCommands
{
    /// <summary>Creates a command for an entry using optional host-registered binding factories.</summary>
    public static BpmnEditCommand? ToCommand(string elementId, BpmnPropertyEntry entry, string? value,
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows = null,
        IEnumerable<IBpmnBindingCommandFactory>? factories = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(elementId);
        ArgumentNullException.ThrowIfNull(entry);
        var binding = entry.Binding;
        if (binding is null) return null;
        foreach (var factory in factories ?? [])
            if (factory.BindingType == binding.Type && factory.Create(elementId, entry, value, rows) is { } custom)
                return custom;

        if (binding.Type == "foreign-attribute" && rows is null)
            return new SetForeignAttributeCommand(elementId, binding.Parameters["namespace"], binding.Parameters["name"], value);
        if (binding.Type == "camunda-collection" && rows is not null)
            return new ReplaceCamundaCollectionCommand(elementId, binding.Parameters.GetValueOrDefault("container"), binding.Parameters["itemLocalName"], rows);
        if (binding.Type == "extension-loop" && binding.Parameters.TryGetValue("path", out var loopPath) && rows is null)
            return new SetLoopExtensionValueCommand(elementId, loopPath, value);
        if (binding.Type == "extension" && binding.Parameters.TryGetValue("path", out var path) && rows is null)
            return new SetExtensionValueCommand(elementId, path, value);
        if (binding.Type == "extension-collection" && rows is not null
            && binding.Parameters.TryGetValue("container", out var container)
            && binding.Parameters.TryGetValue("itemNamespace", out var itemNamespace)
            && binding.Parameters.TryGetValue("itemLocalName", out var itemLocalName))
            return new ReplaceExtensionCollectionCommand(elementId, container, itemNamespace, itemLocalName, rows);
        return null;
    }
}
