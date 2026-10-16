using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components;

namespace Bpmn.Demo.Client.Pages;

/// <summary>A named notation sheet built from a hand-written model.</summary>
/// <param name="Title">Invariant sheet identifier used as the option value.</param>
/// <param name="TitleResourceKey">Resource key describing what the sheet shows.</param>
/// <param name="Build">Builds the document, so a sheet is only constructed when selected.</param>
public sealed record GallerySheet(string Title, string TitleResourceKey, Func<BpmnDocument> Build);

/// <summary>
/// Renders every notation combination the specification defines, from hand-built models.
/// </summary>
/// <remarks>
/// This is the M1 exit criterion made visible: all event trigger and context pairs, every gateway,
/// every activity marker combination, both Pool orientations with zero or more Lanes, all
/// connector styles and all artifacts. Sample files exercise whatever their author happened to
/// draw; this covers the legal matrix deliberately, leaving unsupported cells blank.
/// </remarks>
public sealed partial class Gallery : ComponentBase
{
    private const double Column = 130d;
    private const double Row = 120d;

    private FluentBpmnEditor? _editor;
    private BpmnDocument? _document;
    private GallerySheet _sheet = null!;
    private string _status = string.Empty;

    private static GallerySheet[] Sheets { get; } =
    [
        new("Events: all triggers by context", "Gallery.Events", BuildEventMatrix),
        new("Gateways", "Gallery.Gateways", BuildGateways),
        new("Activities and markers", "Gallery.Activities", BuildActivities),
        new("Connectors and artifacts", "Gallery.Connectors", BuildConnectors),
        new("Pools and Lanes: both orientations", "Gallery.Swimlanes", BuildSwimlanes),
    ];

    protected override void OnInitialized()
    {
        _sheet = Sheets[0];
        _document = _sheet.Build();
    }

    /// <summary>
    /// Shows a sheet, identified by its invariant title.
    /// </summary>
    /// <remarks>
    /// Fluent UI 5's select binds an option's value rather than the option itself. Invariant
    /// identifiers remain stable values when the displayed titles change culture.
    /// </remarks>
    private Task ShowAsync(string title)
    {
        var sheet = Sheets.FirstOrDefault(candidate => candidate.Title == title);
        if (sheet is null) return Task.CompletedTask;

        _sheet = sheet;
        _document = sheet.Build();

        return Task.CompletedTask;
    }

    private void OnDocumentLoaded(DocumentLoaded payload) =>
        _status = Localizer["Gallery.Rendered", payload.ShapeCount, payload.EdgeCount];

    // --- sheets -------------------------------------------------------------

