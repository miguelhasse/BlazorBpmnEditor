using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// What the stencil offers, per container.
/// </summary>
/// <remarks>
/// Section 9.1 tabulates this by container, and the interesting half of every row is what it
/// <em>excludes</em>. These tests assert the exclusions, because a palette that offers a creation
/// the editor will then refuse is worse than one that shows the tool greyed out with its reason.
/// </remarks>
public sealed class StencilTests
{
    [Fact]
    public void ClassifiesADocumentWithNeitherProcessNorCollaborationAsEmpty()
    {
        Assert.Equal(BpmnStencilContext.EmptyCanvas, BpmnStencil.ContextFor(Empty(), null));
    }

    [Fact]
    public void OffersNothingOnAnEmptyCanvas()
    {
        // Creating the root Process or Collaboration is its own action, not a Flow Node dropped
        // on the Definitions element.
        Assert.Empty(BpmnStencil.GroupsFor(Empty(), null));
    }

    [Fact]
    public void ClassifiesAProcessRootWithNoParticipantAsStandalone()
    {
        Assert.Equal(BpmnStencilContext.StandaloneProcess, BpmnStencil.ContextFor(Process(), null));
    }

    [Fact]
    public void OffersFlowNodesInAStandaloneProcess()
    {
        var groups = BpmnStencil.GroupsFor(Process(), null);

        Assert.Contains(groups, group => group.Title == "Events");
        Assert.Contains(groups, group => group.Title == "Activities");
        Assert.Contains(groups, group => group.Title == "Gateways");
    }

    [Fact]
    public void ClassifiesTheCollaborationPlaneSeparatelyFromItsProcesses()
    {
        Assert.Equal(BpmnStencilContext.CollaborationPlane, BpmnStencil.ContextFor(Pooled(), null));
    }

    [Fact]
    public void OffersPoolsAndArtifactsOnTheCollaborationPlaneAndNothingElse()
    {
        // A Pool/Lane tool resolves to Pool outside existing Pools and Lane inside one.
        var groups = BpmnStencil.GroupsFor(Pooled(), null);

        Assert.DoesNotContain(groups, group => group.Title is "Events" or "Activities" or "Gateways");
        Assert.Equal(["Swimlanes", "Artifacts"], groups.Select(group => group.Title));

        var swimlanes = groups[0].Entries;
        Assert.Equal("Pool / Lane", Assert.Single(swimlanes).Label);
        Assert.Equal(["Text Annotation", "Group"], groups[1].Entries.Select(entry => entry.Label));
    }

    [Fact]
    public void OffersOnePoolOrLaneToolWhereEitherCreationIsValid()
    {
        foreach (var context in (BpmnStencilContext[])
        [
            BpmnStencilContext.StandaloneProcess,
            BpmnStencilContext.PoolWithProcess,
            BpmnStencilContext.EmbeddedSubProcess,
        ])
        {
            var labels = LabelsFor(context);

            Assert.Contains("Pool / Lane", labels);
            Assert.DoesNotContain("Pool", labels);
            Assert.DoesNotContain("Lane", labels);
        }

        var unavailable = Assert.Single(BpmnStencil.GroupsFor(Process(), null)
            .SelectMany(group => group.Entries), entry => entry.Kind == BpmnStencilKind.PoolOrLane);
        Assert.False(unavailable.IsEnabled);
        Assert.Equal("CT-001", unavailable.DisabledReason);
    }

    [Fact]
    public void OffersOneAdaptiveIntermediateOrBoundaryEventWhereActivitiesCanBe()
    {
        foreach (var context in (BpmnStencilContext[])
        [
            BpmnStencilContext.StandaloneProcess,
            BpmnStencilContext.PoolWithProcess,
            BpmnStencilContext.EmbeddedSubProcess,
            BpmnStencilContext.EventSubProcess,
        ])
        {
            var adaptive = BpmnStencil.GroupsFor(context)
                .SelectMany(group => group.Entries)
                .Single(entry => entry.Kind == BpmnStencilKind.IntermediateBoundaryEvent);

            Assert.Equal(BpmnFlowNodeType.IntermediateThrowEvent, adaptive.Type);
            Assert.Equal(BpmnEventDefinitionType.None, adaptive.EventDefinition);
        }
    }

