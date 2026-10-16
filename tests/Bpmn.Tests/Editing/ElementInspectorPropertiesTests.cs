using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// <see cref="BpmnElementInspector.PropertiesOf"/> is the cheap read of what <c>Describe</c>
/// reports, for callers that compare every element's values and cannot afford a validation pass each.
/// </summary>
public sealed class ElementInspectorPropertiesTests
{
    private static BpmnDocument Document() => new BpmnDocumentBuilder()
        .Node("review", BpmnFlowNodeType.UserTask, new Bounds(200, 110, 100, 80), "Review")
        .Build();

    [Fact]
    public void ReportsTheSamePropertiesAsDescribe()
    {
        var document = Document();

        var properties = BpmnElementInspector.PropertiesOf(document, "review");

        Assert.NotEmpty(properties);
        Assert.Equal(
            BpmnElementInspector.Describe(document, "review")!.Properties.Select(property => (property.Name, property.Value)),
            properties.Select(property => (property.Name, property.Value)));
    }

    [Fact]
    public void ReportsNothingForAnUnknownElement()
    {
        Assert.Empty(BpmnElementInspector.PropertiesOf(Document(), "missing"));
    }
}
