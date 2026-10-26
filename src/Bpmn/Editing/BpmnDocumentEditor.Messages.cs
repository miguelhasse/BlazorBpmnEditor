using System.Xml.Linq;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Creates a root-level BPMN Message definition.</summary>
/// <param name="Id">The globally unique XML id for the Message.</param>
/// <param name="Name">The optional display name.</param>
public sealed record CreateMessageCommand(string Id, string? Name = null) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Create Message";
}

/// <summary>Changes the display name of a root-level BPMN Message.</summary>
/// <param name="ElementId">The Message definition's fixed id.</param>
/// <param name="Name">The new optional display name.</param>
public sealed record UpdateMessageNameCommand(string ElementId, string? Name) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Rename Message";
}

/// <summary>Deletes a Message definition only when no modeled or retained content uses it.</summary>
/// <param name="ElementId">The Message definition to delete.</param>
public sealed record DeleteMessageCommand(string ElementId) : BpmnEditCommand
{
    /// <inheritdoc />
    public override string Label => "Delete Message";
}

/// <summary>A usage of a Message and whether deleting it is currently safe.</summary>
/// <param name="Message">The inspected Message definition.</param>
/// <param name="Usages">Known modeled or retained references to the Message.</param>
/// <param name="CanDelete">Whether deletion is safe for the inspected document.</param>
/// <param name="Reason">The reason deletion is blocked, or <see langword="null"/> when eligible.</param>
public sealed record BpmnMessageInspection(
    BpmnMessage Message,
    IReadOnlyList<BpmnReferenceSite> Usages,
    bool CanDelete,
    string? Reason);

/// <summary>Inspects Message usages and safe-deletion eligibility in the authoritative document.</summary>
public static class BpmnMessageCatalog
{
    private const string ModelNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";

    /// <summary>Inspects every Message in the document, in catalog order.</summary>
    public static IReadOnlyList<BpmnMessageInspection> Inspect(BpmnDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return [.. document.Catalog.Messages.Select(message => Inspect(document, message.Id)!)];
    }

    /// <summary>Inspects one Message by id, or returns <see langword="null"/> when it does not exist.</summary>
    public static BpmnMessageInspection? Inspect(BpmnDocument document, string messageId)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(messageId);

        var message = document.Catalog.Messages.FirstOrDefault(candidate => candidate.Id == messageId);
        if (message is null) return null;

        if (document.Catalog.Messages.Count(candidate => candidate.Id == messageId) > 1
            || document.Find(messageId) is not BpmnMessage)
        {
            return new BpmnMessageInspection(
                message,
                [],
                false,
                $"The id '{messageId}' is not globally unique, so its references cannot be resolved safely.");
        }

        var sourceIdCount = document.Source?.Clone().Root?.DescendantsAndSelf()
            .Attributes().Count(attribute => attribute.Name.LocalName == "id" && attribute.Value == messageId);
        if (sourceIdCount > 1)
        {
            return new BpmnMessageInspection(
                message,
                [],
                false,
                $"The id '{messageId}' is duplicated in retained XML, so its references cannot be resolved safely.");
        }

        var usages = BpmnReferenceMap.UsagesOf(document, messageId).ToList();
        foreach (var site in BpmnReferenceMap.Sites(document).Where(site =>
            site.Attribute is "messageRef" or "message/ref" && site.TargetId != messageId))
        {
            var context = SourceElement(document, site.OwnerId);
            if (site.Attribute == "message/ref")
            {
                context = context?.Elements(BpmnXmlNames.Model + "messageEventDefinition")
                    .FirstOrDefault(element => (string?)element.Attribute("messageRef") == site.TargetId) ?? context;
            }
            if (QNameNamesMessage(document, site.TargetId, messageId, context))
                usages.Add(site with { TargetId = messageId });
        }
        AddOperationUsages(document, message, usages);

        var opaqueReason = FindOpaqueReference(document, message, usages);
        var distinctUsages = usages
            .DistinctBy(site => (site.OwnerId, site.Attribute, site.TargetId))
            .ToArray();

        if (distinctUsages.Length > 0)
        {
            var summary = string.Join(", ", distinctUsages.Select(site => $"{site.OwnerKind} '{site.OwnerId}' ({site.Attribute})"));
            return new BpmnMessageInspection(message, distinctUsages, false, $"Message is still referenced by {summary}.");
        }