    [Fact]
    public void OffersTheTwelveRequestedToolsInAProcess()
    {
        var labels = BpmnStencil.GroupsFor(Hosted(), "Process_1")
            .SelectMany(group => group.Entries)
            .Select(entry => entry.Label)
            .ToList();

        Assert.Equal(
        [
            "Start Event",
            "Intermediate / Boundary Event",
            "End Event",
            "Task",
            "Sub-Process",
            "Call Activity",
            "Gateway",
            "Data Object Reference",
            "Data Store Reference",
            "Pool / Lane",
            "Text Annotation",
            "Group",
        ],
        labels);

        var callActivity = Assert.Single(
            BpmnStencil.GroupsFor(Hosted(), "Process_1")
                .SelectMany(group => group.Entries),
            entry => entry.Label == "Call Activity");
        Assert.Equal(BpmnStencilKind.CallActivity, callActivity.Kind);
        Assert.True(callActivity.NeedsChoice);
    }

    [Fact]
    public void OffersNoSwimlanesWhereNeitherBelongs()
    {
        Assert.Empty(BpmnStencil.GroupsFor(BpmnStencilContext.EmptyCanvas));
        Assert.Empty(BpmnStencil.GroupsFor(BpmnStencilContext.BlackBoxPool));
    }

    private static IReadOnlyList<string> LabelsFor(BpmnStencilContext context) =>
    [
        .. BpmnStencil.GroupsFor(context).SelectMany(group => group.Entries).Select(entry => entry.Label),
    ];

    [Fact]
    public void ClassifiesAPoolWithAProcessAsAPool()
    {
        Assert.Equal(
            BpmnStencilContext.PoolWithProcess,
            BpmnStencil.ContextFor(Pooled(), "Participant_1"));
    }

    [Fact]
    public void ClassifiesAPooledProcessByItsPoolRatherThanItsOwnIdentity()
    {
        // The available stencil follows the Pool, so naming the Process behind it must not produce
        // a different answer than naming the Participant.
        Assert.Equal(
            BpmnStencilContext.PoolWithProcess,
            BpmnStencil.ContextFor(Pooled(), "Process_1"));
    }

    [Fact]
    public void OffersFlowNodesInsideAPoolWithAProcess()
    {
        Assert.Contains(BpmnStencil.GroupsFor(Pooled(), "Participant_1"), group => group.Title == "Activities");
    }

    [Fact]
    public void ClassifiesAParticipantWithNoProcessAsBlackBox()
    {
        Assert.Equal(BpmnStencilContext.BlackBoxPool, BpmnStencil.ContextFor(BlackBox(), "Participant_1"));
    }

    [Fact]
    public void OffersNothingInsideABlackBoxPool()
    {
        // Section 2.6: a black-box Pool is a Message Flow endpoint with no internal content at
        // all, and stays one until a Process is assigned to it.
        Assert.Empty(BpmnStencil.GroupsFor(BlackBox(), "Participant_1"));
    }

    [Theory]
    [InlineData(BpmnFlowNodeType.SubProcess)]
    [InlineData(BpmnFlowNodeType.Transaction)]
    [InlineData(BpmnFlowNodeType.AdHocSubProcess)]
    public void ClassifiesEveryEmbeddedContainerTheSameWay(BpmnFlowNodeType type)
    {
        Assert.Equal(
            BpmnStencilContext.EmbeddedSubProcess,
            BpmnStencil.ContextFor(WithContainer(type), "Sub_1"));
    }

    [Fact]
    public void DoesNotTreatACollapsedSubProcessAsSomewhereToCreateInto()
    {
        // A collapsed Sub-Process renders as an opaque leaf and does not materialise its children,
        // so a new element inside it would have nowhere to be drawn. The palette follows whatever
        // holds it instead — here the plane's own Process.
        var document = Collapsed(BpmnFlowNodeType.SubProcess);

        Assert.False(BpmnStencil.IsOpenContainer(document, document.FlowNodes[0]));
        Assert.Equal(BpmnStencilContext.StandaloneProcess, BpmnStencil.ContextFor(document, "Sub_1"));
    }

    [Fact]
    public void TreatsASubProcessWithNoExplicitExpansionAsCollapsed()
    {
        /*
         * The specification states no default for isExpanded — section 10.4 is about isHorizontal
         * — so this is the editor's choice, and collapsed is the safe one: a file that omits the
         * flag and draws no children then renders as a Task-sized box with a "+" to press, rather
         * than a large empty rectangle with nothing to say where its content went. It is also what
         * the renderer, the embedding and the marker layer have always done.
         */
        var document = WithUnstatedContainer(BpmnFlowNodeType.SubProcess);

        Assert.False(BpmnStencil.IsOpenContainer(document, document.FlowNodes[0]));
        Assert.Equal(BpmnStencilContext.StandaloneProcess, BpmnStencil.ContextFor(document, "Sub_1"));
    }

