using System.Xml;
using System.Xml.Schema;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

/// <summary>
/// Reader behaviour, asserted against both hand-written XML and the real sample documents.
/// </summary>
/// <remarks>
/// The fixtures matter more than the synthetic cases: they are exports from real modelling tools,
/// and they are where assumptions about optional attributes and element ordering actually break.
/// </remarks>
public sealed class BpmnXmlReaderTests
{
    private const string MinimalProcess = """
        <?xml version="1.0" encoding="UTF-8"?>
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                     xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                     xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                     id="Definitions_1" targetNamespace="http://example.test/bpmn">
          <process id="Process_1" isExecutable="true">
            <startEvent id="Start_1" name="Begin" />
            <task id="Task_1" name="Do the thing" />
            <endEvent id="End_1" />
            <sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
            <sequenceFlow id="Flow_2" sourceRef="Task_1" targetRef="End_1" />
          </process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Process_1">
              <bpmndi:BPMNShape id="Start_1_di" bpmnElement="Start_1">
                <dc:Bounds x="100" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Task_1_di" bpmnElement="Task_1">
                <dc:Bounds x="200" y="80" width="100" height="80" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="End_1_di" bpmnElement="End_1">
                <dc:Bounds x="360" y="100" width="36" height="36" />
              </bpmndi:BPMNShape>
              <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1">
                <di:waypoint x="136" y="118" />
                <di:waypoint x="200" y="118" />
              </bpmndi:BPMNEdge>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </definitions>
        """;

    [Fact]
    public void ReadsDefinitionsMetadata()
    {
        var result = BpmnXmlReader.Read(MinimalProcess);

        Assert.Equal("Definitions_1", result.Document.Definitions.Id);
        Assert.Equal("http://example.test/bpmn", result.Document.Definitions.TargetNamespace);
    }

    [Fact]
    public void ReadsFlowNodesWithTypeAndContainer()
    {
        var document = BpmnXmlReader.Read(MinimalProcess).Document;

        Assert.Equal(3, document.FlowNodes.Count);

        var task = document.FindFlowNode("Task_1");
        Assert.NotNull(task);
        Assert.Equal(BpmnFlowNodeType.Task, task.Type);
        Assert.Equal("Do the thing", task.Name);
        Assert.Equal("Process_1", task.ContainerId);
    }

    [Fact]
    public void PreservesDiGeometryExactly()
    {
        // Specification sections 10.6 and 11: bounds are used as stored, never recomputed, or a
        // round trip silently moves the user's diagram.
        var document = BpmnXmlReader.Read(MinimalProcess).Document;
        var shape = document.Diagrams[0].Plane.Shapes.Single(s => s.BpmnElement == "Task_1");

        Assert.Equal(200d, shape.Bounds.X);
        Assert.Equal(80d, shape.Bounds.Y);
        Assert.Equal(100d, shape.Bounds.Width);
        Assert.Equal(80d, shape.Bounds.Height);
    }

    [Fact]
    public void PreservesEdgeWaypointsExactly()
    {
        var document = BpmnXmlReader.Read(MinimalProcess).Document;
        var edge = document.Diagrams[0].Plane.Edges.Single(e => e.BpmnElement == "Flow_1");

        Assert.Equal(2, edge.Waypoints.Count);
        Assert.Equal(136d, edge.Waypoints[0].X);
        Assert.Equal(200d, edge.Waypoints[1].X);
    }

    [Fact]
    public void ReportsAConnectorWithTooFewWaypointsRatherThanRepairingIt()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <process id="Process_1">
                <startEvent id="Start_1" />
                <endEvent id="End_1" />
                <sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="End_1" />
              </process>
              <bpmndi:BPMNDiagram id="Diagram_1">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Process_1">
                  <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1" />
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </definitions>
            """;

        var result = BpmnXmlReader.Read(xml);

        Assert.Contains(
            result.Diagnostics,
            d => d.Severity == BpmnImportSeverity.Degraded && d.ElementId == "Flow_1");
    }

    [Fact]
    public void ReportsAConnectorWhoseWaypointsAreAllTheSamePoint()
    {
        // simple-process.bpmn in the playground really does this: two waypoints, both (0,0),
        // while its shapes sit at (80,120) and (220,100).
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                         xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <process id="Process_1">
                <startEvent id="Start_1" />
                <endEvent id="End_1" />
                <sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="End_1" />
              </process>
              <bpmndi:BPMNDiagram id="Diagram_1">
                <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Process_1">
                  <bpmndi:BPMNEdge id="Flow_1_di" bpmnElement="Flow_1">
                    <di:waypoint x="0" y="0" />
                    <di:waypoint x="0" y="0" />
                  </bpmndi:BPMNEdge>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </definitions>
            """;

