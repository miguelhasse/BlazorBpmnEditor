using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;

using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Icons;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Resources;

using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Localizes built-in presentation text without changing document values or host captions.</summary>
public sealed class BpmnDisplayLocalizer(IStringLocalizer<BpmnStrings> localizer)
{
    private static readonly ResourceManager English = new(typeof(BpmnStrings));
    private static readonly Regex IndexedProperty = new(
        @"^(assignment|multiInstance\.complexBehavior)\.(\d+)\.(.+)$", RegexOptions.CultureInvariant);

    /// <summary>Reads the built-in resources for the current UI culture; used when no host localizer is supplied.</summary>
    internal static BpmnDisplayLocalizer CurrentCulture { get; } = new(new BpmnResourceStringLocalizer());

    /// <summary>Returns a localized caption only when the supplied caption is the built-in English default.</summary>
    public string BuiltIn(string key, string fallback, params object[] arguments)
    {
        var english = English.GetString(key, CultureInfo.InvariantCulture);
        if (english is null || string.Format(CultureInfo.InvariantCulture, english, arguments) != fallback) return fallback;
        var translated = arguments.Length == 0 ? localizer[key] : localizer[key, arguments];
        return translated.ResourceNotFound ? fallback : translated.Value;
    }

    /// <summary>The display name of a BPMN wire type.</summary>
    public string ElementType(string wireType) => Keyed($"ElementType.{wireType}", TypeFallback(wireType));

    /// <summary>The display name of a type-switch option.</summary>
    public string TypeOption(BpmnTypeOption option)
    {
        var type = BpmnElementInspector.TypeLabel(option.Type);
        if (BpmnElementInspector.CategoryOf(option.Type) != BpmnElementCategory.Event)
            return option.Label == type
                ? Keyed($"ElementType.{BpmnElementInspector.WireNameOf(option.Type)}", option.Label)
                : option.Label;
        var trigger = BpmnElementInspector.TriggerLabel(option.EventDefinition);
        if (option.Label != $"{trigger} {type}") return option.Label;
        return Text("TypeOption.Event", Trigger(option.EventDefinition), ElementType(BpmnElementInspector.WireNameOf(option.Type)));
    }

    /// <summary>The display name of an event trigger.</summary>
    public string Trigger(BpmnEventDefinitionType trigger) =>
        Keyed($"EventTrigger.{trigger}", BpmnElementInspector.TriggerLabel(trigger));

    /// <summary>The localized caption of a built-in context-menu command, read from <c>ContextMenu.Command.{command}</c>.</summary>
    public string MenuCommand(BpmnMenuCommand command) => Text($"ContextMenu.Command.{command}");

    /// <summary>The localized caption of a built-in contextual action, read from <c>ContextualActions.*</c>.</summary>
    public string ContextualAction(BpmnActionKind kind, BpmnFlowNodeType type = BpmnFlowNodeType.Task) =>
        Text(kind == BpmnActionKind.Append ? $"ContextualActions.Append.{type}" : $"ContextualActions.{kind}");

    /// <summary>The caption of a built-in context-menu command, retaining host captions.</summary>
    public string MenuCommand(BpmnMenuEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return BuiltIn($"ContextMenu.Command.{entry.Command}", entry.Label);
    }

    /// <summary>The caption of a contextual action, retaining host captions.</summary>
    public string ContextualAction(BpmnContextualAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var key = action.Kind == BpmnActionKind.Append
            ? $"ContextualActions.Append.{action.Type}"
            : $"ContextualActions.{action.Kind}";
        return BuiltIn(key, action.Label);
    }

    /// <summary>
    /// Localizes the built-in captions of an element snapshot: property and choice labels, extension groups and
    /// type-switch options. Names, values, identifiers, document labels and template or host captions are kept.
    /// </summary>
    public BpmnElementSnapshot Snapshot(BpmnElementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return snapshot with
        {
            Properties = [.. snapshot.Properties.Select(Descriptor)],
            ExtensionProperties = [.. snapshot.ExtensionProperties.Select(Descriptor)],
            ExtensionGroups = [.. snapshot.ExtensionGroups.Select(Group)],
            SwitchableTypes = [.. snapshot.SwitchableTypes.Select(option => option with { Label = TypeOption(option) })],
        };
    }

