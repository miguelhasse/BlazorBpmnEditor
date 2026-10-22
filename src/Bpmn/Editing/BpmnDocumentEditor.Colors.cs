using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

public static partial class BpmnDocumentEditor
{
    private static BpmnEditResult ApplyColors(BpmnDocument document, SetColorsCommand command)
    {
        if (!Enum.IsDefined(command.Channel)
            || command.Color is not null && (!BpmnDiColors.IsValid(command.Color) || command.Channel == BpmnColorChannel.All))
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "COLOR-001",
                "A color must be a six-digit RGB hex value (#RRGGBB); all channels can only be reset."));

        var diagram = document.Diagrams.FirstOrDefault(item => item.Id == command.DiagramId);
        var ids = command.ElementIds.ToHashSet(StringComparer.Ordinal);
        if (diagram is null || ids.Count == 0
            || ids.Any(id => !diagram.Plane.Shapes.Any(shape => shape.BpmnElement == id)
                && !diagram.Plane.Edges.Any(edge => edge.BpmnElement == id)))
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "COLOR-002",
                "Every selected element must be drawn on the requested diagram."));

        if (command.Channel == BpmnColorChannel.Background
            && !diagram.Plane.Shapes.Any(shape => ids.Contains(shape.BpmnElement)))
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(BpmnEditSeverity.Rejected, "COLOR-003",
                "Only shapes have a background color."));

        var background = command.Channel is BpmnColorChannel.Background or BpmnColorChannel.All;
        var border = command.Channel is BpmnColorChannel.Border or BpmnColorChannel.All;
        var text = command.Channel is BpmnColorChannel.Text or BpmnColorChannel.All;

        BpmnLabel? Label(BpmnLabel? label)
        {
            if (!text || label is null && command.Color is null) return label;
            label ??= new BpmnLabel();
            return label with { Extras = BpmnDiColors.Set(label.Extras, "color", null, command.Color) };
        }

        var plane = diagram.Plane with
        {
            Shapes = diagram.Plane.Shapes.Select(shape =>
            {
                if (!ids.Contains(shape.BpmnElement)) return shape;
                var extras = shape.Extras;
                if (background) extras = BpmnDiColors.Set(extras, "background-color", "fill", command.Color);
                if (border) extras = BpmnDiColors.Set(extras, "border-color", "stroke", command.Color);
                return shape with { Extras = extras, Label = Label(shape.Label) };
            }).ToArray(),
            Edges = diagram.Plane.Edges.Select(edge => !ids.Contains(edge.BpmnElement) || !border && !text ? edge : edge with
            {
                Extras = border ? BpmnDiColors.Set(edge.Extras, "border-color", "stroke", command.Color) : edge.Extras,
                Label = Label(edge.Label),
            }).ToArray(),
        };
        if (plane.Shapes.SequenceEqual(diagram.Plane.Shapes) && plane.Edges.SequenceEqual(diagram.Plane.Edges))
            return new BpmnEditResult(document, false, []);

        return BpmnEditResult.Applied(document with
        {
            Diagrams = document.Diagrams.Select(item => item.Id == diagram.Id ? item with { Plane = plane } : item).ToArray(),
        });
    }
}