    /// <summary>
    /// Every event trigger in every context it is legal in (specification section 1.1).
    /// </summary>
    /// <remarks>
    /// Rows are contexts and columns are triggers, so a missing glyph or a wrong ring shows up as
    /// a break in an otherwise regular grid — much easier to spot than in a realistic diagram.
    /// </remarks>
    private static BpmnDocument BuildEventMatrix()
    {
        var builder = new BpmnDocumentBuilder("Process_Events", "Event matrix");
        var contextsDocument = new BpmnDocumentBuilder("Process_Events")
            .Node("EventSub_Context", BpmnFlowNodeType.EventSubProcess, new Bounds(0, 0, 60, 96), isExpanded: true)
            .Node("Host_Context", BpmnFlowNodeType.Task, new Bounds(0, 0, 56, 72))
            .Build();

        (string Label, BpmnFlowNodeType Type, bool Interrupting)[] contexts =
        [
            ("Start", BpmnFlowNodeType.StartEvent, true),
            ("Event sub-process start (non-interrupting)", BpmnFlowNodeType.StartEvent, false),
            ("Intermediate catch", BpmnFlowNodeType.IntermediateCatchEvent, true),
            ("Boundary", BpmnFlowNodeType.BoundaryEvent, true),
            ("Boundary (non-interrupting)", BpmnFlowNodeType.BoundaryEvent, false),
            ("Intermediate throw", BpmnFlowNodeType.IntermediateThrowEvent, true),
            ("End", BpmnFlowNodeType.EndEvent, true),
        ];

        BpmnEventDefinitionType[] triggers =
        [
            BpmnEventDefinitionType.None,
            BpmnEventDefinitionType.Message,
            BpmnEventDefinitionType.Timer,
            BpmnEventDefinitionType.Conditional,
            BpmnEventDefinitionType.Signal,
            BpmnEventDefinitionType.Multiple,
            BpmnEventDefinitionType.ParallelMultiple,
            BpmnEventDefinitionType.Link,
            BpmnEventDefinitionType.Error,
            BpmnEventDefinitionType.Escalation,
            BpmnEventDefinitionType.Cancel,
            BpmnEventDefinitionType.Compensation,
            BpmnEventDefinitionType.Terminate,
        ];

        for (var row = 0; row < contexts.Length; row++)
        {
            var (label, type, interrupting) = contexts[row];
            builder.TextAnnotation($"Row_{row}", label, new Bounds(0, row * Row + 10, 190, 40));

            for (var column = 0; column < triggers.Length; column++)
            {
                var trigger = triggers[column];
                var isEventSubStart = type == BpmnFlowNodeType.StartEvent && !interrupting;
                var candidate = new BpmnFlowNode(
                    $"Event_{row}_{column}",
                    type,
                    isEventSubStart ? "EventSub_Context" : "Process_Events",
                    EventDefinition: trigger,
                    IsInterrupting: interrupting,
                    AttachedToRef: type == BpmnFlowNodeType.BoundaryEvent ? "Host_Context" : null);
                if (!BpmnElementInspector.TriggersFor(contextsDocument, candidate).Contains(trigger)) continue;

                var x = 220 + column * 70;
                var y = row * Row;
                string? containerId = null;
                string? hostId = null;

                if (isEventSubStart)
                {
                    containerId = $"EventSub_{column}";
                    builder.Node(containerId, BpmnFlowNodeType.EventSubProcess,
                        new Bounds(x - 10, y + 8, 60, 96), isExpanded: true);
                }
                else if (type == BpmnFlowNodeType.BoundaryEvent)
                {
                    hostId = $"Host_{row}_{column}";
                    builder.Node(hostId, BpmnFlowNodeType.Task, new Bounds(x - 10, y + 8, 56, 72));
                }

                builder.Node(
                    candidate.Id,
                    type,
                    new Bounds(x, y + (hostId is null ? 20 : 62), 36, 36),
                    name: trigger.ToString(),
                    eventDefinition: trigger,
                    isInterrupting: interrupting,
                    attachedToRef: hostId,
                    containerId: containerId);
            }
        }

        return builder.Build("Diagram_Events", "Events");
    }

    /// <summary>Every Gateway type (specification section 1.3, section 4.1).</summary>
    private static BpmnDocument BuildGateways()
    {
        var builder = new BpmnDocumentBuilder("Process_Gateways", "Gateways");

        (string Name, BpmnFlowNodeType Type)[] gateways =
        [
            ("Exclusive", BpmnFlowNodeType.ExclusiveGateway),
            ("Inclusive", BpmnFlowNodeType.InclusiveGateway),
            ("Parallel", BpmnFlowNodeType.ParallelGateway),
            ("Complex", BpmnFlowNodeType.ComplexGateway),
            ("Event-based", BpmnFlowNodeType.EventBasedGateway),
        ];

        for (var index = 0; index < gateways.Length; index++)
        {
            var (name, type) = gateways[index];
            builder.Node($"Gateway_{index}", type, new Bounds(60 + index * Column, 60, 50, 50), name);
        }

        return builder.Build("Diagram_Gateways", "Gateways");
    }

