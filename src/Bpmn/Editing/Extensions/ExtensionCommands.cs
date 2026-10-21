using System.Xml;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Sets an extension attribute or text value addressed by an expanded-name path.</summary>
/// <param name="ElementId">Element carrying extension data.</param>
/// <param name="Path">Expanded-name extension path.</param>
/// <param name="Value">New value, or null/empty to remove it.</param>
public sealed record SetExtensionValueCommand(string ElementId, string Path, string? Value) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Edit extension value";
}

/// <summary>Sets a vendor extension value nested beneath a multi-instance loop.</summary>
/// <param name="ElementId">Flow node carrying the loop.</param>
/// <param name="Path">Expanded-name extension path within the loop.</param>
/// <param name="Value">New value, or null/empty to remove it.</param>
public sealed record SetLoopExtensionValueCommand(string ElementId, string Path, string? Value) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Edit extension value";
}

/// <summary>Replaces rows in an extension collection while retaining unknown item content.</summary>
/// <param name="ElementId">Element carrying extension data.</param>
/// <param name="ContainerPath">Expanded-name path to the collection container.</param>
/// <param name="ItemNamespace">Item namespace URI.</param>
/// <param name="ItemLocalName">Item local name.</param>
/// <param name="Rows">Attribute maps using the identity and legacy positional semantics of <see cref="BpmnExtensionNavigator.ReplaceCollection(Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.BpmnElementExtras, BpmnExtensionPath, string, string, IReadOnlyList{IReadOnlyDictionary{string, string}})"/>.</param>
public sealed record ReplaceExtensionCollectionCommand(
    string ElementId,
    string ContainerPath,
    string ItemNamespace,
    string ItemLocalName,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Edit extension collection";
}

/// <summary>Edits simple Camunda collection rows. Structured rows remain read-only and are retained verbatim.
/// Rows may carry <c>$index</c> identifying the original matching item, including structured items in the count.
/// Without any indexed rows, legacy positional reuse applies to the editable items.</summary>
public sealed record ReplaceCamundaCollectionCommand(string ElementId, string? ContainerPath, string ItemLocalName,
    IReadOnlyList<IReadOnlyDictionary<string, string?>> Rows) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Edit Camunda collection";
}

/// <summary>Adds an extension element at the root or beneath an addressed extension parent.</summary>
/// <param name="ElementId">Element carrying extension data.</param>
/// <param name="ParentPath">Parent path, or null for the root extension container.</param>
/// <param name="Element">Extension element to add.</param>
public sealed record AddExtensionElementCommand(string ElementId, string? ParentPath, BpmnExtensionElement Element) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Add extension element";
}

/// <summary>Removes an extension element, attribute, or text node by expanded-name path.</summary>
/// <param name="ElementId">Element carrying extension data.</param>
/// <param name="Path">Expanded-name extension path to remove.</param>
public sealed record RemoveExtensionElementCommand(string ElementId, string Path) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Remove extension element";
}

/// <summary>Sets or removes a namespace-qualified attribute on an element.</summary>
/// <param name="ElementId">Element carrying extension data.</param>
/// <param name="NamespaceUri">Foreign attribute namespace URI.</param>
/// <param name="LocalName">Foreign attribute local name.</param>
/// <param name="Value">New attribute value, or null to remove it.</param>
public sealed record SetForeignAttributeCommand(string ElementId, string NamespaceUri, string LocalName, string? Value) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Edit extension attribute";
}

/// <summary>Applies extension-specific commands to the immutable BPMN model.</summary>
public static partial class BpmnDocumentEditor
{
    private const string ExtensionElementIdError = "EDIT-100";
    private const string ExtensionExtrasError = "EDIT-101";
    private const string ExtensionPathError = "EDIT-102";
    private const string ExtensionUnsafeError = "EDIT-103";

    private static BpmnEditResult ApplyExtensionValue(BpmnDocument document, SetExtensionValueCommand command, EditingOptions options)
    {
        if (!TryGetCarrier(document, command.ElementId, out var element, out var extras)) return NoCarrier(document, command.ElementId);
        if (!BpmnExtensionPath.TryParse(command.Path, out var path)) return InvalidPath(document, command.ElementId, command.Path);
        var updatedExtras = BpmnExtensionNavigator.Write(extras, path!, command.Value, WriteOptions(options));
        return ApplyExtras(document, element!, extras, updatedExtras, command.ElementId);
    }