    /// <summary>The caption and choice labels of a standard property descriptor; its name and values are kept.</summary>
    public BpmnPropertyDescriptor Descriptor(BpmnPropertyDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        return descriptor with
        {
            Label = PropertyLabel(descriptor.Name, descriptor.Label),
            Choices = [.. descriptor.Choices.Select(choice => choice with { Label = ChoiceLabel(descriptor.Name, choice) })],
        };
    }

    /// <summary>The name of a built-in icon, retaining host icon overrides.</summary>
    public string IconLabel(BpmnIcon icon) => BuiltIn($"Icon.{icon.IconId}", icon.AccessibilityLabel);

    /// <summary>The caption of a standard property, retaining host descriptors and choices.</summary>
    public BpmnPropertyEntry Property(BpmnPropertyEntry entry)
    {
        if (entry.GroupId?.StartsWith("template:", StringComparison.Ordinal) == true) return entry;
        var label = PropertyLabel(entry.Id, entry.Label);
        return entry with
        {
            Label = label,
            Choices = entry.Choices.Select(choice => choice with
            {
                Label = ChoiceLabel(entry.Id, choice),
            }).ToArray(),
        };
    }

    /// <summary>Localizes built-in property groups and schema fields, preserving template and host labels.</summary>
    public BpmnPropertyGroup Group(BpmnPropertyGroup group)
    {
        if (!group.HasBuiltInLabels || group.Id.StartsWith("template:", StringComparison.Ordinal)) return group;
        var schema = group.Namespace is "http://camunda.org/schema/zeebe/1.0" or "http://camunda.org/schema/1.0/bpmn";
        var generic = group.Id.StartsWith("schema:", StringComparison.Ordinal);
        var prefix = generic ? $"Schema.Generic.{group.Id[7..].Split(':')[0]}:"
            : group.Id.StartsWith("camunda:", StringComparison.Ordinal) ? "Schema.camunda:" : "Schema.";
        return group with
        {
            Label = group.Label is null ? null : BuiltIn(generic ? $"Schema.Generic.{group.Id[7..]}"
                : schema ? SchemaKey(group.Id, prefix) : $"PropertyGroup.{group.Id}", group.Label),
            Entries = group.Entries.Select(entry => schema ? entry with
            {
                Label = entry.Id.Contains(":structured:", StringComparison.Ordinal)
                    && entry.Label.StartsWith("Structured value (preserved): ", StringComparison.Ordinal)
                    ? BuiltIn("Schema.Structured", entry.Label, entry.Label["Structured value (preserved): ".Length..])
                    : BuiltIn(SchemaKey(entry.Id, prefix), entry.Label),
                Columns = entry.Columns?.Select(column => column with
                {
                    Label = BuiltIn(prefix + column.Name, column.Label),
                }).ToArray(),
                Choices = entry.Choices.Select(choice => choice with { Label = ChoiceLabel(entry.Id, choice) }).ToArray(),
            } : Property(entry)).ToArray(),
        };
    }

    /// <summary>The caption of a stencil group, preserving template categories.</summary>
    public string StencilGroup(BpmnStencilGroup group) =>
        group.Entries.Any(entry => entry.Template is not null)
            ? group.Entries.All(entry => entry.Template?.Category is null)
                ? Text("StencilGroup.OtherTemplates") : group.Title
            : Keyed($"StencilGroup.{group.Title}", group.Title);

    /// <summary>The caption of a built-in creation tool, preserving template names.</summary>
    public string StencilEntry(BpmnStencilEntry entry)
    {
        if (entry.Template is not null || entry.HasCustomLabel) return entry.Label;
        var key = entry.Kind switch
        {
            BpmnStencilKind.PoolOrLane => "PoolOrLane",
            BpmnStencilKind.IntermediateBoundaryEvent => "IntermediateBoundaryEvent",
            BpmnStencilKind.Artifact => entry.Artifact.ToString(),
            BpmnStencilKind.Data => entry.Data.ToString(),
            _ => entry.Type.ToString(),
        };
        return Keyed($"StencilEntry.{key}", entry.Label);
    }