        return new BpmnMessageInspection(message, distinctUsages, opaqueReason is null, opaqueReason);
    }

    private static void AddOperationUsages(
        BpmnDocument document,
        BpmnMessage message,
        List<BpmnReferenceSite> usages)
    {
        foreach (var contract in document.Catalog.Interfaces)
        {
            foreach (var operation in contract.Operations)
            {
                Add(operation.InMessageRef, "inMessageRef");
                Add(operation.OutMessageRef, "outMessageRef");

                void Add(string? reference, string attribute)
                {
                    if (reference is null || !QNameNamesMessage(document, reference, message.Id, SourceElement(document, operation.Id))) return;
                    usages.Add(new BpmnReferenceSite(operation.Id, "Interface Operation", attribute, message.Id));
                }
            }
        }
    }

    private static string? FindOpaqueReference(
        BpmnDocument document,
        BpmnMessage message,
        List<BpmnReferenceSite> usages)
    {
        var source = document.Source?.Clone();
        if (source?.Root is { } root)
        {
            foreach (var element in root.DescendantsAndSelf())
            {
                if (!IsUncertainOpaqueElement(element)) continue;
                var ownerId = (string?)element.Attribute("id") ?? element.Name.LocalName;
                return $"Retained opaque content on '{ownerId}' may contain a Message reference; deletion is blocked because its meaning cannot be verified.";
            }

            foreach (var attribute in root.DescendantsAndSelf().Attributes())
            {
                var owner = attribute.Parent!;
                var localName = attribute.Name.LocalName;

                if (attribute.Name.Namespace == XNamespace.None
                    && IsModeledMessageReferenceLocation(document, owner, localName)) continue;

                if (IsMessageReferenceName(localName) && ReferenceNamesMessage(document, owner, attribute.Value, message.Id))
                {
                    var ownerId = (string?)owner.Attribute("id") ?? owner.Name.LocalName;
                    usages.Add(new BpmnReferenceSite(ownerId, owner.Name.LocalName, localName, message.Id));
                    return $"A retained XML reference on '{ownerId}' may still name this Message.";
                }

                if (IsUncertainOpaqueReference(owner, localName))
                {
                    var ownerId = (string?)owner.Attribute("id") ?? owner.Name.LocalName;
                    return $"Retained opaque content on '{ownerId}' may contain a Message reference; deletion is blocked because its meaning cannot be verified.";
                }
            }

            foreach (var referenceElement in root.DescendantsAndSelf().Where(element =>
                element.Name.NamespaceName == ModelNamespace
                && element.Name.LocalName is "inMessageRef" or "outMessageRef"))
            {
                var owner = referenceElement.Parent;
                var ownerId = (string?)owner?.Attribute("id");
                if (ownerId is not null && document.Catalog.Interfaces
                    .SelectMany(contract => contract.Operations)
                    .Any(operation => operation.Id == ownerId))
                {
                    continue;
                }

                if (QNameNamesMessage(document, referenceElement.Value.Trim(), message.Id, referenceElement))
                {
                    usages.Add(new BpmnReferenceSite(
                        ownerId ?? referenceElement.Name.LocalName,
                        "Retained BPMN element",
                        referenceElement.Name.LocalName,
                        message.Id));
                    return $"A retained QName reference in '{ownerId ?? referenceElement.Name.LocalName}' may still name this Message.";
                }
            }
        }

        foreach (var preserved in document.Catalog.Preserved.Concat(
            document.Catalog.ExtensionDeclarations.SelectMany(declaration => declaration.Content)))
        {
            if (HasOpaqueMessageReference(document, preserved, message.Id))
            {
                return $"Retained opaque content '{preserved.LocalName}' may contain a Message reference; deletion is blocked because its meaning cannot be verified.";
            }
        }

        foreach (var extras in AllExtras(document))
        {
            foreach (var name in extras.ForeignAttributes.Keys)
            {
                var localName = name[(name.LastIndexOf('}') + 1)..];
                if (localName.Contains("message", StringComparison.OrdinalIgnoreCase)
                    || localName.Contains("correlation", StringComparison.OrdinalIgnoreCase))
                {
                    return $"Foreign attribute '{name}' may contain a Message reference; deletion is blocked because its meaning cannot be verified.";
                }
            }
            foreach (var extension in extras.Extensions)
            {
                if (HasOpaqueMessageReference(document, extension, message.Id))
                {
                    return $"Retained opaque extension '{extension.LocalName}' may contain a Message reference; deletion is blocked because its meaning cannot be verified.";
                }
            }
        }

        return null;
    }

    private static IEnumerable<BpmnElementExtras> AllExtras(BpmnDocument document)
    {
        yield return document.Definitions.Extras;
        foreach (var (_, extras) in BpmnValidator.Extended(document))
            yield return extras;

        foreach (var diagram in document.Diagrams)
        {
            yield return diagram.Extras;
            yield return diagram.Plane.Extras;

            foreach (var shape in diagram.Plane.Shapes)
            {
                if (shape.Label is { } label)
                    yield return label.Extras;
            }

            foreach (var edge in diagram.Plane.Edges)
            {
                if (edge.Label is { } label)
                    yield return label.Extras;
            }
        }
    }

    private static bool HasOpaqueMessageReference(BpmnDocument document, BpmnExtensionElement element, string messageId)
    {
        if (IsUncertainOpaqueElement(element))
            return true;

        foreach (var (name, value) in element.Attributes)
        {
            var localName = name[(name.LastIndexOf('}') + 1)..];

            if (IsMessageReferenceName(localName) && ReferenceNamesMessage(document, null, value, messageId))
                return true;

            if (IsUncertainOpaqueReference(element, localName))
                return true;
        }

        return element.Children.Any(child => HasOpaqueMessageReference(document, child, messageId));
    }

    private static bool IsModeledMessageReferenceLocation(
        BpmnDocument document,
        XElement element,
        string attributeName)
    {
        if (attributeName != "messageRef" || element.Name.NamespaceName != ModelNamespace) return false;

        if (element.Name.LocalName == "messageEventDefinition")
        {
            var eventOwner = element.Ancestors()
                .FirstOrDefault(ancestor => ancestor.Attribute("id") is not null
                    && document.Find((string)ancestor.Attribute("id")!) is BpmnFlowNode);
            return eventOwner is not null;
        }

        var id = (string?)element.Attribute("id");
        if (id is not null && document.Find(id) is BpmnFlowNode node)
        {
            return BpmnXmlNames.ElementNameFor(node.Type) == element.Name.LocalName;
        }

        if (id is not null && document.Find(id) is BpmnMessageFlow flow)
        {
            return element.Name.LocalName == "messageFlow";
        }

        return false;
    }

    private static bool IsMessageReferenceName(string localName) =>
        localName is "messageRef" or "inMessageRef" or "outMessageRef";

    private static bool IsUncertainOpaqueReference(XElement owner, string localName) =>
        (owner.Name.NamespaceName != ModelNamespace || !BpmnXmlNames.OwnedElements.Contains(owner.Name.LocalName))
        && (localName.Contains("message", StringComparison.OrdinalIgnoreCase)
            || localName.Contains("correlation", StringComparison.OrdinalIgnoreCase));

    private static bool IsUncertainOpaqueElement(XElement element) =>
        (element.Name.NamespaceName != ModelNamespace || !BpmnXmlNames.OwnedElements.Contains(element.Name.LocalName))
        && (element.Name.LocalName.Contains("message", StringComparison.OrdinalIgnoreCase)
            || element.Name.LocalName.Contains("correlation", StringComparison.OrdinalIgnoreCase));

    private static bool IsUncertainOpaqueReference(BpmnExtensionElement owner, string localName) =>
        (owner.Namespace != ModelNamespace || !BpmnXmlNames.OwnedElements.Contains(owner.LocalName))
        && (localName.Contains("message", StringComparison.OrdinalIgnoreCase)
            || localName.Contains("correlation", StringComparison.OrdinalIgnoreCase));

    private static bool IsUncertainOpaqueElement(BpmnExtensionElement element) =>
        (element.Namespace != ModelNamespace || !BpmnXmlNames.OwnedElements.Contains(element.LocalName))
        && (element.LocalName.Contains("message", StringComparison.OrdinalIgnoreCase)
            || element.LocalName.Contains("correlation", StringComparison.OrdinalIgnoreCase));

    private static bool ReferenceNamesMessage(BpmnDocument document, XElement? context, string value, string messageId) =>
        string.Equals(value.Trim(), messageId, StringComparison.Ordinal)
        || QNameNamesMessage(document, value.Trim(), messageId, context);

    private static bool QNameNamesMessage(BpmnDocument document, string value, string messageId, XElement? context)
    {
        var colon = value.IndexOf(':');

        if (colon < 0)
            return string.Equals(value, messageId, StringComparison.Ordinal);

        var prefix = value[..colon];
        var localName = value[(colon + 1)..];

        if (!string.Equals(localName, messageId, StringComparison.Ordinal))
            return false;

        string? namespaceUri = null;
        if (context is not null)
        {
            namespaceUri = context.GetNamespaceOfPrefix(prefix)?.NamespaceName;
        }

        if (namespaceUri is null)
        {
            document.Definitions.NamespaceDeclarations.TryGetValue(prefix, out namespaceUri);
        }

        return string.Equals(namespaceUri, document.Definitions.TargetNamespace, StringComparison.Ordinal);
    }

    private static XElement? SourceElement(BpmnDocument document, string elementId) =>
        document.Source?.Clone().Root?.DescendantsAndSelf()
            .FirstOrDefault(element => (string?)element.Attribute("id") == elementId);
}