    private static BpmnEditResult ApplyLoopExtensionValue(BpmnDocument document, SetLoopExtensionValueCommand command, EditingOptions options)
    {
        if (document.FindFlowNode(command.ElementId) is not { MultiInstanceLoop: { } loop } node)
            return NoCarrier(document, command.ElementId);
        if (!BpmnExtensionPath.TryParse(command.Path, out var path)) return InvalidPath(document, command.ElementId, command.Path);
        var updatedExtras = BpmnExtensionNavigator.Write(loop.Extras, path!, command.Value, WriteOptions(options));
        if (SameExtras(loop.Extras, updatedExtras)) return BpmnEditResult.Applied(document);
        var replacement = node with { MultiInstanceLoop = loop with { Extras = updatedExtras } };
        var edited = WithElement(document, replacement);
        var previousUnsafe = BpmnExtensionSafety.Scan(document)
            .Where(item => item.ElementId == command.ElementId)
            .GroupBy(item => (item.Path, item.Reason))
            .ToDictionary(group => group.Key, group => group.Count());
        var seenUnsafe = new Dictionary<(string Path, string Reason), int>();
        var introduced = BpmnExtensionSafety.Scan(edited).FirstOrDefault(item =>
        {
            if (item.ElementId != command.ElementId) return false;
            var key = (item.Path, item.Reason);
            seenUnsafe.TryGetValue(key, out var count);
            seenUnsafe[key] = ++count;
            return count > previousUnsafe.GetValueOrDefault(key);
        });
        return introduced is null
            ? BpmnEditResult.Applied(edited)
            : RejectExtension(document, command.ElementId, ExtensionUnsafeError,
                $"Extension edit refused because it introduces unsafe content: {introduced.Reason}.");
    }

    private static BpmnEditResult ApplyExtensionCollection(BpmnDocument document, ReplaceExtensionCollectionCommand command, EditingOptions options)
    {
        if (!TryGetCarrier(document, command.ElementId, out var element, out var extras)) return NoCarrier(document, command.ElementId);
        if (!BpmnExtensionPath.TryParse(command.ContainerPath, out var path)
            || path!.AttributeName is not null || path.IsText) return InvalidPath(document, command.ElementId, command.ContainerPath);
        if (command.Rows is null || string.IsNullOrEmpty(command.ItemNamespace) || !ValidLocalName(command.ItemLocalName))
            return RejectExtension(document, command.ElementId, ExtensionPathError, "Extension collection specification is invalid.");
        var updatedExtras = BpmnExtensionNavigator.ReplaceCollection(
            extras, path, command.ItemNamespace, command.ItemLocalName, command.Rows, WriteOptions(options));
        return ApplyExtras(document, element!, extras, updatedExtras, command.ElementId);
    }