    [Fact]
    public void AsksAboutThePlaneItIsGivenRatherThanAnyPlaneAtAll()
    {
        /*
         * The same Sub-Process can be collapsed where the user is looking and expanded on a
         * diagram they are not. Answering from whichever shape came first offered the palette of
         * a container whose inside is not on screen.
         */
        var document = OnTwoPlanes();

        Assert.False(BpmnStencil.IsOpenContainer(document, document.FlowNodes[0], "Diagram_collapsed"));
        Assert.True(BpmnStencil.IsOpenContainer(document, document.FlowNodes[0], "Diagram_expanded"));
    }

    [Fact]
    public void DoesNotTreatAPlainTaskAsSomewhereToCreateInto()
    {
        var document = Process();
        var task = new BpmnDocumentBuilder("Process_1")
            .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(100, 100, 100, 80))
            .Build();

        Assert.False(BpmnStencil.IsOpenContainer(task, task.FlowNodes[0]));

        // And naming one as the container resolves to whatever holds it rather than silently
        // offering the plane's palette for a place nothing can go.
        Assert.Equal(BpmnStencilContext.StandaloneProcess, BpmnStencil.ContextFor(document, "Event_1"));
    }

    [Fact]
    public void ClassifiesAnEventSubProcessSeparately()
    {
        Assert.Equal(
            BpmnStencilContext.EventSubProcess,
            BpmnStencil.ContextFor(WithContainer(BpmnFlowNodeType.EventSubProcess), "Sub_1"));
    }

    [Fact]
    public void ShowsTheUntypedStartEventDisabledInAnEventSubProcess()
    {
        var start = Assert.Single(StartEntriesFor(BpmnStencilContext.EventSubProcess),
            entry => entry.EventDefinition == BpmnEventDefinitionType.None);

        // EV-004: a None Start would leave the Event Sub-Process untriggerable. Shown with its
        // reason rather than hidden, so the user learns the constraint instead of guessing.
        Assert.False(start.IsEnabled);
        Assert.Equal("EV-004", start.DisabledReason);
    }

    [Fact]
    public void OffersNoTypedStartVariantsInAnEventSubProcess()
    {
        var typed = StartEntriesFor(BpmnStencilContext.EventSubProcess)
            .Where(entry => entry.EventDefinition != BpmnEventDefinitionType.None)
            .ToList();

        Assert.Empty(typed);
    }

    [Fact]
    public void DoesNotRepeatStartEventsInTheEventsGroupOfAnEventSubProcess()
    {
        // The constrained Start group already covers them; a second, unconstrained set would
        // reintroduce exactly the None Start the first group disabled.
        var events = Assert.Single(
            BpmnStencil.GroupsFor(BpmnStencilContext.EventSubProcess),
            group => group.Title == "Events");

        Assert.DoesNotContain(events.Entries, entry => entry.Type == BpmnFlowNodeType.StartEvent);
    }

    [Fact]
    public void OffersAStartEventInAnOrdinaryContainer()
    {
        var events = Assert.Single(
            BpmnStencil.GroupsFor(BpmnStencilContext.EmbeddedSubProcess),
            group => group.Title == "Events");

        Assert.Contains(events.Entries, entry => entry.Type == BpmnFlowNodeType.StartEvent);
    }

    [Fact]
    public void OrdinarySubProcessOffersGenericStartAndIntermediateEvents()
    {
        var entries = BpmnStencil.GroupsFor(BpmnStencilContext.EmbeddedSubProcess)
            .SelectMany(group => group.Entries).ToList();
        Assert.All(entries.Where(entry => entry.Type == BpmnFlowNodeType.StartEvent),
            entry => Assert.Equal(entry.EventDefinition == BpmnEventDefinitionType.None, entry.IsEnabled));
        var adaptive = Assert.Single(entries, entry => entry.Kind == BpmnStencilKind.IntermediateBoundaryEvent);
        Assert.Equal(BpmnFlowNodeType.IntermediateThrowEvent, adaptive.Type);
    }

    [Fact]
    public void OffersArtifactsAndDataAsTheirOwnKinds()
    {
        // Three commands behind one palette: an artifact is not a Flow Node, and a data reference
        // points at a definition that is never drawn.
        var entries = BpmnStencil.GroupsFor(BpmnStencilContext.StandaloneProcess)
            .SelectMany(group => group.Entries)
            .ToList();

        Assert.Contains(entries, entry => entry is { Kind: BpmnStencilKind.Artifact, Artifact: BpmnArtifactKind.Group });
        Assert.Contains(entries, entry => entry is { Kind: BpmnStencilKind.Data, Data: BpmnDataKind.DataObject });
        Assert.Contains(entries, entry => entry is { Kind: BpmnStencilKind.Data, Data: BpmnDataKind.DataStore });

        // And they are all usable now, which they were not when they had no commands behind them.
        Assert.All(
            entries.Where(entry => entry.Kind is BpmnStencilKind.Artifact or BpmnStencilKind.Data),
            entry => Assert.True(entry.IsEnabled));
    }

    [Fact]
    public void GivesEveryDisabledEntryAReason()
    {
        // The reason is the whole point of showing a disabled tool: an entry greyed out with no
        // code is indistinguishable from a bug in the editor.
        var disabled = Enum.GetValues<BpmnStencilContext>()
            .SelectMany(BpmnStencil.GroupsFor)
            .SelectMany(group => group.Entries)
            .Where(entry => !entry.IsEnabled);

        Assert.All(disabled, entry => Assert.False(string.IsNullOrWhiteSpace(entry.DisabledReason)));
    }

    [Fact]
    public void GivesEveryEnabledEntryACommandThatSucceeds()
    {
        /*
         * Every enabled entry has to work when used, or the palette is offering a tool that fails.
         * The Call Activity is the one exception and says so through NeedsChoice: it cannot be
         * created until the user names what it calls.
         */
        var enabled = Enum.GetValues<BpmnStencilContext>()
            .SelectMany(context => BpmnStencil.GroupsFor(context)
                .SelectMany(group => group.Entries)
                .Select(entry => (Context: context, Entry: entry)))
            .Where(item => item.Entry is { IsEnabled: true, NeedsChoice: false });

        Assert.All(enabled, item =>
        {
            var entry = item.Entry;
            /*
             * The combined Pool/Lane tool needs a Pool or a Collaboration, so judge it against a
             * document with both.
             */
            var document = entry.Kind is BpmnStencilKind.Participant or BpmnStencilKind.Lane or BpmnStencilKind.PoolOrLane
                ? Pooled()
                : Hosted();
            var containerId = "Process_1";
            if (item.Context is BpmnStencilContext.EventSubProcess or BpmnStencilContext.EmbeddedSubProcess)
            {
                containerId = "Container_1";
                var type = item.Context == BpmnStencilContext.EventSubProcess
                    ? BpmnFlowNodeType.EventSubProcess : BpmnFlowNodeType.SubProcess;
                document = document with
                {
                    FlowNodes = [.. document.FlowNodes, new BpmnFlowNode(
                        containerId, type, "Process_1", FlowElementsOwned: true)],
                };
            }
            var result = BpmnDocumentEditor.Apply(document, CommandFor(entry, containerId, item.Context));

            Assert.True(
                result.IsApplied,
                $"{entry.Label} was refused: {string.Join(", ", result.Rejections.Select(r => r.Code))}");
        });
    }

    /// <summary>The command a stencil entry stands for, mirroring what the editor builds.</summary>
    private static BpmnEditCommand CommandFor(
        BpmnStencilEntry entry,
        string containerId,
        BpmnStencilContext context) => entry.Kind switch
        {
            BpmnStencilKind.Participant => new CompoundCommand(
                [
                    new CreateProcessCommand("Process_New"),
                new CreateParticipantCommand(
                    BpmnId.New("Participant"),
                    new Bounds(0, 400, 600, 250),
                    "Pool",
                    "Process_New"),
            ],
                "Create Pool"),

            BpmnStencilKind.Lane => new CreateLaneCommand(BpmnId.New("Lane"), "Process_1", entry.Label),

            BpmnStencilKind.PoolOrLane when context == BpmnStencilContext.CollaborationPlane => new CompoundCommand(
                [
                    new CreateProcessCommand("Process_New"),
                new CreateParticipantCommand(
                    BpmnId.New("Participant"),
                    new Bounds(0, 400, 600, 250),
                    "Pool",
                    "Process_New"),
            ],
                "Create Pool"),

            BpmnStencilKind.PoolOrLane => new CreateLaneCommand(BpmnId.New("Lane"), "Process_1", "Lane"),

            // Dropped onto the host Activity's outline, which is what the editor resolves before it
            // builds the command (section 9.3).
            BpmnStencilKind.BoundaryEvent => new CreateElementCommand(
                BpmnId.New("Boundary"),
                entry.Type,
                "Process_1",
                new Bounds(302, 160, 36, 36),
                entry.Label,
                entry.EventDefinition,
                AttachedToRef: "Task_Host"),

            BpmnStencilKind.Artifact => new CreateArtifactCommand(
                BpmnId.New("Artifact"),
                entry.Artifact,
                "Process_1",
                new Bounds(800, 600, 120, 60),
                Text: entry.Label,
                Name: entry.Label),

            BpmnStencilKind.Data => new CreateDataReferenceCommand(
                BpmnId.New("DataReference"),
                entry.Data,
                "Process_1",
                new Bounds(800, 600, 36, 50),
                entry.Label,
                NewDefinitionId: BpmnId.New("Definition")),

            _ => new CreateElementCommand(
                BpmnId.New("Element"),
                entry.Type,
                containerId,
                new Bounds(800, 600, 100, 80),
                entry.Label,
                entry.EventDefinition),
        };

    [Fact]
    public void NamesEveryGroupAndEntry()
    {
        var groups = Enum.GetValues<BpmnStencilContext>().SelectMany(BpmnStencil.GroupsFor).ToList();

        Assert.All(groups, group => Assert.False(string.IsNullOrWhiteSpace(group.Title)));
        Assert.All(
            groups.SelectMany(group => group.Entries),
            entry => Assert.False(string.IsNullOrWhiteSpace(entry.Label)));
    }

    private static IReadOnlyList<BpmnStencilEntry> StartEntriesFor(BpmnStencilContext context) =>
        Assert.Single(BpmnStencil.GroupsFor(context), group => group.Title == "Start").Entries;

    private static BpmnDocument Empty() => new() { Definitions = new BpmnDefinitions("Definitions_1", "http://example.org/bpmn") };

    private static BpmnDocument Process() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 120, 36, 36), "Start")
        .Build();

    /// <summary>A Process with an Activity in it, so a Boundary Event has something to attach to.</summary>
    private static BpmnDocument Hosted() => new BpmnDocumentBuilder("Process_1")
        .Node("Event_1", BpmnFlowNodeType.StartEvent, new Bounds(80, 120, 36, 36), "Start")
        .Node("Task_Host", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Build();

    private static BpmnDocument Pooled() => new BpmnDocumentBuilder("Process_1")
        .Pool("Participant_1", "Customer", new Bounds(0, 0, 600, 250))
        .Node("Task_1", BpmnFlowNodeType.UserTask, new Bounds(220, 100, 100, 80), "Work")
        .Build();

    /// <summary>A Participant with no <c>processRef</c>, which the builder cannot express.</summary>
    private static BpmnDocument BlackBox()
    {
        var document = Pooled();
        var collaboration = document.Collaboration!;

        return document with
        {
            Collaboration = collaboration with
            {
                Participants = [collaboration.Participants[0] with { ProcessRef = null }],
            },
        };
    }

    /// <summary>A container whose BPMNDI shape says it is collapsed.</summary>
    private static BpmnDocument Collapsed(BpmnFlowNodeType type)
    {
        var document = WithContainer(type);
        var diagram = document.Diagrams[0];
        var plane = diagram.Plane;

        return document with
        {
            Diagrams =
            [
                diagram with
                {
                    Plane = plane with
                    {
                        Shapes = [.. plane.Shapes.Select(shape =>
                            shape.BpmnElement == "Sub_1" ? shape with { IsExpanded = false } : shape)],
                    },
                },
            ],
        };
    }

    /// <summary>
    /// One Sub-Process drawn twice: collapsed on one diagram, expanded on another.
    /// </summary>
    /// <remarks>
    /// Legal BPMN — a document may draw the same element on several planes — and the case that
    /// makes "which plane are you asking about" a real question rather than a tidiness.
    /// </remarks>
    private static BpmnDocument OnTwoPlanes()
    {
        var document = Collapsed(BpmnFlowNodeType.SubProcess);
        var collapsed = document.Diagrams[0];

        return document with
        {
            Diagrams =
            [
                collapsed with { Id = "Diagram_collapsed" },
                new BpmnDiagram(
                    "Diagram_expanded",
                    new BpmnPlane(
                        "Plane_expanded",
                        "Process_1",
                        [new BpmnShape("Sub_1_di_2", "Sub_1", new Bounds(0, 0, 350, 200), IsExpanded: true)],
                        [])),
            ],
        };
    }

    /// <summary>An embedded container, drawn expanded, which is the only state you can create into.</summary>
    private static BpmnDocument WithContainer(BpmnFlowNodeType type) => new BpmnDocumentBuilder("Process_1")
        .Node("Sub_1", type, new Bounds(200, 100, 350, 200), "Contained", isExpanded: true)
        .Build();

    /// <summary>The same container with no <c>isExpanded</c> at all, which is a distinct case.</summary>
    private static BpmnDocument WithUnstatedContainer(BpmnFlowNodeType type) => new BpmnDocumentBuilder("Process_1")
        .Node("Sub_1", type, new Bounds(200, 100, 350, 200), "Contained")
        .Build();
}