public static partial class BpmnDocumentEditor
{
    private static BpmnEditResult ApplyCreateMessage(BpmnDocument document, CreateMessageCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Id) || !IsNCName(command.Id))
        {
            return MessageRejected(document, "MSG-001", $"'{command.Id}' is not a valid XML id for a Message.", command.Id);
        }

        var collision = CreationIdsProblem(document, [command.Id]);
        if (collision is null
            && document.Source?.Clone().Root?.DescendantsAndSelf()
                .Attributes().Any(attribute => attribute.Name.LocalName == "id" && attribute.Value == command.Id) == true)
        {
            collision = new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "MSG-002",
                $"The id '{command.Id}' is already used by retained document content.",
                command.Id);
        }
        if (collision is not null)
        {
            return BpmnEditResult.Reject(document, collision with { Code = "MSG-002" });
        }

        var message = new BpmnMessage(command.Id, command.Name);
        return BpmnEditResult.Applied(document with
        {
            Catalog = document.Catalog with { Messages = [.. document.Catalog.Messages, message] },
        }) with
        { CreatedIds = [command.Id] };
    }

    private static BpmnEditResult ApplyUpdateMessageName(BpmnDocument document, UpdateMessageNameCommand command)
    {
        var index = IndexOfMessage(document, command.ElementId);
        if (index < 0)
        {
            return MessageRejected(document, "MSG-003", $"No Message '{command.ElementId}' exists.", command.ElementId);
        }

        var messages = document.Catalog.Messages.ToArray();
        if (messages[index].Name == command.Name) return BpmnEditResult.Applied(document);

        messages[index] = messages[index] with { Name = command.Name };
        return BpmnEditResult.Applied(document with
        {
            Catalog = document.Catalog with { Messages = messages },
        });
    }

    private static BpmnEditResult ApplyDeleteMessage(BpmnDocument document, string messageId)
    {
        var inspection = BpmnMessageCatalog.Inspect(document, messageId);
        if (inspection is null)
        {
            return MessageRejected(document, "MSG-003", $"No Message '{messageId}' exists.", messageId);
        }

        if (!inspection.CanDelete)
        {
            return MessageRejected(document, "MSG-004", inspection.Reason ?? "This Message cannot be deleted safely.", messageId);
        }

        return BpmnEditResult.Applied(document with
        {
            Catalog = document.Catalog with
            {
                Messages = [.. document.Catalog.Messages.Where(message => message.Id != messageId)],
            },
        }) with
        { DeletedElementIds = new HashSet<string>([messageId], StringComparer.Ordinal) };
    }

    private static BpmnEditResult MessageRejected(BpmnDocument document, string code, string message, string? elementId) =>
        BpmnEditResult.Reject(document, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, code, message, elementId));

    private static int IndexOfMessage(BpmnDocument document, string elementId)
    {
        for (var index = 0; index < document.Catalog.Messages.Count; index++)
        {
            if (document.Catalog.Messages[index].Id == elementId) return index;
        }

        return -1;
    }
}