        var result = BpmnXmlReader.Read(xml);

        var diagnostic = Assert.Single(
            result.Diagnostics,
            d => d.Severity == BpmnImportSeverity.Degraded && d.ElementId == "Flow_1");
        Assert.Contains("same point", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void DistinguishesAnEventSubProcessFromAnOrdinarySubProcess()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <process id="Process_1">
                <subProcess id="Sub_1" />
                <subProcess id="Sub_2" triggeredByEvent="true" />
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;

        Assert.Equal(BpmnFlowNodeType.SubProcess, document.FindFlowNode("Sub_1")!.Type);
        Assert.Equal(BpmnFlowNodeType.EventSubProcess, document.FindFlowNode("Sub_2")!.Type);
    }

    [Fact]
    public void KeepsNestedSubProcessContentInPlaceWithItsOwnContainer()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <process id="Process_1">
                <subProcess id="Sub_1">
                  <startEvent id="InnerStart_1" />
                  <task id="InnerTask_1" />
                  <sequenceFlow id="InnerFlow_1" sourceRef="InnerStart_1" targetRef="InnerTask_1" />
                </subProcess>
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;

        Assert.Equal("Sub_1", document.FindFlowNode("InnerTask_1")!.ContainerId);
        Assert.Equal("Sub_1", document.SequenceFlows.Single(f => f.Id == "InnerFlow_1").ContainerId);
        Assert.True(document.FindFlowNode("Sub_1")!.FlowElementsOwned);
    }

    [Fact]
    public void ReadsEventDefinitionsAndInterruptingState()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <process id="Process_1">
                <task id="Task_1" />
                <boundaryEvent id="Boundary_1" attachedToRef="Task_1" cancelActivity="false">
                  <timerEventDefinition />
                </boundaryEvent>
                <endEvent id="End_1">
                  <terminateEventDefinition />
                </endEvent>
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;

        var boundary = document.FindFlowNode("Boundary_1")!;
        Assert.Equal(BpmnEventDefinitionType.Timer, boundary.EventDefinition);
        Assert.False(boundary.IsInterrupting);
        Assert.Equal("Task_1", boundary.AttachedToRef);

        Assert.Equal(BpmnEventDefinitionType.Terminate, document.FindFlowNode("End_1")!.EventDefinition);
    }

    [Fact]
    public void ResolvesMultipleTriggersToMultipleOrParallelMultiple()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <process id="Process_1">
                <startEvent id="Start_1">
                  <messageEventDefinition />
                  <timerEventDefinition />
                </startEvent>
                <startEvent id="Start_2" parallelMultiple="true">
                  <messageEventDefinition />
                  <signalEventDefinition />
                </startEvent>
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;

        Assert.Equal(BpmnEventDefinitionType.Multiple, document.FindFlowNode("Start_1")!.EventDefinition);
        Assert.Equal(BpmnEventDefinitionType.ParallelMultiple, document.FindFlowNode("Start_2")!.EventDefinition);
    }

    [Fact]
    public void ReadsLoopCharacteristics()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <process id="Process_1">
                <task id="Task_1"><standardLoopCharacteristics /></task>
                <task id="Task_2"><multiInstanceLoopCharacteristics isSequential="true" /></task>
                <task id="Task_3"><multiInstanceLoopCharacteristics /></task>
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;

        Assert.Equal(BpmnLoopCharacteristics.Standard, document.FindFlowNode("Task_1")!.LoopCharacteristics);
        Assert.Equal(BpmnLoopCharacteristics.MultiInstanceSequential, document.FindFlowNode("Task_2")!.LoopCharacteristics);
        // isSequential defaults to false, which BPMN draws as the parallel three-bar marker.
        Assert.Equal(BpmnLoopCharacteristics.MultiInstanceParallel, document.FindFlowNode("Task_3")!.LoopCharacteristics);
    }

    [Fact]
    public void ReadsQNameCallActivityReferencesWithoutCollapsingNamespaces()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:tns="https://example.test/target"
                         xmlns:ext="https://example.test/external"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1" />
              <process id="Process_external" />
              <process id="Caller">
                <callActivity id="Call_local" calledElement="tns:Process_1" />
                <callActivity id="Call_external" calledElement="ext:Process_1" />
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;

        var local = document.FindFlowNode("Call_local")!;
        Assert.Equal("Process_1", local.CalledElementRef);
        Assert.Equal("https://example.test/target", local.CalledElementQName?.NamespaceUri);
        Assert.Equal("tns:Process_1", local.CalledElementQName?.LexicalValue);

        var external = document.FindFlowNode("Call_external")!;
        Assert.Null(external.CalledElementRef);
        Assert.Equal("https://example.test/external", external.CalledElementQName?.NamespaceUri);
        Assert.Equal("Process_1", external.CalledElementQName?.LocalName);
    }

    [Fact]
    public void ValidatesOnlyAgainstAnExplicitlySuppliedSchemaSet()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="https://example.test/bpmn" />
            """;
        const string schemaXml = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       targetNamespace="http://www.omg.org/spec/BPMN/20100524/MODEL"
                       elementFormDefault="qualified">
              <xs:element name="definitions">
                <xs:complexType>
                  <xs:attribute name="id" type="xs:string" use="required" />
                  <xs:attribute name="targetNamespace" type="xs:string" use="required" />
                  <xs:attribute name="requiredByTest" type="xs:string" use="required" />
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        using (var schemaReader = XmlReader.Create(new StringReader(schemaXml)))
        {
            schemas.Add("http://www.omg.org/spec/BPMN/20100524/MODEL", schemaReader);
        }

        var withoutSchemas = BpmnXmlReader.Read(xml);
        var withEmptySchemas = BpmnXmlReader.Read(xml, new XmlSchemaSet());
        var withSchemas = BpmnXmlReader.Read(xml, schemas);

        Assert.Equal(BpmnSchemaValidationStatus.NotRun, withoutSchemas.SchemaValidationStatus);
        Assert.Equal(BpmnSchemaValidationStatus.NotRun, withEmptySchemas.SchemaValidationStatus);
        Assert.Equal(BpmnSchemaValidationStatus.Failed, withSchemas.SchemaValidationStatus);
        Assert.DoesNotContain(withoutSchemas.Diagnostics, diagnostic => diagnostic.Severity == BpmnImportSeverity.SchemaError);
        Assert.Contains(withSchemas.Diagnostics, diagnostic => diagnostic.Severity == BpmnImportSeverity.SchemaError);
    }

    [Fact]
    public void ReportsSuccessfulSchemaValidationAgainstSuppliedSchemas()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="https://example.test/bpmn"
                         requiredByTest="supplied" />
            """;
        const string schemaXml = """
            <xs:schema xmlns:xs="http://www.w3.org/2001/XMLSchema"
                       targetNamespace="http://www.omg.org/spec/BPMN/20100524/MODEL"
                       elementFormDefault="qualified">
              <xs:element name="definitions">
                <xs:complexType>
                  <xs:attribute name="id" type="xs:string" use="required" />
                  <xs:attribute name="targetNamespace" type="xs:string" use="required" />
                  <xs:attribute name="requiredByTest" type="xs:string" use="required" />
                </xs:complexType>
              </xs:element>
            </xs:schema>
            """;
        var schemas = new XmlSchemaSet();
        using (var schemaReader = XmlReader.Create(new StringReader(schemaXml)))
        {
            schemas.Add("http://www.omg.org/spec/BPMN/20100524/MODEL", schemaReader);
        }

        var result = BpmnXmlReader.Read(xml, schemas);

        Assert.Equal(BpmnSchemaValidationStatus.Passed, result.SchemaValidationStatus);
        Assert.DoesNotContain(result.Diagnostics, diagnostic => diagnostic.Severity == BpmnImportSeverity.SchemaError);
    }

    [Fact]
    public void ReadsExpressionFieldsAndDataAssociationAssignments()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                         id="Definitions_1" targetNamespace="https://example.test/target">
              <process id="Process_1">
                <task id="Task_standard">
                  <standardLoopCharacteristics testBefore="false" loopMaximum="8">
                    <loopCondition language="urn:expr" xsi:type="bpmn:tFormalExpression">count &lt; 8</loopCondition>
                  </standardLoopCharacteristics>
                </task>
                <task id="Task_multi">
                  <multiInstanceLoopCharacteristics isSequential="true" behavior="Complex">
                    <loopCardinality xsi:type="bpmn:tFormalExpression">4</loopCardinality>
                    <loopDataInputRef>Input_1</loopDataInputRef>
                    <inputDataItem id="InputItem_1" name="item" itemSubjectRef="Item_1" isCollection="false" />
                    <loopDataOutputRef>Output_1</loopDataOutputRef>
                    <outputDataItem id="OutputItem_1" name="result" itemSubjectRef="Item_2" />
                    <complexBehaviorDefinition event="Exclusive">
                      <condition xsi:type="bpmn:tFormalExpression">done</condition>
                    </complexBehaviorDefinition>
                    <completionCondition xsi:type="bpmn:tFormalExpression">completed &gt; 0</completionCondition>
                  </multiInstanceLoopCharacteristics>
                  <dataInputAssociation id="Assignment_1">
                    <sourceRef>Data_1</sourceRef>
                    <targetRef>Input_1</targetRef>
                    <assignment>
                      <from xsi:type="bpmn:tFormalExpression">source.value</from>
                      <to xsi:type="bpmn:tFormalExpression">target.value</to>
                    </assignment>
                  </dataInputAssociation>
                </task>
                <complexGateway id="Gateway_complex">
                  <activationCondition xsi:type="bpmn:tFormalExpression">ready</activationCondition>
                </complexGateway>
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;

        var standard = document.FindFlowNode("Task_standard")!;
        Assert.Equal(false, standard.StandardLoop?.TestBefore);
        Assert.Equal(8, standard.StandardLoop?.LoopMaximum);
        Assert.Equal("count < 8", standard.StandardLoop?.LoopCondition?.Body);
        Assert.Equal("urn:expr", standard.StandardLoop?.LoopCondition?.Language);

        var multi = document.FindFlowNode("Task_multi")!.MultiInstanceLoop!;
        Assert.True(multi.IsSequential);
        Assert.Equal("Complex", multi.Behavior);
        Assert.Equal("4", multi.LoopCardinality?.Body);
        Assert.Equal("Input_1", multi.LoopDataInputRef);
        Assert.Equal("InputItem_1", multi.InputDataItem?.Id);
        Assert.Equal("Output_1", multi.LoopDataOutputRef);
        Assert.Equal("OutputItem_1", multi.OutputDataItem?.Id);
        Assert.Equal("Exclusive", Assert.Single(multi.ComplexBehaviorDefinitions!).Event);
        Assert.Equal("done", multi.ComplexBehaviorDefinitions![0].Condition?.Body);
        Assert.Equal("completed > 0", multi.CompletionCondition?.Body);

        Assert.Equal("ready", document.FindFlowNode("Gateway_complex")?.ActivationCondition?.Body);
        var assignment = Assert.Single(document.DataAssociations).Assignments.Single();
        Assert.Equal("source.value", assignment.From.Body);
        Assert.Equal("target.value", assignment.To.Body);
    }

    [Fact]
    public void ProjectsPoolAndLaneMembershipOntoFlowNodes()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <collaboration id="Collaboration_1">
                <participant id="Participant_1" name="Warehouse" processRef="Process_1" />
                <participant id="Participant_2" name="Customer" />
              </collaboration>
              <process id="Process_1">
                <laneSet id="LaneSet_1">
                  <lane id="Lane_1" name="Picking">
                    <flowNodeRef>Task_1</flowNodeRef>
                  </lane>
                </laneSet>
                <task id="Task_1" />
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;
        var task = document.FindFlowNode("Task_1")!;

        Assert.Equal("Participant_1", task.PoolId);
        Assert.Equal("Lane_1", task.LaneId);

        // A black-box Participant is valid and keeps a null processRef (section 9.1).
        var blackBox = document.Collaboration!.Participants.Single(p => p.Id == "Participant_2");
        Assert.Null(blackBox.ProcessRef);
    }

    [Fact]
    public void RetainsVendorExtensionsVerbatim()
    {
        const string xml = """
            <?xml version="1.0" encoding="UTF-8"?>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:zeebe="http://camunda.org/schema/zeebe/1.0"
                         id="Definitions_1" targetNamespace="http://example.test/bpmn">
              <process id="Process_1">
                <serviceTask id="Task_1">
                  <extensionElements>
                    <zeebe:taskDefinition type="payment" retries="3" />
                  </extensionElements>
                </serviceTask>
              </process>
            </definitions>
            """;

        var document = BpmnXmlReader.Read(xml).Document;
        var extension = Assert.Single(document.FindFlowNode("Task_1")!.Extras.Extensions);

        Assert.Equal("http://camunda.org/schema/zeebe/1.0", extension.Namespace);
        Assert.Equal("taskDefinition", extension.LocalName);
        Assert.Equal("payment", extension.Attributes["type"]);
        Assert.Equal("3", extension.Attributes["retries"]);
    }

    [Fact]
    public void RejectsMalformedXml()
    {
        Assert.Throws<BpmnXmlException>(() => BpmnXmlReader.Read("<definitions"));
    }

    [Fact]
    public void RejectsANonBpmnRootElement()
    {
        Assert.Throws<BpmnXmlException>(() => BpmnXmlReader.Read("<html></html>"));
    }

    [Fact]
    public void RefusesToResolveExternalEntities()
    {
        // XXE closed outright rather than mitigated: DTD processing is prohibited, so a document
        // declaring one fails to parse at all (specification section 5.5, layer 1).
        const string xml = """
            <?xml version="1.0"?>
            <!DOCTYPE definitions [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="d" targetNamespace="t">
              <process id="Process_1" name="&xxe;" />
            </definitions>
            """;

        Assert.Throws<BpmnXmlException>(() => BpmnXmlReader.Read(xml));
    }
}