    /// <summary>
    /// Every Task subtype, container kind, and bottom-marker combination
    /// (specification sections 1.2, 4.3 and 4.4).
    /// </summary>
    private static BpmnDocument BuildActivities()
    {
        var builder = new BpmnDocumentBuilder("Process_Activities", "Activities");

        (string Name, BpmnFlowNodeType Type)[] tasks =
        [
            ("Task", BpmnFlowNodeType.Task),
            ("User", BpmnFlowNodeType.UserTask),
            ("Manual", BpmnFlowNodeType.ManualTask),
            ("Service", BpmnFlowNodeType.ServiceTask),
            ("Script", BpmnFlowNodeType.ScriptTask),
            ("Business rule", BpmnFlowNodeType.BusinessRuleTask),
            ("Send", BpmnFlowNodeType.SendTask),
            ("Receive", BpmnFlowNodeType.ReceiveTask),
            ("Call activity", BpmnFlowNodeType.CallActivity),
        ];

        for (var index = 0; index < tasks.Length; index++)
        {
            var (name, type) = tasks[index];
            builder.Node($"Task_{index}", type, new Bounds(40 + index * Column, 40, 100, 80), name);
        }

        (string Name, BpmnLoopCharacteristics Loop, bool Compensation)[] markers =
        [
            ("Standard loop", BpmnLoopCharacteristics.Standard, false),
            ("MI parallel", BpmnLoopCharacteristics.MultiInstanceParallel, false),
            ("MI sequential", BpmnLoopCharacteristics.MultiInstanceSequential, false),
            ("Compensation", BpmnLoopCharacteristics.None, true),
            ("Compensation + loop", BpmnLoopCharacteristics.Standard, true),
            ("Compensation + MI", BpmnLoopCharacteristics.MultiInstanceParallel, true),
        ];

        for (var index = 0; index < markers.Length; index++)
        {
            var (name, loop, compensation) = markers[index];
            builder.Node(
                $"Marked_{index}",
                BpmnFlowNodeType.Task,
                new Bounds(40 + index * Column, 180, 100, 80),
                name,
                loop: loop,
                isForCompensation: compensation);
        }

        // Containers, including the combinations that stack several markers at once.
        builder
            .Node("Sub_Collapsed", BpmnFlowNodeType.SubProcess, new Bounds(40, 320, 100, 80), "Collapsed", isExpanded: false)
            .Node("Sub_CollapsedLoop", BpmnFlowNodeType.SubProcess, new Bounds(40 + Column, 320, 100, 80), "Collapsed + loop", loop: BpmnLoopCharacteristics.Standard, isExpanded: false)
            .Node("Sub_AdHoc", BpmnFlowNodeType.AdHocSubProcess, new Bounds(40 + Column * 2, 320, 100, 80), "Ad-hoc", isExpanded: false)
            .Node("Sub_AdHocAll", BpmnFlowNodeType.AdHocSubProcess, new Bounds(40 + Column * 3, 320, 100, 80), "Ad-hoc, MI, compensation", loop: BpmnLoopCharacteristics.MultiInstanceParallel, isForCompensation: true, isExpanded: false)
            .Node("Sub_Transaction", BpmnFlowNodeType.Transaction, new Bounds(40 + Column * 4, 320, 100, 80), "Transaction", isExpanded: false)
            .Node("Sub_Event", BpmnFlowNodeType.EventSubProcess, new Bounds(40 + Column * 5, 320, 100, 80), "Event sub-process", isExpanded: false)
            .Node("Sub_Expanded", BpmnFlowNodeType.SubProcess, new Bounds(40, 440, 350, 200), "Expanded sub-process", isExpanded: true);

        return builder.Build("Diagram_Activities", "Activities");
    }

    /// <summary>Every connector style and artifact (specification sections 1.4, 1.6 and 4).</summary>
    private static BpmnDocument BuildConnectors()
    {
        var builder = new BpmnDocumentBuilder("Process_Connectors", "Connectors");

        builder
            .Node("C_Start", BpmnFlowNodeType.StartEvent, new Bounds(60, 60, 36, 36), "Start")
            .Node("C_Gateway", BpmnFlowNodeType.ExclusiveGateway, new Bounds(180, 53, 50, 50), "Choice")
            .Node("C_TaskA", BpmnFlowNodeType.Task, new Bounds(300, 38, 100, 80), "Plain flow")
            .Node("C_TaskB", BpmnFlowNodeType.Task, new Bounds(300, 160, 100, 80), "Conditional")
            .Node("C_TaskC", BpmnFlowNodeType.Task, new Bounds(300, 280, 100, 80), "Default")
            .Flow("C_Flow1", "C_Start", "C_Gateway", [new DiPoint(96, 78), new DiPoint(180, 78)])
            .Flow("C_Flow2", "C_Gateway", "C_TaskA", [new DiPoint(230, 78), new DiPoint(300, 78)], "plain")
            .Flow("C_Flow3", "C_Gateway", "C_TaskB", [new DiPoint(205, 103), new DiPoint(205, 200), new DiPoint(300, 200)], "conditional", condition: "${amount > 100}")
            .Flow("C_Flow4", "C_Gateway", "C_TaskC", [new DiPoint(205, 103), new DiPoint(205, 320), new DiPoint(300, 320)], "default", isDefault: true);

        // Artifacts and data, with the associations that connect them.
        builder
            .TextAnnotation("C_Note", "Text annotation, associated below", new Bounds(480, 38, 160, 50))
            .Association("C_Assoc", "C_TaskA", "C_Note", [new DiPoint(400, 68), new DiPoint(480, 63)])
            .DataObject("C_Data", "Data object", new Bounds(520, 170, 36, 50))
            .DataObject("C_Collection", "Collection", new Bounds(600, 170, 36, 50), isCollection: true)
            .DataStore("C_Store", "Data store", new Bounds(680, 170, 50, 50))
            .Association("C_DataAssoc", "C_TaskB", "C_Data", [new DiPoint(400, 200), new DiPoint(520, 195)], BpmnAssociationDirection.One)
            .Group("C_Group", "Group", new Bounds(460, 260, 300, 140));

        // A second Pool, so the Message Flow crosses a boundary as MF-002 requires.
        builder
            .Pool("C_Pool", "Collaboration pool", new Bounds(40, 420, 760, 140))
            .Node("C_Receive", BpmnFlowNodeType.IntermediateCatchEvent, new Bounds(300, 470, 36, 36), "Receive", eventDefinition: BpmnEventDefinitionType.Message)
            .MessageFlow("C_Msg", "C_TaskC", "C_Receive", [new DiPoint(350, 360), new DiPoint(318, 470)], "message");

        return builder.Build("Diagram_Connectors", "Connectors");
    }

