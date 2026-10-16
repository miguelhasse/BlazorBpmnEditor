using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Accessibility;

/// <summary>
/// The diagram as a navigable graph (implementation plan M7).
/// </summary>
/// <remarks>
/// Built from the model rather than from the drawn shapes, and these are the reasons why: the
/// shapes come in paint order, they say nothing about what connects to what, and once the paper
/// culls what is off screen they are not even all there. None of that is true of the document.
/// </remarks>
public sealed class AccessibleGraphTests
{
    [Fact]
    public void ReadsInDocumentOrder()
    {
        var nodes = BpmnAccessibleGraph.For(Linear(), null);

        Assert.Equal(["Start_1", "Task_1", "End_1"], nodes.Select(node => node.ElementId));
    }

    [Fact]
    public void CarriesTheFlowsBothWays()
    {
        // The part a labelled SVG cannot give a reader at all, and most of what a diagram means.
        var nodes = BpmnAccessibleGraph.For(Linear(), null);

        Assert.Equal(["Task_1"], nodes[0].Outgoing);
        Assert.Empty(nodes[0].Incoming);
        Assert.Equal(["Start_1"], nodes[1].Incoming);
        Assert.Equal(["End_1"], nodes[1].Outgoing);
    }

    [Fact]
    public void OnlyTheDiagramBeingShown()
    {
        /*
         * A document may hold several planes and the reader is looking at one. Listing them all
         * would announce elements that are not on screen and offer to walk to them, which is the
         * diagram picker's job and not an arrow key's.
         */
        var document = TwoDiagrams();

        var first = BpmnAccessibleGraph.For(document, "Diagram_1");
        var second = BpmnAccessibleGraph.For(document, "Diagram_2");

        Assert.Equal(["Start_1", "Task_1", "End_1"], first.Select(node => node.ElementId));
        Assert.Equal(["Other_1"], second.Select(node => node.ElementId));
    }

    [Fact]
    public void AnUnknownDiagramIsEmptyRatherThanEverything()
    {
        Assert.Empty(BpmnAccessibleGraph.For(Linear(), "Diagram_Nonexistent"));
    }

    [Fact]
    public void FlowsToSomewhereOffThisPlaneAreNotOffered()
    {
        // A flow whose other end is not drawn here is not a step a reader can take, and offering
        // it would move focus to an element the tree does not contain.
        var document = TwoDiagrams();

        var nodes = BpmnAccessibleGraph.For(document, "Diagram_2");

        Assert.Empty(nodes[0].Outgoing);
        Assert.Empty(nodes[0].Incoming);
    }

    [Fact]
    public void NestingBecomesALevel()
    {
        // What lets a reader skip a Sub-Process instead of walking everything inside it.
        var nodes = BpmnAccessibleGraph.For(Nested(), null);

        Assert.Equal(1, nodes.Single(node => node.ElementId == "Sub_1").Depth);
        Assert.Equal(2, nodes.Single(node => node.ElementId == "Inner_Task").Depth);
    }

    [Fact]
    public void PoolsAndLanesAreStopsToo()
    {
        /*
         * They hold no Sequence Flow and are never walked along one, but on a Collaboration they
         * are what says whose process this is. A reader offered the tasks and never told which
         * Pool they belong to has been given the diagram with its structure taken out.
         */
        var nodes = BpmnAccessibleGraph.For(Read("pizza-store.bpmn"), null);

        var pools = nodes.Where(node => node.TypeName == "pool").ToList();
        var lanes = nodes.Where(node => node.TypeName == "lane").ToList();

        Assert.NotEmpty(pools);
        Assert.All(pools, pool => Assert.Equal(1, pool.Depth));
        Assert.All(lanes, lane => Assert.Equal(2, lane.Depth));

        // And no flow counts: a Pool cannot carry a Sequence Flow, so "no incoming, no outgoing"
        // would not be describing this one, it would be describing every Pool ever drawn.
        Assert.All(pools, pool => Assert.DoesNotContain("incoming", pool.AccessibleName, StringComparison.Ordinal));
    }

