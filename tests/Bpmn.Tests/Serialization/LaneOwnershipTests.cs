using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>
/// A Lane always has a Pool to subdivide, including one that arrived without.
/// </summary>
/// <remarks>
/// Section 1.5 writes every rule about a Lane in terms of the Pool it divides: stacked inside it,
/// subdividing its bounds, taking its orientation. A Lane on a Process no Participant references has
/// none of those. The editor refuses to create one; a file that already contains one is given a Pool
/// on the way in, because the alternative is a document that is permanently invalid and cannot be
/// edited into a valid one.
/// </remarks>
public sealed class LaneOwnershipTests
{
    [Fact]
    public void AnImportedLaneWithNoPoolGetsOne()
    {
        var result = BpmnXmlReader.Read(PoollessLanes);

        var pool = Assert.Single(result.Document.Collaboration?.Participants ?? []);
        Assert.Equal("Process_1", pool.ProcessRef);
    }

    [Fact]
    public void AndTheReaderSaysItDidSo()
    {
        /*
         * The one structural rewrite the reader performs, so it is the one it has to report.
         * Sections 10.6 and 6.1 exist to stop a file being changed on the way in; changing it
         * silently would be the part they actually forbid.
         */
        var result = BpmnXmlReader.Read(PoollessLanes);

        var told = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Message.Contains("no Pool", StringComparison.Ordinal));
        Assert.Equal(BpmnImportSeverity.Degraded, told.Severity);
    }

    [Fact]
    public void ThePoolIsDrawnAroundTheLanesThatWereThere()
    {
        // Derived from the lanes rather than invented, so nothing the file drew appears to move.
        var document = BpmnXmlReader.Read(PoollessLanes).Document;

        var pool = document.Collaboration!.Participants[0];
        var poolBox = Shape(document, pool.Id).Bounds;

        foreach (var lane in document.Lanes)
        {
            var box = Shape(document, lane.Id).Bounds;

            Assert.True(box.X >= poolBox.X, $"lane {lane.Id} starts left of its Pool");
            Assert.True(box.Y >= poolBox.Y, $"lane {lane.Id} starts above its Pool");
            Assert.True(box.X + box.Width <= poolBox.X + poolBox.Width + 0.01, "a lane reaches past its Pool");
            Assert.True(box.Y + box.Height <= poolBox.Y + poolBox.Height + 0.01, "a lane reaches below its Pool");
        }
    }

    [Fact]
    public void AndTheDocumentIsValidAfterwards()
    {
        // The point of wrapping rather than reporting: the file opens, edits and exports.
        var report = BpmnValidator.Validate(BpmnXmlReader.Read(PoollessLanes).Document);

        Assert.DoesNotContain(report.Diagnostics, diagnostic => diagnostic.RuleId == "CT-012");
    }

    [Fact]
    public void NothingElseIsTouched()
    {
        /*
         * A Participant, a Collaboration to hold it and one shape. Every Lane, every FlowNode and
         * every waypoint is left exactly as read — that is what makes this a narrow exception to
         * "draw what the file says" rather than a licence to tidy imports up.
         */
        var document = BpmnXmlReader.Read(PoollessLanes).Document;

        Assert.Equal(2, document.Lanes.Count);
        Assert.Single(document.LaneSets);
        Assert.Equal(["Task_1"], document.FlowNodes.Select(node => node.Id));
        Assert.Equal("Process_1", document.LaneSets[0].ContainerId);
    }

    [Fact]
    public void AFileThatAlreadyHasAPoolIsLeftAlone()
    {
        var before = BpmnXmlReader.Read(PooledLanes).Document;

        Assert.Single(before.Collaboration!.Participants);
        Assert.DoesNotContain(
            BpmnXmlReader.Read(PooledLanes).Diagnostics,
            diagnostic => diagnostic.Message.Contains("no Pool", StringComparison.Ordinal));
    }

    // --- the validator ----------------------------------------------------------------

    [Fact]
    public void ALaneWhoseProcessNoPoolReferencesIsAnError()
    {
        /*
         * Reachable only for a document assembled some other way — the editor refuses to create
         * one and the reader wraps one it finds — which is exactly when a document-wide check is
         * the thing that catches it.
         */
        var document = Poolless();

        var problem = Assert.Single(
            BpmnValidator.Validate(document).Diagnostics,
            diagnostic => diagnostic.RuleId == "CT-012");

        Assert.Equal(BpmnSeverity.Error, problem.Severity);
        Assert.Equal("Lane_1", problem.ElementId);
    }

    [Fact]
    public void AndSaysWhichProcessHasNoPool()
    {
        var problem = Assert.Single(
            BpmnValidator.Validate(Poolless()).Diagnostics,
            diagnostic => diagnostic.RuleId == "CT-012");

        Assert.Contains("Process_1", problem.Message, StringComparison.Ordinal);
    }

    // --- fixtures ---------------------------------------------------------------------

    private static BpmnShape Shape(BpmnDocument document, string elementId) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Shapes)
        .Single(shape => shape.BpmnElement == elementId);

    /// <summary>A Lane whose lane set sits on a Process no Participant references.</summary>
    private static BpmnDocument Poolless()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 120, 100, 80), "Work")
            .Build();

        return document with
        {
            LaneSets = [new BpmnLaneSet("LaneSet_1", "Process_1", ["Lane_1"])],
            Lanes = [new BpmnLane("Lane_1", "Sales", "LaneSet_1", ["Task_1"])],
        };
    }

    private const string PoollessLanes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_1" targetNamespace="http://example.com/lanes">
          <bpmn:process id="Process_1" isExecutable="false">
            <bpmn:laneSet id="LaneSet_1">
              <bpmn:lane id="Lane_Sales" name="Sales">
                <bpmn:flowNodeRef>Task_1</bpmn:flowNodeRef>
              </bpmn:lane>
              <bpmn:lane id="Lane_Support" name="Support" />
            </bpmn:laneSet>
            <bpmn:task id="Task_1" name="Work" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Process_1">
              <bpmndi:BPMNShape id="Shape_LaneSales" bpmnElement="Lane_Sales" isHorizontal="true">
                <dc:Bounds x="190" y="80" width="600" height="125" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_LaneSupport" bpmnElement="Lane_Support" isHorizontal="true">
                <dc:Bounds x="190" y="205" width="600" height="125" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_Task" bpmnElement="Task_1">
                <dc:Bounds x="260" y="100" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;

    private const string PooledLanes = """
        <?xml version="1.0" encoding="UTF-8"?>
        <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                          xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                          xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                          id="Definitions_2" targetNamespace="http://example.com/lanes">
          <bpmn:collaboration id="Collaboration_1">
            <bpmn:participant id="Participant_1" name="Sales" processRef="Process_1" />
          </bpmn:collaboration>
          <bpmn:process id="Process_1" isExecutable="false">
            <bpmn:laneSet id="LaneSet_1">
              <bpmn:lane id="Lane_Sales" name="Sales" />
            </bpmn:laneSet>
            <bpmn:task id="Task_1" name="Work" />
          </bpmn:process>
          <bpmndi:BPMNDiagram id="Diagram_2">
            <bpmndi:BPMNPlane id="Plane_2" bpmnElement="Collaboration_1">
              <bpmndi:BPMNShape id="Shape_Pool" bpmnElement="Participant_1" isHorizontal="true">
                <dc:Bounds x="160" y="80" width="630" height="125" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_LaneSales" bpmnElement="Lane_Sales" isHorizontal="true">
                <dc:Bounds x="190" y="80" width="600" height="125" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Shape_Task" bpmnElement="Task_1">
                <dc:Bounds x="260" y="100" width="100" height="80" />
              </bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </bpmn:definitions>
        """;
}