    /// <summary>The localized accessible description of a graph node.</summary>
    public string AccessibleName(BpmnGraphNode node)
    {
        var type = Keyed($"AccessibleType.{node.TypeName}", BpmnGraphNode.Humanise(node.TypeName));
        var named = node.Label.Length == 0 ? type : Text("AccessibleTree.Named", type, node.Label);
        if (node.IsExpanded is { } expanded)
            return Text(expanded ? "AccessibleTree.Expanded" : "AccessibleTree.Collapsed", named);
        if (node.TypeName is "pool" or "lane") return Text("AccessibleTree.Container", named);
        if (node.TypeName is "textAnnotation" or "group" or "dataObjectReference" or "dataStoreReference")
            return node.Attached.Count == 0 ? Text("AccessibleTree.Unassociated", named)
                : Text("AccessibleTree.Associated", named, string.Join(", ", node.Attached.Select((attachment, index) =>
                    index < node.AttachedTypes.Count && node.AttachedTypes[index] is { } attachedType
                        ? Keyed($"AccessibleType.{attachedType}", attachment) : attachment)));
        var incoming = node.Incoming.Count == 0 ? Text("AccessibleTree.NoIncoming")
            : Text("AccessibleTree.Incoming", node.Incoming.Count);
        var outgoing = node.Outgoing.Count == 0 ? Text("AccessibleTree.NoOutgoing")
            : Text("AccessibleTree.Outgoing", node.Outgoing.Count);
        return Text("AccessibleTree.Connections", named, incoming, outgoing);
    }

    private string PropertyLabel(string name, string fallback)
    {
        var indexed = IndexedProperty.Match(name);
        if (indexed.Success)
            return BuiltIn($"Property.{indexed.Groups[1].Value}.{indexed.Groups[3].Value}", fallback,
                int.Parse(indexed.Groups[2].Value, CultureInfo.InvariantCulture) + 1);
        if (name.StartsWith(BpmnElementInspector.ExtensionPrefix, StringComparison.Ordinal))
            return name.EndsWith("#text", StringComparison.Ordinal) && fallback.EndsWith(" text", StringComparison.Ordinal)
                ? Text("Property.RawText", fallback[..^5]) : fallback;
        return BuiltIn($"Property.{name}", fallback);
    }

    private string ChoiceLabel(string name, BpmnPropertyChoice choice)
    {
        if (choice.LabelResourceKey is { } key) return BuiltIn(key, choice.Label);
        if (choice.Value.Length == 0) return BuiltIn("PropertyChoice.Empty", choice.Label);
        if (name == "eventDefinition" && Enum.TryParse<BpmnEventDefinitionType>(choice.Value, true, out var trigger))
            return Keyed($"EventTrigger.{trigger}", choice.Label);
        return name is "loopCharacteristics" or "multiInstance.behavior" or "associationDirection" or "processType"
            ? BuiltIn($"PropertyChoice.{name}.{choice.Value}", choice.Label) : choice.Label;
    }

    private static string SchemaKey(string id, string prefix)
    {
        var name = id.StartsWith("camunda:", StringComparison.Ordinal) ? id[8..] : id;
        if (name.StartsWith("inputs-", StringComparison.Ordinal)) name = "inputs";
        if (name.StartsWith("outputs-", StringComparison.Ordinal)) name = "outputs";
        if (name.Contains("-fields-", StringComparison.Ordinal)) name = "fields";
        return prefix + name;
    }

    /// <summary>Reads a built-in caption by its resource key, falling back when the key is unknown.</summary>
    private string Keyed(string key, string fallback)
    {
        var value = localizer[key];
        if (!value.ResourceNotFound) return value.Value;
        return English.GetString(key, CultureInfo.InvariantCulture) ?? fallback;
    }

    private string Text(string key, params object[] arguments)
    {
        var value = arguments.Length == 0 ? localizer[key] : localizer[key, arguments];
        return value.ResourceNotFound
            ? string.Format(CultureInfo.CurrentCulture, English.GetString(key, CultureInfo.InvariantCulture) ?? key, arguments)
            : value.Value;
    }

    private static string TypeFallback(string wireType)
    {
        foreach (var type in Enum.GetValues<BpmnFlowNodeType>())
            if (BpmnElementInspector.WireNameOf(type) == wireType) return BpmnElementInspector.TypeLabel(type);
        return wireType switch
        {
            "pool" or "participant" => "Pool",
            "lane" => "Lane",
            "process" => "Process",
            "collaboration" => "Collaboration",
            "sequenceFlow" => "Sequence Flow",
            "messageFlow" => "Message Flow",
            "association" => "Association",
            "dataInputAssociation" => "Data Input Association",
            "dataOutputAssociation" => "Data Output Association",
            "textAnnotation" => "Text Annotation",
            "group" => "Group",
            "dataObjectReference" => "Data Object Reference",
            "dataStoreReference" => "Data Store Reference",
            _ => wireType,
        };
    }
}
