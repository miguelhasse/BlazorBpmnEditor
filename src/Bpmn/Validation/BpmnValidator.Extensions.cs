using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Validation;

/// <summary>
/// The extension layer (specification section 6.1).
/// </summary>
/// <remarks>
/// <para>
/// Preserved content is not validated in the sense the other layers mean it — the whole point of
/// section 6.1 is that unknown vendor XML round-trips untouched whether the editor understands it
/// or not, so "this extension is wrong" is not a judgement this engine is entitled to make.
/// </para>
/// <para>
/// What it can judge is whether the content can be <em>identified</em>. Section 6.1.2 gives
/// extension content its identity as an expanded name — namespace URI plus local name — which is
/// what lets a registered adapter claim it and what stops two vendors' <c>properties</c> elements
/// from being confused for one another. Content with no namespace URI has no such identity. It
/// still round-trips, which is why this is a warning rather than an error, but nothing can be
/// built on top of it.
/// </para>
/// </remarks>
public static partial class BpmnValidator
{
    private static void CheckExtensions(DocumentIndex index, List<BpmnDiagnostic> found)
    {
        foreach (var (elementId, extras) in Extended(index.Document))
        {
            // Depth-first, because the unnamespaced child of a properly namespaced extension is the
            // realistic case: a vendor declares its prefix on the outer element and then writes the
            // inner ones without it.
            foreach (var anonymous in Anonymous(extras.Extensions))
            {
                found.Add(BpmnDiagnostic.FromDetail(
                    BpmnRules.EXT001,
                    elementId,
                    detail: BpmnDiagnosticText.Create("EXT-001.Detail", "<{0}> is in no namespace.", anonymous.LocalName)));
            }
        }

        /*
         * Quarantine, not repair. Section 6.1 requires preserved content to round-trip untouched
         * and section 5.5 requires unsafe external content to be quarantined, so the content stays
         * exactly as it was read and the document is reported non-exportable instead. An editor
         * that silently rewrote what it did not understand would be worse than one that refuses.
         */
        foreach (var unsafeContent in BpmnExtensionSafety.Scan(index.Document))
        {
            found.Add(BpmnDiagnostic.FromDetail(
                BpmnRules.EXT010,
                unsafeContent.ElementId,
                detail: unsafeContent.LocalizableDetail
                ?? BpmnDiagnosticText.Create("EXT-010.Detail", "{0}: {1}.", unsafeContent.Path, unsafeContent.Reason)));
        }
    }

    /// <summary>Extension content, at any depth, that carries no namespace URI.</summary>
    private static IEnumerable<BpmnExtensionElement> Anonymous(IReadOnlyList<BpmnExtensionElement> extensions)
    {
        foreach (var extension in extensions)
        {
            if (extension.Namespace.Length == 0)
            {
                yield return extension;
            }

            foreach (var nested in Anonymous(extension.Children))
            {
                yield return nested;
            }
        }
    }

    /// <summary>
    /// Every element in the document that preserves something, with the id to report it against.
    /// </summary>
    /// <remarks>
    /// The model deliberately has no common base for this: <c>Extras</c> is declared on each record
    /// that can carry it rather than inherited, so that a type which cannot carry extensions cannot
    /// accidentally appear to. The cost is this enumeration, and the benefit is that adding a
    /// collection to <see cref="BpmnDocument"/> without adding it here is a visible omission rather
    /// than a silent one.
    /// </remarks>
    internal static IEnumerable<(string Id, BpmnElementExtras Extras)> Extended(BpmnDocument document)
    {
        foreach (var node in document.FlowNodes)
        {
            yield return (node.Id, node.Extras);
            if (node.MultiInstanceLoop is { } loop) yield return (node.Id, loop.Extras);
            foreach (var definition in node.EventDefinitions) yield return (definition.Id ?? node.Id, definition.Extras);
        }
        foreach (var message in document.Catalog.Messages) yield return (message.Id, message.Extras);
        foreach (var signal in document.Catalog.Signals) yield return (signal.Id, signal.Extras);
        foreach (var error in document.Catalog.Errors) yield return (error.Id, error.Extras);
        foreach (var escalation in document.Catalog.Escalations) yield return (escalation.Id, escalation.Extras);
        foreach (var flow in document.SequenceFlows) yield return (flow.Id, flow.Extras);
        foreach (var flow in document.MessageFlows) yield return (flow.Id, flow.Extras);
        foreach (var association in document.Associations) yield return (association.Id, association.Extras);
        foreach (var association in document.DataAssociations) yield return (association.Id, association.Extras);
        foreach (var annotation in document.TextAnnotations) yield return (annotation.Id, annotation.Extras);
        foreach (var group in document.Groups) yield return (group.Id, group.Extras);
        foreach (var reference in document.DataObjectReferences) yield return (reference.Id, reference.Extras);
        foreach (var reference in document.DataStoreReferences) yield return (reference.Id, reference.Extras);
        foreach (var data in document.DataObjects) yield return (data.Id, data.Extras);
        foreach (var process in document.Processes) yield return (process.Id, process.Extras);
        foreach (var participant in document.Collaboration?.Participants ?? []) yield return (participant.Id, participant.Extras);
        if (document.Collaboration is { } collaboration) yield return (collaboration.Id, collaboration.Extras);
        foreach (var lane in document.Lanes) yield return (lane.Id, lane.Extras);

        // DI carries its own extension content (section 10.5), and a diagram-level extension with
        // no namespace is exactly as unidentifiable as a semantic one.
        foreach (var diagram in document.Diagrams)
        {
            foreach (var shape in diagram.Plane.Shapes) yield return (shape.BpmnElement ?? shape.Id, shape.Extras);
            foreach (var edge in diagram.Plane.Edges) yield return (edge.BpmnElement ?? edge.Id, edge.Extras);
        }
    }
}
