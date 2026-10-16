using System.Xml.Linq;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>Finds retained XML content that canonical serialization cannot reproduce.</summary>
internal static class BpmnCanonicalLossAnalyzer
{
    internal static IReadOnlyList<BpmnExportDiagnostic> FindLosses(
        XDocument source,
        XDocument canonical,
        IReadOnlyList<BpmnImportDiagnostic> importLosses)
    {
        var diagnostics = new List<BpmnExportDiagnostic>();

        foreach (var loss in importLosses)
        {
            diagnostics.Add(new BpmnExportDiagnostic(
                BpmnExportSeverity.Dropped,
                $"The reader could not represent source content: {loss.Message}",
                loss.ElementId));
        }

        foreach (var node in source.Nodes().Where(node => node is XComment or XProcessingInstruction))
        {
            if (HasEquivalentNode(source, canonical, node)) continue;

            diagnostics.Add(new BpmnExportDiagnostic(
                BpmnExportSeverity.Dropped,
                $"Canonical output cannot preserve the source {node.NodeType}."));
        }

        if (source.Root is null || canonical.Root is null) return diagnostics;

        Inspect(source.Root, canonical.Root);
        return diagnostics;

        void Inspect(XElement sourceElement, XElement canonicalElement)
        {
            foreach (var attribute in sourceElement.Attributes()
                         .Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                if (canonicalElement.Attribute(attribute.Name) is not null) continue;

                diagnostics.Add(new BpmnExportDiagnostic(
                    BpmnExportSeverity.Dropped,
                    $"Canonical output cannot preserve source attribute '{attribute.Name}'.",
                    Id(sourceElement)));
            }

            var canonicalChildren = canonicalElement.Elements().ToList();
            foreach (var child in sourceElement.Elements())
            {
                if (IsRedundantFlowReference(child)) continue;

                var counterpart = FindCounterpart(child, canonicalChildren);
                if (counterpart is null)
                {
                    // A known model element missing from canonical output was explicitly removed
                    // from the model. Unowned source content has no such edit representation.
                    if (!BpmnXmlNames.IsOwnedElement(sourceElement, child))
                    {
                        diagnostics.Add(new BpmnExportDiagnostic(
                            BpmnExportSeverity.Dropped,
                            $"Canonical output cannot preserve unmodeled <{child.Name}> content.",
                            Id(child) ?? Id(sourceElement)));
                    }

                    continue;
                }

                Inspect(child, counterpart);
            }

            foreach (var node in sourceElement.Nodes().Where(node => node is XComment or XProcessingInstruction))
            {
                if (HasEquivalentNode(sourceElement, canonicalElement, node)) continue;

                diagnostics.Add(new BpmnExportDiagnostic(
                    BpmnExportSeverity.Dropped,
                    $"Canonical output cannot preserve the source {node.NodeType}.",
                    Id(sourceElement)));
            }
        }
    }

    private static XElement? FindCounterpart(XElement source, IReadOnlyList<XElement> canonicalSiblings)
    {
        var id = Id(source);
        if (id is not null)
        {
            return canonicalSiblings.FirstOrDefault(candidate =>
                candidate.Name == source.Name && Id(candidate) == id);
        }

        var ordinal = source.ElementsBeforeSelf(source.Name).Count();
        return canonicalSiblings.Where(candidate => candidate.Name == source.Name)
            .Skip(ordinal)
            .FirstOrDefault();
    }

    private static bool HasEquivalentNode(XContainer source, XContainer output, XNode sourceNode)
    {
        var sourceOrdinal = source.Nodes()
            .TakeWhile(node => !ReferenceEquals(node, sourceNode))
            .Count(node => AreEquivalent(node, sourceNode));
        var outputCount = output.Nodes().Count(node => AreEquivalent(node, sourceNode));
        return outputCount > sourceOrdinal;
    }

    private static bool AreEquivalent(XNode candidate, XNode source) =>
        (candidate, source) switch
        {
            (XComment left, XComment right) => left.Value == right.Value,
            (XProcessingInstruction left, XProcessingInstruction right) =>
                left.Target == right.Target && left.Data == right.Data,
            _ => false,
        };

    private static bool IsRedundantFlowReference(XElement element) =>
        element.Name.Namespace == BpmnXmlNames.Model
        && element.Name.LocalName is "incoming" or "outgoing";

    private static string? Id(XElement element) => element.Attribute("id")?.Value;
}
