using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Editing standard BPMN properties and switching an element's type (specification section 7).
/// </summary>
/// <remarks>
/// Both are schema-driven: the descriptors a snapshot publishes are what the editor validates
/// against, so half of what is asserted here is that the two agree. The other half is atomicity —
/// section 7 requires a property edit and a type switch to apply completely or not at all, and
/// every rejection below is a case where a partial application would leave the document quietly
/// contradicting itself.
/// </remarks>
public sealed class PropertyEditTests
{
    // --- setting values ----------------------------------------------------------

    [Fact]
    public void RenamesAnElement()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Task_Yes", "name", "Dispatch"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Equal("Dispatch", result.Document.FindFlowNode("Task_Yes")!.Name);
    }

    [Fact]
    public void ClearsAValueWithNullRatherThanEmptyText()
    {
        // An absent attribute and an empty one are different things in the file, and the
        // distinction survives the round trip.
        var named = Apply(Gatewayed(), new UpdatePropertiesCommand("Task_Yes", "name", string.Empty));
        var cleared = Apply(Gatewayed(), new UpdatePropertiesCommand("Task_Yes", "name", null));

        Assert.Equal(string.Empty, named.Document.FindFlowNode("Task_Yes")!.Name);
        Assert.Null(cleared.Document.FindFlowNode("Task_Yes")!.Name);
    }

    [Fact]
    public void SetsSeveralPropertiesAtOnce()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Task_Yes",
        [
            new BpmnPropertyValue("name", "Dispatch"),
            new BpmnPropertyValue("loopCharacteristics", "multiInstanceParallel"),
            new BpmnPropertyValue("isForCompensation", "true"),
        ]));

        Assert.True(result.IsApplied, Codes(result));
        var task = result.Document.FindFlowNode("Task_Yes")!;
        Assert.Equal("Dispatch", task.Name);
        Assert.Equal(BpmnLoopCharacteristics.MultiInstanceParallel, task.LoopCharacteristics);
        Assert.True(task.IsForCompensation);
    }

    [Fact]
    public void ChangingMultiInstanceModeUpdatesSequentialFlagAndCanonicalXml()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         id="Definitions_1" targetNamespace="https://example.test/bpmn">
              <process id="Process_1">
                <task id="Task_1">
                  <multiInstanceLoopCharacteristics isSequential="true" />
                </task>
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var result = Apply(
            document,
            new UpdatePropertiesCommand("Task_1", "loopCharacteristics", "multiInstanceParallel"));

        Assert.True(result.IsApplied, Codes(result));
        var task = result.Document.FindFlowNode("Task_1")!;
        Assert.Equal(BpmnLoopCharacteristics.MultiInstanceParallel, task.LoopCharacteristics);
        Assert.False(task.MultiInstanceLoop?.IsSequential);

        var output = BpmnXmlWriter.Write(result.Document).Xml;
        Assert.Equal("false", XDocument.Parse(output)
            .Descendants(XName.Get("multiInstanceLoopCharacteristics", "http://www.omg.org/spec/BPMN/20100524/MODEL"))
            .Single()
            .Attribute("isSequential")?.Value);
        Assert.Equal(
            BpmnLoopCharacteristics.MultiInstanceParallel,
            BpmnXmlReader.Read(output).Document.FindFlowNode("Task_1")!.LoopCharacteristics);
    }

    [Fact]
    public void EditingAnExpressionTypeResolvesItsNewNamespace()
    {
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:xs="http://www.w3.org/2001/XMLSchema"
                         xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"
                         id="Definitions_1" targetNamespace="https://example.test/bpmn">
              <process id="Process_1">
                <complexGateway id="Gateway_1">
                  <activationCondition xsi:type="bpmn:tFormalExpression">ready</activationCondition>
                </complexGateway>
              </process>
            </definitions>
            """;
        var document = BpmnXmlReader.Read(xml).Document;

        var edited = Apply(
            document,
            new UpdatePropertiesCommand("Gateway_1", "activationCondition.type", "xs:string"));

        Assert.True(edited.IsApplied, Codes(edited));
        var type = edited.Document.FindFlowNode("Gateway_1")!.ActivationCondition!;
        Assert.Equal("http://www.w3.org/2001/XMLSchema", type.TypeNamespaceUri);

        var output = BpmnXmlWriter.Write(edited.Document).Xml;
        var reread = BpmnXmlReader.Read(output).Document.FindFlowNode("Gateway_1")!.ActivationCondition!;
        Assert.Equal("http://www.w3.org/2001/XMLSchema", reread.TypeNamespaceUri);
        var rereadType = Assert.IsType<string>(reread.TypeQName);
        Assert.Equal("string", rereadType[(rereadType.LastIndexOf(':') + 1)..]);
    }

    [Fact]
    public void RetriggersAnEventAndReplacesItsDefinitions()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("End_1", "eventDefinition", "terminate"));

        Assert.True(result.IsApplied, Codes(result));
        var end = result.Document.FindFlowNode("End_1")!;
        Assert.Equal(BpmnEventDefinitionType.Terminate, end.EventDefinition);
        Assert.Equal(BpmnEventDefinitionType.Terminate, Assert.Single(end.EventDefinitions).Type);
    }

    [Fact]
    public void ReportsTheReferencesARetriggerThrewAway()
    {
        // An errorRef on a Message Event is not a Message. The payload cannot come along, and
        // section 7 wants the loss reported rather than discovered on export.
        var document = Gatewayed();
        var withError = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "End_1"
                ? node with
                {
                    EventDefinition = BpmnEventDefinitionType.Error,
                    EventDefinitions = [new BpmnEventDefinition(BpmnEventDefinitionType.Error, Ref: "Error_1")],
                }
                : node)],
        };

        var result = Apply(withError, new UpdatePropertiesCommand("End_1", "eventDefinition", "none"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Empty(result.Document.FindFlowNode("End_1")!.EventDefinitions);
        Assert.Contains(result.Diagnostics, d => d.Code == "EDIT-082" && d.Message.Contains("Error_1", StringComparison.Ordinal));
    }

    [Fact]
    public void EditsAConnector()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Flow_Yes",
        [
            new BpmnPropertyValue("name", "approved"),
            new BpmnPropertyValue("conditionExpression", "${amount > 0}"),
        ]));

        Assert.True(result.IsApplied, Codes(result));
        var flow = result.Document.SequenceFlows.Single(f => f.Id == "Flow_Yes");
        Assert.Equal("approved", flow.Name);
        Assert.Equal("${amount > 0}", flow.ConditionExpression);
    }

    [Fact]
    public void EditsLoopGatewayAndDataAssignmentExpressions()
    {
        var built = Gatewayed();
        var document = built with
        {
            FlowNodes =
            [
                .. built.FlowNodes.Select(node => node.Id switch
                {
                    "Task_Yes" => node with
                    {
                        LoopCharacteristics = BpmnLoopCharacteristics.Standard,
                        StandardLoop = new BpmnStandardLoopDetails(),
                    },
                    "Gateway_1" => node with { Type = BpmnFlowNodeType.ComplexGateway },
                    _ => node,
                }),
            ],
            DataAssociations =
            [
                new BpmnDataAssociation(
                    "DataAssociation_1",
                    BpmnDataAssociationKind.Input,
                    ["Task_No"],
                    "Task_Yes",
                    "Process_1")
                    {
                        Assignments =
                        [
                            new BpmnDataAssignment(
                                new BpmnFormalExpression("source"),
                                new BpmnFormalExpression("target")),
                        ],
                    },
            ],
        };

        var loop = Apply(document, new UpdatePropertiesCommand("Task_Yes",
        [
            new BpmnPropertyValue("standardLoop.testBefore", "false"),
            new BpmnPropertyValue("standardLoop.loopMaximum", "5"),
            new BpmnPropertyValue("standardLoop.loopCondition", "again"),
        ]));
        Assert.True(loop.IsApplied, Codes(loop));
        Assert.Equal(5, loop.Document.FindFlowNode("Task_Yes")!.StandardLoop?.LoopMaximum);
        Assert.Equal("again", loop.Document.FindFlowNode("Task_Yes")!.StandardLoop?.LoopCondition?.Body);

        var gateway = Apply(loop.Document, new UpdatePropertiesCommand("Gateway_1", "activationCondition", "ready"));
        Assert.True(gateway.IsApplied, Codes(gateway));
        Assert.Equal("ready", gateway.Document.FindFlowNode("Gateway_1")!.ActivationCondition?.Body);

        var assignment = Apply(gateway.Document, new UpdatePropertiesCommand("DataAssociation_1",
        [
            new BpmnPropertyValue("assignment.0.from", "source.amount"),
            new BpmnPropertyValue("assignment.0.from.language", "urn:expression-language"),
            new BpmnPropertyValue("assignment.0.from.type", "bpmn:tFormalExpression"),
            new BpmnPropertyValue("assignment.0.to", "target.amount"),
        ]));
        Assert.True(assignment.IsApplied, Codes(assignment));
        Assert.Equal("source.amount", assignment.Document.DataAssociations.Single().Assignments.Single().From.Body);
        Assert.Equal("urn:expression-language", assignment.Document.DataAssociations.Single().Assignments.Single().From.Language);
        Assert.Equal("bpmn:tFormalExpression", assignment.Document.DataAssociations.Single().Assignments.Single().From.TypeQName);
        Assert.Equal("target.amount", assignment.Document.DataAssociations.Single().Assignments.Single().To.Body);
    }

    [Fact]
    public void EditsAProcessAndAParticipant()
    {
        var document = Collaboration();

        var process = Apply(document, new UpdatePropertiesCommand("Process_1", "isExecutable", "true"));
        Assert.True(process.Document.Processes.Single().IsExecutable);

        var pool = Apply(document, new UpdatePropertiesCommand("Pool_1", "name", "Supplier"));
        Assert.Equal("Supplier", pool.Document.Collaboration!.Participants.Single().Name);
    }

    [Fact]
    public void EditsAnAnnotationsBody()
    {
        var document = new BpmnDocumentBuilder()
            .TextAnnotation("Note_1", "old", new Bounds(80, 80, 160, 60))
            .Build();

        var result = Apply(document, new UpdatePropertiesCommand("Note_1", "text", "new\nwords"));

        Assert.Equal("new\nwords", result.Document.TextAnnotations.Single().Text);
    }

    // --- validation ---------------------------------------------------------------

    [Fact]
    public void RefusesAPropertyThatIsNotOnThisElement()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Task_Yes", "conditionExpression", "x"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-070", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesAValueThatDoesNotFitItsKind()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Task_Yes", "isForCompensation", "yes"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-071", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesATriggerTheEventTypeDoesNotAllow()
    {
        // The choice list is the rule: a Timer cannot be thrown, so an End Event never offers one.
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("End_1", "eventDefinition", "timer"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-071", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesAReferenceThatResolvesToNothing()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Gateway_1", "defaultFlowRef", "Flow_Imaginary"));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-071", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void ChangesNothingWhenOneValueInABatchIsWrong()
    {
        // Section 7 requires an atomic result.
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Task_Yes",
        [
            new BpmnPropertyValue("name", "Dispatch"),
            new BpmnPropertyValue("isForCompensation", "perhaps"),
        ]));

        Assert.False(result.IsApplied);
        Assert.Equal("Ship", result.Document.FindFlowNode("Task_Yes")!.Name);
    }

    [Fact]
    public void RefusesAnEditToAnElementThatIsNotThere()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Nothing_1", "name", "x"));

        Assert.Equal("EDIT-072", Assert.Single(result.Rejections).Code);
    }

    // --- the default flow, which is stated twice ------------------------------------

    [Fact]
    public void MarkingAFlowDefaultPointsItsSourceAtIt()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Flow_No", "isDefault", "true"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.True(result.Document.SequenceFlows.Single(f => f.Id == "Flow_No").IsDefault);
        Assert.Equal("Flow_No", result.Document.FindFlowNode("Gateway_1")!.DefaultFlowRef);
    }

    [Fact]
    public void ChoosingADefaultOnTheSourceMarksTheFlow()
    {
        var result = Apply(Gatewayed(), new UpdatePropertiesCommand("Gateway_1", "defaultFlowRef", "Flow_No"));

        Assert.True(result.IsApplied, Codes(result));
        Assert.True(result.Document.SequenceFlows.Single(f => f.Id == "Flow_No").IsDefault);
    }

    [Fact]
    public void ASourceHasOnlyOneDefault()
    {
        var first = Apply(Gatewayed(), new UpdatePropertiesCommand("Flow_Yes", "isDefault", "true"));
        var second = Apply(first.Document, new UpdatePropertiesCommand("Flow_No", "isDefault", "true"));

        Assert.False(second.Document.SequenceFlows.Single(f => f.Id == "Flow_Yes").IsDefault);
        Assert.Contains(second.Diagnostics, d => d.Code == "SF-006" && d.Severity == BpmnEditSeverity.Info);
    }

    [Fact]
    public void ClearingTheFlagClearsTheSourcesReference()
    {
        var marked = Apply(Gatewayed(), new UpdatePropertiesCommand("Flow_No", "isDefault", "true"));
        var cleared = Apply(marked.Document, new UpdatePropertiesCommand("Flow_No", "isDefault", "false"));

        Assert.Null(cleared.Document.FindFlowNode("Gateway_1")!.DefaultFlowRef);
    }

    [Fact]
    public void RefusesADefaultFlowThatCarriesACondition()
    {
        // SF-006: the default branch is the one taken when no condition holds.
        var conditioned = Apply(Gatewayed(), new UpdatePropertiesCommand("Flow_Yes", "conditionExpression", "${ok}"));
        var result = Apply(conditioned.Document, new UpdatePropertiesCommand("Flow_Yes", "isDefault", "true"));

        Assert.False(result.IsApplied);
        Assert.Equal("SF-006", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void AcceptsClearingTheConditionAndSettingTheDefaultTogether()
    {
        var conditioned = Apply(Gatewayed(), new UpdatePropertiesCommand("Flow_Yes", "conditionExpression", "${ok}"));

        var result = Apply(conditioned.Document, new UpdatePropertiesCommand("Flow_Yes",
        [
            new BpmnPropertyValue("conditionExpression", null),
            new BpmnPropertyValue("isDefault", "true"),
        ]));

        Assert.True(result.IsApplied, Codes(result));
        Assert.True(result.Document.SequenceFlows.Single(f => f.Id == "Flow_Yes").IsDefault);
    }

    // --- type switching ---------------------------------------------------------------

    [Fact]
    public void SwitchesATaskAndKeepsEverythingElse()
    {
        var before = Gatewayed();
        var result = Apply(before, new SwitchElementTypeCommand("Task_Yes", BpmnFlowNodeType.UserTask));

        Assert.True(result.IsApplied, Codes(result));

        var task = result.Document.FindFlowNode("Task_Yes")!;
        Assert.Equal(BpmnFlowNodeType.UserTask, task.Type);
        Assert.Equal("Ship", task.Name);
        Assert.Equal("Process_1", task.ContainerId);

        // Section 7: id, geometry, connections and DI all survive.
        Assert.Equal(ShapeOf(before, "Task_Yes").Bounds, ShapeOf(result.Document, "Task_Yes").Bounds);
        Assert.Equal(before.SequenceFlows.Count, result.Document.SequenceFlows.Count);
        Assert.Contains(result.Document.SequenceFlows, f => f.TargetRef == "Task_Yes");
    }

    [Fact]
    public void SwitchesAGatewayAndRetriggersAnEvent()
    {
        var gateway = Apply(Gatewayed(), new SwitchElementTypeCommand("Gateway_1", BpmnFlowNodeType.InclusiveGateway));
        Assert.Equal(BpmnFlowNodeType.InclusiveGateway, gateway.Document.FindFlowNode("Gateway_1")!.Type);

        var end = Apply(Gatewayed(), new SwitchElementTypeCommand(
            "End_1",
            BpmnFlowNodeType.EndEvent,
            BpmnEventDefinitionType.Message));

        Assert.True(end.IsApplied, Codes(end));
        Assert.Equal(BpmnEventDefinitionType.Message, end.Document.FindFlowNode("End_1")!.EventDefinition);
    }

    [Fact]
    public void SwitchingAwayFromComplexGatewayClearsItsActivationCondition()
    {
        var original = Gatewayed();
        var withCondition = original with
        {
            FlowNodes = [.. original.FlowNodes.Select(node => node.Id == "Gateway_1"
                ? node with
                {
                    Type = BpmnFlowNodeType.ComplexGateway,
                    ActivationCondition = new BpmnFormalExpression("ready"),
                }
                : node)],
        };

        var result = Apply(
            withCondition,
            new SwitchElementTypeCommand("Gateway_1", BpmnFlowNodeType.InclusiveGateway));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Null(result.Document.FindFlowNode("Gateway_1")!.ActivationCondition);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "EDIT-082"
            && diagnostic.Message.Contains("activation condition", StringComparison.Ordinal));
        Assert.DoesNotContain(
            XDocument.Parse(BpmnXmlWriter.Write(result.Document).Xml).Descendants(),
            element => element.Name.LocalName == "activationCondition");
    }

    [Fact]
    public void RefusesToSwitchAcrossCategories()
    {
        var result = Apply(Gatewayed(), new SwitchElementTypeCommand("Task_Yes", BpmnFlowNodeType.ExclusiveGateway));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-081", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesStartToEnd()
    {
        // Named explicitly in section 7 as ineligible.
        var document = new BpmnDocumentBuilder()
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(100, 100, 36, 36))
            .Build();

        var result = Apply(document, new SwitchElementTypeCommand("Start_1", BpmnFlowNodeType.EndEvent));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-081", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesASwitchToTheTypeItAlreadyIs()
    {
        var result = Apply(Gatewayed(), new SwitchElementTypeCommand("Task_Yes", BpmnFlowNodeType.Task));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-081", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void ClearsTheDefaultBranchWhenTheGatewayStopsHavingOne()
    {
        var marked = Apply(Gatewayed(), new UpdatePropertiesCommand("Flow_No", "isDefault", "true"));

        var result = Apply(
            marked.Document,
            new SwitchElementTypeCommand("Gateway_1", BpmnFlowNodeType.ParallelGateway));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Null(result.Document.FindFlowNode("Gateway_1")!.DefaultFlowRef);
        Assert.False(result.Document.SequenceFlows.Single(f => f.Id == "Flow_No").IsDefault);
        Assert.Contains(result.Diagnostics, d => d.Code == "EDIT-082");
    }

    [Fact]
    public void RefusesAnEventBasedGatewayThatBranchesToTasks()
    {
        var result = Apply(Gatewayed(), new SwitchElementTypeCommand("Gateway_1", BpmnFlowNodeType.EventBasedGateway));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-083", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void RefusesToDemoteATransactionThatHasACancelBoundary()
    {
        // EV-002: only a Transaction may carry one, so demoting it would strand the event.
        var document = new BpmnDocumentBuilder()
            .Node("Txn_1", BpmnFlowNodeType.Transaction, new Bounds(100, 100, 300, 200))
            .Build();

        var withCancel = document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode(
                "Boundary_1",
                BpmnFlowNodeType.BoundaryEvent,
                "Process_1",
                "Cancelled",
                BpmnEventDefinitionType.Cancel,
                AttachedToRef: "Txn_1")],
        };

        var result = Apply(withCancel, new SwitchElementTypeCommand("Txn_1", BpmnFlowNodeType.SubProcess));

        Assert.False(result.IsApplied);
        Assert.Equal("EDIT-083", Assert.Single(result.Rejections).Code);
    }

    [Fact]
    public void DropsTheExclusiveMarkerFlagWhenTheGatewayIsNoLongerExclusive()
    {
        var document = Gatewayed();
        var marked = document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "Gateway_1"
                        ? shape with { IsMarkerVisible = true }
                        : shape)],
                },
            })],
        };

        var result = Apply(marked, new SwitchElementTypeCommand("Gateway_1", BpmnFlowNodeType.InclusiveGateway));

        Assert.True(result.IsApplied, Codes(result));
        Assert.Null(ShapeOf(result.Document, "Gateway_1").IsMarkerVisible);
    }

    // --- helpers -------------------------------------------------------------------

    private static BpmnEditResult Apply(BpmnDocument document, BpmnEditCommand command) =>
        BpmnDocumentEditor.Apply(document, command);

    private static string Codes(BpmnEditResult result) =>
        string.Join(", ", result.Rejections.Select(rejection => $"{rejection.Code}: {rejection.Message}"));

    private static BpmnShape ShapeOf(BpmnDocument document, string id) => document.Diagrams
        .SelectMany(diagram => diagram.Plane.Shapes)
        .Single(shape => shape.BpmnElement == id);

    private static BpmnDocument Collaboration() => new BpmnDocumentBuilder()
        .Pool("Pool_1", "Customer", new Bounds(40, 40, 900, 400))
        .Node("Task_1", BpmnFlowNodeType.ServiceTask, new Bounds(200, 120, 100, 80), "Validate", poolId: "Pool_1")
        .Build();

    private static BpmnDocument Gatewayed() => new BpmnDocumentBuilder()
        .Node("Gateway_1", BpmnFlowNodeType.ExclusiveGateway, new Bounds(200, 120, 50, 50), "Approved?")
        .Node("Task_Yes", BpmnFlowNodeType.Task, new Bounds(340, 100, 100, 80), "Ship")
        .Node("Task_No", BpmnFlowNodeType.Task, new Bounds(340, 240, 100, 80), "Reject")
        .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(520, 130, 36, 36), "Done")
        .Flow("Flow_In", "Task_No", "Task_Yes", [new DiPoint(440, 280), new DiPoint(390, 180)])
        .Flow("Flow_Yes", "Gateway_1", "Task_Yes", [new DiPoint(250, 145), new DiPoint(340, 140)], "yes")
        .Flow("Flow_No", "Gateway_1", "Task_No", [new DiPoint(225, 170), new DiPoint(340, 280)], "no")
        .Build();
}
