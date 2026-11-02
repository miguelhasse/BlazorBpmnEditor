using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

public sealed class ValidationIndexTests
{
    [Fact]
    public void AnnotationCountsPreserveSelfConnectionsDuplicatesAndDocumentOrder()
    {
        var document = new BpmnDocumentBuilder()
            .TextAnnotation("First", "First", new Bounds(0, 0, 100, 40))
            .TextAnnotation("Second", "Second", new Bounds(0, 100, 100, 40))
            .TextAnnotation("Self", "Self", new Bounds(0, 200, 100, 40))
            .Association("A", "First", "Second", [])
            .Association("B", "Second", "First", [])
            .Association("C", "Self", "Self", [])
            .Build();
        document = document with { TextAnnotations = [.. document.TextAnnotations, document.TextAnnotations[0]] };

        var diagnostics = BpmnValidator.Validate(document).Diagnostics.Where(diagnostic => diagnostic.RuleId == "AN-001");

        Assert.Equal(["First", "Second", "First"], diagnostics.Select(diagnostic => diagnostic.ElementId));
    }

    [Fact]
    public void ReachabilityDiagnosticsFollowContainerThenNodeOrder()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Sub", BpmnFlowNodeType.SubProcess, new Bounds(0, 0, 600, 200), isExpanded: true)
            .Node("OuterStart", BpmnFlowNodeType.StartEvent, new Bounds(0, 300, 36, 36))
            .Node("InnerOrphan", BpmnFlowNodeType.Task, new Bounds(100, 50, 100, 80), containerId: "Sub")
            .Node("OuterOrphan", BpmnFlowNodeType.Task, new Bounds(100, 300, 100, 80))
            .Node("InnerStart", BpmnFlowNodeType.StartEvent, new Bounds(40, 50, 36, 36), containerId: "Sub")
            .Build();

        var diagnostics = BpmnValidator.Validate(document).Diagnostics.Where(diagnostic => diagnostic.RuleId == "D009");

        Assert.Equal(["Sub", "OuterOrphan", "InnerOrphan"], diagnostics.Select(diagnostic => diagnostic.ElementId));
    }
}
