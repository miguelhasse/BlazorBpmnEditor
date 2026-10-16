using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Validation;

/// <summary>
/// The document validation engine (specification sections 5.2 and 5.5).
/// </summary>
/// <remarks>
/// One test per rule, keyed by the rule's id rather than by its wording — section 5.3 exists so a
/// finding can be referred to from a tooltip, a test and a document without depending on how it is
/// phrased, and a test that asserted the message would break every time the message improved.
/// </remarks>
public sealed class ValidatorTests
{
    // --- events ---------------------------------------------------------------------

    [Fact]
    public void D001FindsAStartEventWithAnIncomingFlow()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 90, 100, 80))
            .Flow("Flow_Back", "Task_1", "Start_1", [new DiPoint(200, 130), new DiPoint(116, 118)])
            .Build();

        Assert.Contains("D001", Codes(document));
    }

    [Fact]
    public void D002FindsAnEndEventWithAnOutgoingFlow()
    {
        var document = new BpmnDocumentBuilder()
            .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(80, 100, 36, 36))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 90, 100, 80))
            .Flow("Flow_On", "End_1", "Task_1", [new DiPoint(116, 118), new DiPoint(200, 130)])
            .Build();

        Assert.Contains("D002", Codes(document));
    }

    [Fact]
    public void D003FindsABoundaryEventAttachedToNothing()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 90, 100, 80))
            .Build();

        var stranded = document with
        {
            FlowNodes = [.. document.FlowNodes, new BpmnFlowNode(
                "Boundary_1",
                BpmnFlowNodeType.BoundaryEvent,
                "Process_1",
                EventDefinition: BpmnEventDefinitionType.Timer)],
        };

        Assert.Contains("D003", Codes(stranded));
    }

    [Fact]
    public void D020FindsATriggerTheEventCannotCarry()
    {
        // A Timer cannot be thrown: an End Event does not wait.
        var document = new BpmnDocumentBuilder()
            .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(80, 100, 36, 36), eventDefinition: BpmnEventDefinitionType.Timer)
            .Build();

        Assert.Contains("D020", Codes(document));
    }

    [Fact]
    public void D011FindsAnUnpairedLink()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Throw_1", BpmnFlowNodeType.IntermediateThrowEvent, new Bounds(200, 100, 36, 36), "Retry", BpmnEventDefinitionType.Link)
            .Build();

        Assert.Contains("D011", Codes(document));
    }

    [Fact]
    public void D011AcceptsSeveralThrowsIntoOneCatch()
    {
        // Several sources, one target: the specification allows exactly this.
        var document = new BpmnDocumentBuilder()
            .Node("Throw_1", BpmnFlowNodeType.IntermediateThrowEvent, new Bounds(200, 100, 36, 36), "Retry", BpmnEventDefinitionType.Link)
            .Node("Throw_2", BpmnFlowNodeType.IntermediateThrowEvent, new Bounds(200, 200, 36, 36), "Retry", BpmnEventDefinitionType.Link)
            .Node("Catch_1", BpmnFlowNodeType.IntermediateCatchEvent, new Bounds(400, 100, 36, 36), "Retry", BpmnEventDefinitionType.Link)
            .Build();

        Assert.DoesNotContain("D011", Codes(document));
    }

    [Fact]
    public void SF017FindsAnOutgoingFlowFromALinkThrow()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Throw_1", BpmnFlowNodeType.IntermediateThrowEvent, new Bounds(80, 100, 36, 36), "Retry", BpmnEventDefinitionType.Link)
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 90, 100, 80))
            .Flow("Flow_On", "Throw_1", "Task_1", [new DiPoint(116, 118), new DiPoint(200, 130)])
            .Build();

        Assert.Contains("SF-017", Codes(document));
    }

    [Fact]
    public void SF018FindsAnIncomingFlowToALinkCatch()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(80, 90, 100, 80))
            .Node("Catch_1", BpmnFlowNodeType.IntermediateCatchEvent, new Bounds(300, 100, 36, 36), "Retry", BpmnEventDefinitionType.Link)
            .Flow("Flow_In", "Task_1", "Catch_1", [new DiPoint(180, 130), new DiPoint(300, 118)])
            .Build();

        Assert.Contains("SF-018", Codes(document));
    }

    [Fact]
    public void LinkEventsAcceptFlowIntoTheThrowAndOutOfTheCatch()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(80, 90, 100, 80))
            .Node("Throw_1", BpmnFlowNodeType.IntermediateThrowEvent, new Bounds(300, 100, 36, 36), "Retry", BpmnEventDefinitionType.Link)
            .Node("Catch_1", BpmnFlowNodeType.IntermediateCatchEvent, new Bounds(80, 300, 36, 36), "Retry", BpmnEventDefinitionType.Link)
            .Node("Task_2", BpmnFlowNodeType.Task, new Bounds(200, 290, 100, 80))
            .Flow("Flow_In", "Task_1", "Throw_1", [new DiPoint(180, 130), new DiPoint(300, 118)])
            .Flow("Flow_Out", "Catch_1", "Task_2", [new DiPoint(116, 318), new DiPoint(200, 330)])
            .Build();

        var codes = Codes(document);
        Assert.DoesNotContain("SF-017", codes);
        Assert.DoesNotContain("SF-018", codes);
    }

    // --- gateways --------------------------------------------------------------------

    [Fact]
    public void D004FindsASplitThatDecidesNothing()
    {
        var document = Gatewayed();

        Assert.Contains("D004", Codes(document));
    }

    [Fact]
    public void D004DoesNotTreatFlowNamesAsMachineReadableConditions()
    {
        var document = Gatewayed();
        var labelled = document with
        {
            SequenceFlows =
            [
                .. document.SequenceFlows.Select(flow => flow with
                {
                    Name = flow.Id == "Flow_Yes" ? "Yes" : "No",
                }),
            ],
        };

        Assert.Contains("D004", Codes(labelled));
    }

    [Fact]
    public void D021FindsMissingTargetNamespaceAndUnboundCallQNamePrefix()
    {
        var document = Gatewayed();
        var missingNamespace = document with
        {
            Definitions = document.Definitions with { TargetNamespace = string.Empty },
        };
        Assert.Contains("D021", Codes(missingNamespace));

        var unresolvedQName = document with
        {
            FlowNodes =
            [
                .. document.FlowNodes.Select(node => node.Id == "Task_Yes"
                    ? node with
                    {
                        Type = BpmnFlowNodeType.CallActivity,
                        CalledElementQName = new BpmnQName("Process_1", null, "missing:Process_1"),
                    }
                    : node),
            ],
        };
        Assert.Contains("D021", Codes(unresolvedQName));

        var malformedExpressionType = document with
        {
            FlowNodes =
            [
                .. document.FlowNodes.Select(node => node.Id == "Gateway_1"
                    ? node with
                    {
                        Type = BpmnFlowNodeType.ComplexGateway,
                        ActivationCondition = new BpmnFormalExpression(
                            "ready",
                            TypeQName: "bpmn:tFormalExpression:extra")
                        {
                            TypeNamespaceUri = "http://www.omg.org/spec/BPMN/20100524/MODEL",
                        },
                    }
                    : node),
            ],
        };
        Assert.Contains("D021", Codes(malformedExpressionType));
    }

    [Fact]
    public void D004AcceptsASplitWithADefault()
    {
        var document = Gatewayed();
        var withDefault = document with
        {
            SequenceFlows = [.. document.SequenceFlows.Select(flow => flow.Id == "Flow_No"
                ? flow with { IsDefault = true }
                : flow)],
        };

        Assert.DoesNotContain("D004", Codes(withDefault));
    }

    [Fact]
    public void D004RejectsMultipleDefaultFlowsEvenWhenNoBranchIsUnconditional()
    {
        var document = Gatewayed();
        var multipleDefaults = document with
        {
            SequenceFlows = [.. document.SequenceFlows.Select(flow => flow.SourceRef == "Gateway_1"
                ? flow with { IsDefault = true, ConditionExpression = "${ready}" }
                : flow)],
        };

        Assert.Contains("D004", Codes(multipleDefaults));
    }

    [Fact]
    public void D004AcceptsASplitWhereEveryBranchIsConditional()
    {
        var document = Gatewayed();
        var conditioned = document with
        {
            SequenceFlows = [.. document.SequenceFlows.Select(flow => flow.SourceRef == "Gateway_1"
                ? flow with { ConditionExpression = "${ok}" }
                : flow)],
        };

        Assert.DoesNotContain("D004", Codes(conditioned));
    }

    [Fact]
    public void D005WarnsAboutAGatewayThatMergesOneFlow()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("Gateway_1", BpmnFlowNodeType.ParallelGateway, new Bounds(260, 115, 50, 50))
            .Node("Task_2", BpmnFlowNodeType.Task, new Bounds(380, 100, 100, 80))
            .Flow("Flow_1", "Task_1", "Gateway_1", [new DiPoint(200, 140), new DiPoint(260, 140)])
            .Flow("Flow_2", "Gateway_1", "Task_2", [new DiPoint(310, 140), new DiPoint(380, 140)])
            .Build();

        var report = BpmnValidator.Validate(document);

        Assert.Contains(report.Diagnostics, d => d.RuleId == "D005" && d.Severity == BpmnSeverity.Warning);

        // Advisory, so the document still exports.
        Assert.True(report.IsExportable);
    }

    [Fact]
    public void D006FindsAnEventGatewayBranchingToATask()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Gateway_1", BpmnFlowNodeType.EventBasedGateway, new Bounds(200, 115, 50, 50))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(340, 100, 100, 80))
            .Flow("Flow_1", "Gateway_1", "Task_1", [new DiPoint(250, 140), new DiPoint(340, 140)])
            .Build();

        Assert.Contains("D006", Codes(document));
    }

    [Fact]
    public void D014FindsAnEventGatewayWithMixedTargets()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Gateway_1", BpmnFlowNodeType.EventBasedGateway, new Bounds(200, 115, 50, 50))
            .Node("Catch_1", BpmnFlowNodeType.IntermediateCatchEvent, new Bounds(340, 100, 36, 36), eventDefinition: BpmnEventDefinitionType.Message)
            .Node("Receive_1", BpmnFlowNodeType.ReceiveTask, new Bounds(340, 200, 100, 80))
            .Flow("Flow_1", "Gateway_1", "Catch_1", [new DiPoint(250, 140), new DiPoint(340, 118)])
            .Flow("Flow_2", "Gateway_1", "Receive_1", [new DiPoint(250, 140), new DiPoint(340, 240)])
            .Build();

        Assert.Contains("D014", Codes(document));
    }

    // --- scope ------------------------------------------------------------------------

    [Fact]
    public void D007FindsASequenceFlowAcrossPools()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool_1", "Customer", new Bounds(0, 0, 600, 250))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 80, 100, 80), poolId: "Pool_1")
            .Node("Task_2", BpmnFlowNodeType.Task, new Bounds(100, 400, 100, 80), poolId: "Pool_2")
            .Flow("Flow_1", "Task_1", "Task_2", [new DiPoint(150, 160), new DiPoint(150, 400)])
            .Build();

        Assert.Contains("D007", Codes(document));
    }

    [Fact]
    public void SF007FindsASequenceFlowLeavingASubProcess()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Sub_1", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 300, 200))
            .Node("Inner_1", BpmnFlowNodeType.Task, new Bounds(140, 140, 100, 80), containerId: "Sub_1")
            .Node("Outer_1", BpmnFlowNodeType.Task, new Bounds(500, 140, 100, 80))
            .Flow("Flow_1", "Inner_1", "Outer_1", [new DiPoint(240, 180), new DiPoint(500, 180)])
            .Build();

        Assert.Contains("SF-007", Codes(document));
    }

    [Fact]
    public void D008FindsAMessageFlowInsideOnePool()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool_1", "Customer", new Bounds(0, 0, 600, 250))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 80, 100, 80), poolId: "Pool_1")
            .Node("Task_2", BpmnFlowNodeType.Task, new Bounds(300, 80, 100, 80), poolId: "Pool_1")
            .MessageFlow("MF_1", "Task_1", "Task_2", [new DiPoint(200, 120), new DiPoint(300, 120)])
            .Build();

        Assert.Contains("D008", Codes(document));
    }

    [Fact]
    public void D019FindsANodeWithNoContainer()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80), containerId: "Process_Nonexistent")
            .Build();

        Assert.Contains("D019", Codes(document));
    }

    [Fact]
    public void D019FindsANodeClaimedByTwoLanes()
    {
        // Built by hand: the document builder refuses to put one node in two Lanes, which is the
        // defect being tested. Only an imported file or a bug gets a document into this state.
        var document = new BpmnDocumentBuilder()
            .Pool("Pool_1", "Customer", new Bounds(0, 0, 600, 250))
            .Lane("Lane_1", "Sales", new Bounds(30, 0, 570, 125), ["Task_1"])
            .Lane("Lane_2", "Support", new Bounds(30, 125, 570, 125), [])
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 30, 100, 80), poolId: "Pool_1")
            .Build();

        var contested = document with
        {
            Lanes = [.. document.Lanes.Select(lane => lane.Id == "Lane_2"
                ? lane with { FlowNodeRefs = ["Task_1"] }
                : lane)],
        };

        Assert.Contains("D019", Codes(contested));
    }

    // --- references -------------------------------------------------------------------

    [Fact]
    public void D017FindsAReferenceThatLandsNowhere()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();

        var dangling = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node with { DefaultFlowRef = "Flow_Imaginary" })],
        };

        Assert.Contains("D017", Codes(dangling));
    }

    [Fact]
    public void D018FindsADuplicateId()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();

        var duplicated = document with
        {
            TextAnnotations = [new BpmnTextAnnotation("Task_1", "note", "Process_1")],
        };

        Assert.Contains("D018", Codes(duplicated));
    }

    [Fact]
    public void D018IncludesDefinitionsCatalogAndEventDefinitionIds()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();

        var duplicateDefinitionsId = document with
        {
            Definitions = document.Definitions with { Id = "Task_1" },
        };
        var duplicateCatalogId = document with
        {
            Catalog = document.Catalog with { Messages = [new BpmnMessage("Task_1")] },
        };
        var invalidEventDefinitionId = document with
        {
            FlowNodes =
            [
                document.FlowNodes[0] with
                {
                    EventDefinitions = [new BpmnEventDefinition(BpmnEventDefinitionType.Message, Id: "bad id")],
                },
            ],
        };

        Assert.Contains("D018", Codes(duplicateDefinitionsId));
        Assert.Contains("D018", Codes(duplicateCatalogId));
        Assert.Contains("D018", Codes(invalidEventDefinitionId));
    }

    [Fact]
    public void D018IncludesDiagramAndShapeIds()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build() with
        {
            Diagrams =
            [
                new BpmnDiagram(
                    "Diagram_1",
                    new BpmnPlane(
                        "Plane_1",
                        "Process_1",
                        [new BpmnShape("Task_1", "Task_1", new Bounds(100, 100, 100, 80))],
                        [])),
            ],
        };

        Assert.Contains("D018", Codes(document));
    }

    [Fact]
    public void D018FindsAnInvalidNestedDataItemId()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();
        var invalid = document with
        {
            FlowNodes =
            [
                document.FlowNodes[0] with
                {
                    MultiInstanceLoop = new BpmnMultiInstanceLoopDetails(
                        InputDataItem: new BpmnLoopDataItem(Id: "input item")),
                },
            ],
        };

        Assert.Contains("D018", Codes(invalid));
    }

    [Fact]
    public void D023FindsACallActivityCallingNothing()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Call_1", BpmnFlowNodeType.CallActivity, new Bounds(100, 100, 100, 80))
            .Build();

        // The builder points every Call Activity at GlobalProcess_1, which this document has not got.
        Assert.Contains("D023", Codes(document));
    }

    // --- semantics ---------------------------------------------------------------------

    [Fact]
    public void D010FindsAnEventSubProcessWithAnUntypedStart()
    {
        var document = new BpmnDocumentBuilder()
            .Node("EventSub_1", BpmnFlowNodeType.EventSubProcess, new Bounds(100, 100, 300, 200))
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(140, 160, 36, 36), containerId: "EventSub_1")
            .Build();

        Assert.Contains("D010", Codes(document));
    }

    [Fact]
    public void D010FindsAFlowCrossingAnEventSubProcessBoundary()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 400, 100, 80))
            .Node("EventSub_1", BpmnFlowNodeType.EventSubProcess, new Bounds(100, 100, 300, 200))
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(140, 160, 36, 36), eventDefinition: BpmnEventDefinitionType.Error, containerId: "EventSub_1")
            .Flow("Flow_1", "Task_1", "EventSub_1", [new DiPoint(150, 400), new DiPoint(150, 300)])
            .Build();

        Assert.Contains("D010", Codes(document));
    }

    [Fact]
    public void D009WarnsAboutAnUnreachableNode()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 90, 100, 80))
            .Node("Orphan_1", BpmnFlowNodeType.Task, new Bounds(200, 300, 100, 80))
            .Flow("Flow_1", "Start_1", "Task_1", [new DiPoint(116, 118), new DiPoint(200, 130)])
            .Build();

        var report = BpmnValidator.Validate(document);

        Assert.Contains(report.Diagnostics, d => d.RuleId == "D009" && d.ElementId == "Orphan_1");
        Assert.True(report.IsExportable, "reachability is advisory, so it must not block export");
    }

    [Fact]
    public void D009FollowsABoundaryEventsOwnOutgoingFlow()
    {
        // A node reached only through a Boundary Event's escape path is reachable.
        var document = new BpmnDocumentBuilder()
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 90, 100, 80))
            .Node("Boundary_1", BpmnFlowNodeType.BoundaryEvent, new Bounds(280, 152, 36, 36), eventDefinition: BpmnEventDefinitionType.Timer, attachedToRef: "Task_1")
            .Node("Task_Late", BpmnFlowNodeType.Task, new Bounds(400, 250, 100, 80))
            .Flow("Flow_1", "Start_1", "Task_1", [new DiPoint(116, 118), new DiPoint(200, 130)])
            .Flow("Flow_2", "Boundary_1", "Task_Late", [new DiPoint(298, 188), new DiPoint(400, 290)])
            .Build();

        Assert.DoesNotContain(
            BpmnValidator.Validate(document).Diagnostics,
            d => d.RuleId == "D009" && d.ElementId == "Task_Late");
    }

    // --- renderability -------------------------------------------------------------------

    [Fact]
    public void D015FindsAnEdgeWithOneWaypoint()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Node("Task_2", BpmnFlowNodeType.Task, new Bounds(300, 100, 100, 80))
            .Flow("Flow_1", "Task_1", "Task_2", [new DiPoint(200, 140)])
            .Build();

        Assert.Contains("D015", Codes(document));
    }

    [Fact]
    public void D015FindsAShapeWithNoArea()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 0, 80))
            .Build();

        Assert.Contains("D015", Codes(document));
    }

    [Fact]
    public void D015FindsOrphanedDi()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();

        var orphaned = document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes, new BpmnShape("Ghost_di", "Ghost_1", new Bounds(0, 0, 10, 10))],
                },
            })],
        };

        Assert.Contains("D015", Codes(orphaned));
    }

    [Fact]
    public void D022FindsAnElementDrawnTwiceOnOnePlane()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 100, 80))
            .Build();

        var twice = document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes, new BpmnShape("Task_1_di2", "Task_1", new Bounds(400, 100, 100, 80))],
                },
            })],
        };

        Assert.Contains("D022", Codes(twice));
    }

    // --- the report ------------------------------------------------------------------------

    [Fact]
    public void AnErrorBlocksExportAndAWarningDoesNot()
    {
        var clean = new BpmnDocumentBuilder()
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36))
            .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(300, 100, 36, 36))
            .Flow("Flow_1", "Start_1", "End_1", [new DiPoint(116, 118), new DiPoint(300, 118)])
            .Build();

        var report = BpmnValidator.Validate(clean);

        Assert.True(report.IsExportable, string.Join(", ", report.Diagnostics.Select(d => $"{d.RuleId}: {d.Message}")));
        Assert.Empty(report.Diagnostics);
    }

    [Fact]
    public void FindingsAreLookedUpByElement()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 100, 36, 36))
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 90, 100, 80))
            .Flow("Flow_Back", "Task_1", "Start_1", [new DiPoint(200, 130), new DiPoint(116, 118)])
            .Build();

        var report = BpmnValidator.Validate(document);

        // Both ends of a finding can be asked about it: a panel offers to go to either.
        Assert.NotEmpty(report.For("Start_1"));
        Assert.NotEmpty(report.For("Flow_Back"));
    }

    [Fact]
    public void EveryLayerRunsEvenWhenAnEarlierOneFails()
    {
        // Section 5.5: "retain all issues rather than stopping after the first failure".
        var document = new BpmnDocumentBuilder()
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 100, 0, 80), containerId: "Process_Nonexistent")
            .Build();

        var codes = Codes(document);

        Assert.Contains("D019", codes);
        Assert.Contains("D015", codes);
    }

    // --- helpers ---------------------------------------------------------------------------

    private static IReadOnlyList<string> Codes(BpmnDocument document) =>
        [.. BpmnValidator.Validate(document).Diagnostics.Select(diagnostic => diagnostic.RuleId)];

    private static BpmnDocument Gatewayed() => new BpmnDocumentBuilder()
        .Node("Gateway_1", BpmnFlowNodeType.ExclusiveGateway, new Bounds(200, 115, 50, 50))
        .Node("Task_Yes", BpmnFlowNodeType.Task, new Bounds(340, 60, 100, 80))
        .Node("Task_No", BpmnFlowNodeType.Task, new Bounds(340, 200, 100, 80))
        .Flow("Flow_Yes", "Gateway_1", "Task_Yes", [new DiPoint(250, 140), new DiPoint(340, 100)])
        .Flow("Flow_No", "Gateway_1", "Task_No", [new DiPoint(250, 140), new DiPoint(340, 240)])
        .Build();
}
