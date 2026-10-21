using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class BpmnContextualActionsTests
{
    [Theory]
    [InlineData("Task")]
    [InlineData("End")]
    [InlineData("Data")]
    [InlineData("Store")]
    [InlineData("Note")]
    [InlineData("Pool")]
    [InlineData("Lane")]
    public void EditableShapeOffersAnnotationButReadOnlyDoesNot(string id)
    {
        var document = Document();
        var actions = BpmnContextualActionsBase.DefaultsFor(BpmnElementInspector.Describe(document, id));
        Assert.Single(actions, action => action.Kind == BpmnActionKind.AddTextAnnotation);
        Assert.Empty(BpmnContextualActionsBase.DefaultsFor(BpmnElementInspector.Describe(document, id, isReadOnly: true)));
        if (id == "End") Assert.DoesNotContain(actions, action => action.Kind == BpmnActionKind.Append);
    }

    [Fact]
    public void GroupAndUndrawnShapeDoNotOfferAnnotation()
    {
        var document = Document();
        Assert.Empty(BpmnContextualActionsBase.DefaultsFor(BpmnElementInspector.Describe(document, "Group")));
        var snapshot = BpmnElementInspector.Describe(document, "Task")! with { Bounds = null };
        Assert.DoesNotContain(BpmnContextualActionsBase.DefaultsFor(snapshot),
            action => action.Kind == BpmnActionKind.AddTextAnnotation);
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Pool("Pool", "Pool", new Bounds(0, 0, 800, 400))
        .Lane("Lane", "Lane", new Bounds(30, 0, 770, 400), ["Task", "End"])
        .Node("Task", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
        .Node("End", BpmnFlowNodeType.EndEvent, new Bounds(300, 120, 36, 36))
        .DataObject("Data", "Data", new Bounds(400, 100, 36, 50))
        .DataStore("Store", "Store", new Bounds(500, 100, 50, 50))
        .TextAnnotation("Note", "Note", new Bounds(100, 250, 100, 30))
        .Group("Group", "Group", new Bounds(80, 80, 500, 200))
        .Build();
}
