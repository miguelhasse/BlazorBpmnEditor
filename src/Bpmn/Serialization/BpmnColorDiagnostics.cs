using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

internal static class BpmnColorDiagnostics
{
    internal static IEnumerable<BpmnImportDiagnostic> Find(BpmnDocument document)
    {
        foreach (var diagram in document.Diagrams)
        {
            foreach (var finding in Check(diagram.Extras, "BPMNDiagram", diagram.Id)) yield return finding;
            foreach (var finding in Check(diagram.Plane.Extras, "BPMNPlane", diagram.Plane.Id)) yield return finding;
            foreach (var shape in diagram.Plane.Shapes)
            {
                foreach (var finding in Check(shape.Extras, "BPMNShape", shape.BpmnElement)) yield return finding;
                if (shape.Label is { } label)
                    foreach (var finding in Check(label.Extras, "BPMNLabel", shape.BpmnElement)) yield return finding;
            }
            foreach (var edge in diagram.Plane.Edges)
            {
                foreach (var finding in Check(edge.Extras, "BPMNEdge", edge.BpmnElement)) yield return finding;
                if (edge.Label is { } label)
                    foreach (var finding in Check(label.Extras, "BPMNLabel", edge.BpmnElement)) yield return finding;
            }
        }
    }

    private static IEnumerable<BpmnImportDiagnostic> Check(BpmnElementExtras extras, string element, string id)
    {
        foreach (var (key, value) in extras.ForeignAttributes)
        {
            var namespaceName = key.StartsWith($"{{{BpmnDiColors.Namespace}}}", StringComparison.Ordinal)
                ? BpmnDiColors.Namespace
                : key.StartsWith($"{{{BpmnDiColors.BiocNamespace}}}", StringComparison.Ordinal)
                    ? BpmnDiColors.BiocNamespace : null;
            if (namespaceName is null) continue;
            var name = key[(namespaceName.Length + 2)..];
            if (BpmnDiColors.IsApplicable(element, namespaceName, name) && BpmnDiColors.IsValid(value)) continue;
            yield return new BpmnImportDiagnostic(BpmnImportSeverity.Degraded,
                $"Color attribute '{key}' is invalid or inapplicable and will not be rendered; its source value is preserved.", id);
        }
    }
}