    private static BpmnEditResult ApplyCamundaCollection(BpmnDocument document, ReplaceCamundaCollectionCommand command, EditingOptions options)
    {
        if (!TryGetCarrier(document, command.ElementId, out var element, out var extras)) return NoCarrier(document, command.ElementId);
        const string uri = Camunda7TemplateBindingHandler.Uri;
        var root = command.ContainerPath is null;
        if (command.Rows is null || !ValidLocalName(command.ItemLocalName)) return InvalidPath(document, command.ElementId, command.ContainerPath ?? "");
        BpmnExtensionPath? path;
        if (root) path = new([new("urn:editor:internal", "collection")]);
        else if (!BpmnExtensionPath.TryParse(command.ContainerPath!, out path) || path!.AttributeName is not null || path.IsText)
            return InvalidPath(document, command.ElementId, command.ContainerPath!);
        var working = root ? extras with { Extensions = [new("urn:editor:internal", "collection", new Dictionary<string, string>(), extras.Extensions)] } : extras;
        var parent = BpmnExtensionNavigator.Resolve(working.Extensions, path.Steps);
        var complex = parent?.Children.Where(e => e.Namespace == uri && e.LocalName == command.ItemLocalName && e.Children.Count > 0 && command.ItemLocalName != "executionListener").ToArray() ?? [];
        if (parent is not null)
            working = BpmnExtensionNavigator.UpdateElement(working, path, e => e with { Children = e.Children.Except(complex).ToArray() });
        var rows = command.Rows;
        if (complex.Length > 0 && rows.Any(row => row.ContainsKey(BpmnExtensionNavigator.CollectionRowIndexKey)))
        {
            var allItems = parent!.Children.Where(e => e.Namespace == uri && e.LocalName == command.ItemLocalName).ToArray();
            var editableIndices = allItems.Select((item, index) => (item, index)).Where(x => !complex.Contains(x.item))
                .Select((x, index) => (Original: x.index, Editable: index)).ToDictionary(x => x.Original, x => x.Editable);
            rows = rows.Select(row =>
            {
                if (!row.ContainsKey(BpmnExtensionNavigator.CollectionRowIndexKey)) return row;
                var mapped = editableIndices.GetValueOrDefault(BpmnExtensionNavigator.CollectionRowIndex(row), -1);
                return (IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>(row)
                {
                    [BpmnExtensionNavigator.CollectionRowIndexKey] = mapped.ToString(System.Globalization.CultureInfo.InvariantCulture)
                };
            }).ToArray();
        }
        var updated = BpmnExtensionNavigator.ReplaceCollection(working, path, uri, command.ItemLocalName, rows, WriteOptions(options));
        if (complex.Length > 0)
        {
            // Keep structured items at their original positions; editable rows never replace their payload.
            updated = BpmnExtensionNavigator.UpdateElement(updated, path, e =>
            {
                var children = e.Children.ToList();
                foreach (var item in complex)
                    children.Insert(Math.Min(parent!.Children.ToList().IndexOf(item), children.Count), item);
                return e with { Children = children };
            });
        }
        if (root) updated = extras with { Extensions = BpmnExtensionNavigator.Resolve(updated.Extensions, path.Steps)?.Children ?? [] };
        return ApplyExtras(document, element!, extras, updated, command.ElementId);
    }

    private static BpmnEditResult ApplyAddExtension(BpmnDocument document, AddExtensionElementCommand command, EditingOptions options)
    {
        if (!TryGetCarrier(document, command.ElementId, out var element, out var extras)) return NoCarrier(document, command.ElementId);
        if (command.Element is null || string.IsNullOrEmpty(command.Element.Namespace) || !ValidLocalName(command.Element.LocalName))
            return RejectExtension(document, command.ElementId, ExtensionPathError, "Extension element name is invalid.");
        BpmnExtensionPath? parentPath = null;
        if (command.ParentPath is not null && !BpmnExtensionPath.TryParse(command.ParentPath, out parentPath))
            return InvalidPath(document, command.ElementId, command.ParentPath);
        BpmnElementExtras updatedExtras;
        try
        {
            updatedExtras = BpmnExtensionNavigator.AddChild(extras, parentPath, command.Element, WriteOptions(options));
        }
        catch (InvalidOperationException)
        {
            return InvalidPath(document, command.ElementId, command.ParentPath ?? string.Empty);
        }
        return ApplyExtras(document, element!, extras, updatedExtras, command.ElementId);
    }

    private static BpmnEditResult ApplyRemoveExtension(BpmnDocument document, RemoveExtensionElementCommand command)
    {
        if (!TryGetCarrier(document, command.ElementId, out var element, out var extras)) return NoCarrier(document, command.ElementId);
        if (!BpmnExtensionPath.TryParse(command.Path, out var path)) return InvalidPath(document, command.ElementId, command.Path);
        var updatedExtras = BpmnExtensionNavigator.Remove(extras, path!);
        return ApplyExtras(document, element!, extras, updatedExtras, command.ElementId);
    }

    private static BpmnEditResult ApplyForeignAttribute(BpmnDocument document, SetForeignAttributeCommand command)
    {
        if (!TryGetCarrier(document, command.ElementId, out var element, out var extras)) return NoCarrier(document, command.ElementId);
        if (string.IsNullOrEmpty(command.NamespaceUri) || !ValidLocalName(command.LocalName))
            return RejectExtension(document, command.ElementId, ExtensionPathError, "Foreign attribute name is invalid.");
        var key = $"{{{command.NamespaceUri}}}{command.LocalName}";
        if (extras.ForeignAttributes.TryGetValue(key, out var oldValue) && oldValue == command.Value)
            return BpmnEditResult.Applied(document);
        if (command.Value is null && !extras.ForeignAttributes.ContainsKey(key)) return BpmnEditResult.Applied(document);
        var attributes = new Dictionary<string, string>(extras.ForeignAttributes, StringComparer.Ordinal);
        if (command.Value is null) attributes.Remove(key);
        else attributes[key] = command.Value;
        var updatedExtras = extras with { ForeignAttributes = attributes };
        return ApplyExtras(document, element!, extras, updatedExtras, command.ElementId);
    }

    private static BpmnEditResult ApplyExtras(
        BpmnDocument document,
        object element,
        BpmnElementExtras original,
        BpmnElementExtras updated,
        string elementId)
    {
        if (SameExtras(original, updated)) return BpmnEditResult.Applied(document);
        var replacement = WithExtras(element, _ => updated);
        var edited = element is BpmnEventDefinition definition
            ? UpdateEventDefinition(document, definition, (BpmnEventDefinition)replacement)
            : WithElement(document, replacement);
        var previousUnsafe = BpmnExtensionSafety.Scan(document)
            .Where(item => item.ElementId == elementId)
            .GroupBy(item => (item.Path, item.Reason))
            .ToDictionary(group => group.Key, group => group.Count());
        var seenUnsafe = new Dictionary<(string Path, string Reason), int>();
        var introduced = BpmnExtensionSafety.Scan(edited).FirstOrDefault(item =>
        {
            if (item.ElementId != elementId) return false;
            var key = (item.Path, item.Reason);
            seenUnsafe.TryGetValue(key, out var count);
            seenUnsafe[key] = ++count;
            return count > previousUnsafe.GetValueOrDefault(key);
        });
        if (introduced is not null)
        {
            return RejectExtension(document, elementId, ExtensionUnsafeError,
                $"Extension edit refused because it introduces unsafe content: {introduced.Reason}.");
        }
        return BpmnEditResult.Applied(edited);
    }

    private static bool TryGetCarrier(BpmnDocument document, string elementId, out object? element, out BpmnElementExtras extras)
    {
        element = document.Find(elementId);
        element ??= document.FlowNodes
                .SelectMany(node => node.EventDefinitions)
                .FirstOrDefault(definition => definition.Id == elementId);
        extras = element switch
        {
            BpmnFlowNode node => node.Extras,
            BpmnEventDefinition definition => definition.Extras,
            BpmnSequenceFlow flow => flow.Extras,
            BpmnMessageFlow flow => flow.Extras,
            BpmnAssociation association => association.Extras,
            BpmnTextAnnotation annotation => annotation.Extras,
            BpmnGroup group => group.Extras,
            BpmnDataObjectReference reference => reference.Extras,
            BpmnDataStoreReference reference => reference.Extras,
            BpmnDataObject data => data.Extras,
            BpmnParticipant participant => participant.Extras,
            BpmnLane lane => lane.Extras,
            BpmnProcess process => process.Extras,
            BpmnCollaboration collaboration => collaboration.Extras,
            BpmnMessage message => message.Extras,
            BpmnSignal signal => signal.Extras,
            BpmnError error => error.Extras,
            BpmnEscalation escalation => escalation.Extras,
            _ => BpmnElementExtras.None,
        };
        return element is not null && element switch
        {
            BpmnFlowNode or BpmnEventDefinition or BpmnSequenceFlow or BpmnMessageFlow or BpmnAssociation or BpmnTextAnnotation or BpmnGroup
                or BpmnDataObjectReference or BpmnDataStoreReference or BpmnDataObject or BpmnParticipant or BpmnLane
                or BpmnProcess or BpmnCollaboration or BpmnMessage or BpmnSignal or BpmnError or BpmnEscalation => true,
            _ => false,
        };
    }

    private static BpmnDocument UpdateEventDefinition(
        BpmnDocument document,
        BpmnEventDefinition oldDefinition,
        BpmnEventDefinition newDefinition)
    {
        var owner = document.FlowNodes.First(node => node.EventDefinitions.Any(definition => ReferenceEquals(definition, oldDefinition)));
        var replacement = owner with
        {
            EventDefinitions = [.. owner.EventDefinitions.Select(definition => ReferenceEquals(definition, oldDefinition) ? newDefinition : definition)],
        };
        return WithElement(document, replacement);
    }

    private static bool SameExtras(BpmnElementExtras left, BpmnElementExtras right) =>
        left.Documentation == right.Documentation
        && left.ForeignAttributes.Count == right.ForeignAttributes.Count
        && left.ForeignAttributes.All(pair => right.ForeignAttributes.TryGetValue(pair.Key, out var value) && value == pair.Value)
        && left.Extensions.Count == right.Extensions.Count
        && !left.Extensions.Where((item, index) => !item.Equals(right.Extensions[index])).Any();

    private static BpmnExtensionWriteOptions WriteOptions(EditingOptions options) =>
        new(ChildOrder: options.ExtensionChildOrder);

    private static bool ValidLocalName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        try { XmlConvert.VerifyNCName(name); return true; }
        catch (XmlException) { return false; }
    }

    private static BpmnEditResult NoCarrier(BpmnDocument document, string elementId) =>
        RejectExtension(document, elementId, document.Find(elementId) is null ? ExtensionElementIdError : ExtensionExtrasError,
            document.Find(elementId) is null
                ? $"There is no element with id '{elementId}'."
                : $"Element '{elementId}' does not carry extension content.");

    private static BpmnEditResult InvalidPath(BpmnDocument document, string elementId, string path) =>
        RejectExtension(document, elementId, ExtensionPathError, $"Extension path '{path}' is malformed or does not resolve.");

    private static BpmnEditResult RejectExtension(BpmnDocument document, string elementId, string code, string message) =>
        BpmnEditResult.Reject(document, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, code, message, elementId));
}