    [Fact]
    public void AProcessWithSeveralLaneSetsStillListsItsLanes()
    {
        // BPMN allows more than one Lane Set per Process; keying them by container threw.
        var document = Read("pizza-store.bpmn");
        var first = document.LaneSets[0];
        var duplicate = new BpmnLaneSet("LaneSet_Extra", first.ContainerId, []);

        var nodes = BpmnAccessibleGraph.For(document with { LaneSets = [.. document.LaneSets, duplicate] }, null);

        Assert.Equal(
            BpmnAccessibleGraph.For(document, null).Count(node => node.TypeName == "lane"),
            nodes.Count(node => node.TypeName == "lane"));
    }

    [Fact]
    public void SeveralParticipantsReferencingOneProcessListEachLaneOnce()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool_A", "A", new Bounds(0, 0, 400, 120))
            .Pool("Pool_B", "B", new Bounds(0, 160, 400, 120))
            .Lane("Shared_Lane", "Shared", new Bounds(30, 0, 370, 120), ["Task_1"])
            .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(100, 20, 100, 80))
            .Build();

        var nodes = BpmnAccessibleGraph.For(document, null);

        Assert.Equal(["Pool_A", "Shared_Lane", "Task_1", "Pool_B"], nodes.Select(node => node.ElementId));
        Assert.Equal("Pool_A", nodes.Single(node => node.ElementId == "Shared_Lane").ContainerId);
        Assert.Equal(nodes.Count, nodes.Select(node => node.ElementId).Distinct(StringComparer.Ordinal).Count());

        var withoutFirstPool = document with
        {
            Diagrams = [document.Diagrams[0] with
            {
                Plane = document.Diagrams[0].Plane with
                {
                    Shapes = [.. document.Diagrams[0].Plane.Shapes.Where(shape => shape.BpmnElement != "Pool_A")],
                },
            }],
        };
        var remaining = BpmnAccessibleGraph.For(withoutFirstPool, null);
        Assert.Equal(["Pool_B", "Shared_Lane", "Task_1"], remaining.Select(node => node.ElementId));
        Assert.Equal("Pool_B", remaining.Single(node => node.ElementId == "Shared_Lane").ContainerId);
    }

    [Fact]
    public void AParentComesImmediatelyBeforeWhatItHolds()
    {
        // A tree is only worth walking if a parent is next to its children.
        var nodes = BpmnAccessibleGraph.For(Read("pizza-store.bpmn"), null);

        Assert.Equal("pool", nodes[0].TypeName);

        // Everything after a container and before the next one at the same level is inside it.
        var deeper = nodes.Skip(1).TakeWhile(node => node.Depth > 1).ToList();
        Assert.NotEmpty(deeper);
    }

    [Fact]
    public void ANodeInsideAPoolIsDeeperThanTheSameNodeOnAPlainProcess()
    {
        var plain = BpmnAccessibleGraph.For(Linear(), null);
        var pooled = BpmnAccessibleGraph.For(Read("pizza-store.bpmn"), null);

        Assert.All(plain, node => Assert.Equal(1, node.Depth));
        Assert.Contains(pooled, node => node.TypeName != "pool" && node.TypeName != "lane" && node.Depth > 1);
    }

    [Fact]
    public void EverySuchDiagramListsEveryDrawnFlowNodeExactlyOnce()
    {
        // The interleaving must not drop a node that belongs to no Pool, nor list one twice.
        foreach (var sample in new[] { "pizza-store.bpmn", "collaboration.bpmn", "order-process.bpmn" })
        {
            var document = Read(sample);
            var nodes = BpmnAccessibleGraph.For(document, null);

            var drawn = document.Diagrams[0].Plane.Shapes.Select(shape => shape.BpmnElement).ToHashSet(StringComparer.Ordinal);
            var expected = document.FlowNodes.Count(node => drawn.Contains(node.Id));

            var listed = nodes.Count(node => node.TypeName is not ("pool" or "lane"
                or "textAnnotation" or "group" or "dataObjectReference" or "dataStoreReference"));

            Assert.Equal(expected, listed);
            Assert.Equal(nodes.Count, nodes.Select(node => node.ElementId).Distinct(StringComparer.Ordinal).Count());
        }
    }

    // --- artifacts and data ------------------------------------------------------------

    [Fact]
    public void ArtifactsAndDataElementsAreStopsToo()
    {
        /*
         * They carry no Sequence Flow and the arrow keys never walk into one, but they are drawn
         * and they are named. A diagram where a Data Object feeds three Tasks says something that
         * a list of only the Tasks does not.
         */
        var nodes = BpmnAccessibleGraph.For(Read("global-catalog.bpmn"), null);

        Assert.Contains(nodes, node => node.TypeName == "textAnnotation");
        Assert.Contains(nodes, node => node.TypeName == "dataObjectReference");
        Assert.Contains(nodes, node => node.TypeName == "group");
    }

    [Fact]
    public void EveryDrawnArtifactIsListedExactlyOnce()
    {
        foreach (var sample in new[] { "pizza-store.bpmn", "global-catalog.bpmn", "self-serve-restaurant.bpmn" })
        {
            var document = Read(sample);
            var drawn = document.Diagrams[0].Plane.Shapes.Select(shape => shape.BpmnElement).ToHashSet(StringComparer.Ordinal);

            var expected = document.TextAnnotations.Count(a => drawn.Contains(a.Id))
                + document.Groups.Count(g => drawn.Contains(g.Id))
                + document.DataObjectReferences.Count(r => drawn.Contains(r.Id))
                + document.DataStoreReferences.Count(r => drawn.Contains(r.Id));

            var nodes = BpmnAccessibleGraph.For(document, null);
            var listed = nodes.Count(node => node.TypeName
                is "textAnnotation" or "group" or "dataObjectReference" or "dataStoreReference");

            Assert.Equal(expected, listed);
            Assert.Equal(nodes.Count, nodes.Select(node => node.ElementId).Distinct(StringComparer.Ordinal).Count());
        }
    }

    [Fact]
    public void AnAnnotationIsReadAsItsText()
    {
        // It has no name, and reading out its id would tell a reader nothing about what it says.
        var annotation = BpmnAccessibleGraph.For(Read("global-catalog.bpmn"), null)
            .First(node => node.TypeName == "textAnnotation");

        Assert.NotEmpty(annotation.Label);
        Assert.DoesNotContain(annotation.ElementId, annotation.AccessibleName, StringComparison.Ordinal);
    }

    [Fact]
    public void AnArtifactSaysWhatItIsAssociatedWith()
    {
        /*
         * The only thing that makes an artifact mean anything. A Data Object nobody uses and one
         * every Task reads are different facts, and neither is visible to a reader being read to.
         * Names rather than ids, because an id is not something a reader can do anything with.
         */
        var nodes = BpmnAccessibleGraph.For(Read("global-catalog.bpmn"), null);
        var attached = nodes.FirstOrDefault(node => node.Attached.Count > 0);

        Assert.NotNull(attached);
        Assert.Contains("Associated with", attached.AccessibleName, StringComparison.Ordinal);
        Assert.All(attached.Attached, name => Assert.DoesNotContain("_", name, StringComparison.Ordinal));
    }

    [Fact]
    public void AnUnassociatedArtifactSaysThatInstead()
    {
        var lonely = new BpmnGraphNode("Annotation_1", "textAnnotation", "A note", "Process_1", null, 1, [], []);

        Assert.Equal("Text annotation A note. Not associated with anything.", lonely.AccessibleName);
    }

    [Fact]
    public void AnArtifactNeverAnnouncesFlowCounts()
    {
        // It cannot carry a Sequence Flow, so "no incoming, no outgoing" would be true of every
        // artifact ever drawn rather than of this one.
        var nodes = BpmnAccessibleGraph.For(Read("global-catalog.bpmn"), null);

        foreach (var artifact in nodes.Where(node => node.TypeName is "textAnnotation" or "group"
            or "dataObjectReference" or "dataStoreReference"))
        {
            Assert.DoesNotContain("incoming", artifact.AccessibleName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnAssociationIsNotSomethingTheArrowKeysWalk()
    {
        /*
         * Deliberate: Right and Left follow Sequence Flow. An Association is not control flow, and
         * stepping from a Task into a comment about it would make the one direction that means
         * "what happens next" sometimes mean something else.
         */
        var nodes = BpmnAccessibleGraph.For(Read("global-catalog.bpmn"), null);

        foreach (var artifact in nodes.Where(node => node.TypeName is "textAnnotation" or "group"
            or "dataObjectReference" or "dataStoreReference"))
        {
            Assert.Empty(artifact.Outgoing);
            Assert.Empty(artifact.Incoming);
            Assert.Null(BpmnGraphNavigation.Move(nodes, artifact.ElementId, BpmnGraphDirection.Forward));
        }
    }

    // --- what a reader hears -----------------------------------------------------------

    [Theory]
    [InlineData("startEvent", "Begin", 0, 1, "Start event Begin. no incoming, 1 outgoing.")]
    [InlineData("userTask", "", 2, 3, "User task. 2 incoming, 3 outgoing.")]
    [InlineData("exclusiveGateway", "OK?", 1, 2, "Exclusive gateway OK?. 1 incoming, 2 outgoing.")]
    public void NamesTheTypeFirstAndCountsTheFlows(string type, string label, int incoming, int outgoing, string expected)
    {
        /*
         * The type first because many BPMN elements have no name at all and the type is what says
         * what the thing is. The counts because on a diagram nobody can see, they are the only
         * indication that there is somewhere further to go.
         */
        var node = new BpmnGraphNode(
            "X",
            type,
            label,
            null,
            null,
            1,
            [.. Enumerable.Range(0, outgoing).Select(index => $"out{index}")],
            [.. Enumerable.Range(0, incoming).Select(index => $"in{index}")]);

        Assert.Equal(expected, node.AccessibleName);
    }

    // --- fixtures ----------------------------------------------------------------------

    /// <summary>A shipped fixture, because a Collaboration with Lanes is tedious to build by hand.</summary>
    private static BpmnDocument Read(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        var root = directory?.FullName ?? throw new InvalidOperationException("the repository root was not found");
        var path = Path.Combine(root, "playground", "Bpmn.Demo", "wwwroot", "samples", fileName);

        return BpmnXmlReader.Read(File.ReadAllText(path)).Document;
    }

    private static BpmnDocument Linear() => new BpmnDocumentBuilder()
        .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(100, 100, 36, 36), "Begin")
        .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(200, 80, 100, 80), "Work")
        .Node("End_1", BpmnFlowNodeType.EndEvent, new Bounds(360, 100, 36, 36), "Done")
        .Flow("Flow_1", "Start_1", "Task_1", [new DiPoint(136, 118), new DiPoint(200, 118)])
        .Flow("Flow_2", "Task_1", "End_1", [new DiPoint(300, 118), new DiPoint(360, 118)])
        .Build();

    private static BpmnDocument TwoDiagrams()
    {
        var document = Linear();

        var other = new BpmnFlowNode("Other_1", BpmnFlowNodeType.Task, document.Processes[0].Id, "Elsewhere");

        return document with
        {
            FlowNodes = [.. document.FlowNodes, other],
            SequenceFlows = [.. document.SequenceFlows, new BpmnSequenceFlow("Flow_Cross", "End_1", "Other_1", document.Processes[0].Id)],
            Diagrams =
            [
                document.Diagrams[0] with { Id = "Diagram_1" },
                new BpmnDiagram(
                    "Diagram_2",
                    new BpmnPlane(
                        "Plane_2",
                        document.Processes[0].Id,
                        [new BpmnShape("Shape_Other", "Other_1", new Bounds(600, 100, 100, 80))],
                        [])),
            ],
        };
    }

    private static BpmnDocument Nested()
    {
        var document = Linear();
        var process = document.Processes[0].Id;

        var sub = new BpmnFlowNode("Sub_1", BpmnFlowNodeType.SubProcess, process, "Handle") { };
        var inner = new BpmnFlowNode("Inner_Task", BpmnFlowNodeType.Task, "Sub_1", "Inside");

        return document with
        {
            FlowNodes = [.. document.FlowNodes, sub, inner],
            Diagrams =
            [
                document.Diagrams[0] with
                {
                    Plane = document.Diagrams[0].Plane with
                    {
                        Shapes =
                        [
                            .. document.Diagrams[0].Plane.Shapes,
                            new BpmnShape("Shape_Sub", "Sub_1", new Bounds(500, 60, 300, 200)),
                            new BpmnShape("Shape_Inner", "Inner_Task", new Bounds(540, 100, 100, 80)),
                        ],
                    },
                },
            ],
        };
    }
}