    /// <summary>
    /// Both Pool orientations, with and without Lanes (specification sections 1.5 and 10.4).
    /// </summary>
    /// <remarks>
    /// The lane-less Pools matter: <c>Process.laneSet</c> has cardinality 0..*, so a Pool with no
    /// Lane at all is valid and must never have one synthesised for it (section 9.1).
    /// </remarks>
    private static BpmnDocument BuildSwimlanes()
    {
        var builder = new BpmnDocumentBuilder("Process_Lanes", "Swimlanes");
        builder
            .Process("Process_Vertical", "Vertical swimlanes")
            .Process("Process_NoLanes", "Lane-less process");

        builder
            .Pool("S_Horizontal", "Horizontal pool, two lanes", new Bounds(40, 40, 600, 220))
            .Lane("S_LaneA", "Picking", new Bounds(70, 40, 570, 110), flowNodeRefs: ["S_TaskA"])
            .Lane("S_LaneB", "Packing", new Bounds(70, 150, 570, 110), flowNodeRefs: ["S_TaskB"])
            .Node("S_TaskA", BpmnFlowNodeType.UserTask, new Bounds(180, 55, 100, 80), "Pick items")
            .Node("S_TaskB", BpmnFlowNodeType.ServiceTask, new Bounds(180, 165, 100, 80), "Pack items");

        // A vertical Pool: the header band moves to the top edge and its label is not rotated.
        builder
            .Pool("S_Vertical", "Vertical pool", new Bounds(700, 40, 220, 400), isHorizontal: false, processRef: "Process_Vertical")
            .Lane("S_LaneC", "Left", new Bounds(700, 70, 110, 370), flowNodeRefs: ["S_TaskC"], isHorizontal: false, laneSetId: "LaneSet_2", containerId: "Process_Vertical")
            .Lane("S_LaneD", "Right", new Bounds(810, 70, 110, 370), flowNodeRefs: ["S_TaskD"], isHorizontal: false, laneSetId: "LaneSet_2", containerId: "Process_Vertical")
            .Node("S_TaskC", BpmnFlowNodeType.Task, new Bounds(705, 180, 100, 80), "Left task", containerId: "Process_Vertical")
            .Node("S_TaskD", BpmnFlowNodeType.Task, new Bounds(815, 180, 100, 80), "Right task", containerId: "Process_Vertical");

        // Lane-less Pools, both orientations.
        builder
            .Pool("S_NoLanes", "Lane-less pool", new Bounds(40, 300, 600, 140), processRef: "Process_NoLanes")
            .Node("S_TaskE", BpmnFlowNodeType.Task, new Bounds(180, 330, 100, 80), "No lane needed", containerId: "Process_NoLanes")
            .BlackBoxPool("S_BlackBox", "Black-box pool (no process)", new Bounds(40, 480, 600, 90));

        return builder.Build("Diagram_Swimlanes", "Swimlanes");
    }
}
