using System.Collections;

using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class SessionAccessibleGraphCacheTests
{
    [Fact]
    public void BuildingGraphWithoutDrawnArtifactsEnumeratesSemanticNodesOnce()
    {
        var document = Document();
        var nodes = new CountingNodes(document.FlowNodes);
        document = document with
        {
            FlowNodes = nodes,
            Diagrams = [document.Diagrams[0] with
            {
                Plane = document.Diagrams[0].Plane with
                {
                    Shapes = [.. document.Diagrams[0].Plane.Shapes.Where(shape => shape.BpmnElement is "Pool" or "Lane" or "A" or "B")],
                },
            }],
        };

        var graph = BpmnAccessibleGraph.For(document, null);

        Assert.Equal(4, graph.Count);
        Assert.Equal(1, nodes.Enumerations);
    }

    [Fact]
    public void GeometryOnlyDocumentDoesNotEnumerateSemanticNodesAgain()
    {
        var document = Document();
        var nodes = new CountingNodes(document.FlowNodes);
        document = document with { FlowNodes = nodes };
        using var services = new ServiceCollection().BuildServiceProvider();
        var session = new BpmnEditorSession(new BpmnOptions(), services) { Document = document };
        var graph = session.AccessibleGraph;
        var enumerations = nodes.Enumerations;
        var diagram = document.Diagrams[0];
        session.Document = document with
        {
            Diagrams = [diagram with
            {
                Plane = diagram.Plane with
                {
                    Shapes = [.. diagram.Plane.Shapes.Select(shape =>
                        shape with { Bounds = shape.Bounds with { X = shape.Bounds.X + 10 } })],
                },
            }],
        };

        Assert.Same(graph, session.AccessibleGraph);
        Assert.Equal(enumerations, nodes.Enumerations);
    }

    [Theory]
    [InlineData("nodes")]
    [InlineData("flows")]
    [InlineData("pools")]
    [InlineData("laneSets")]
    [InlineData("lanes")]
    [InlineData("associations")]
    [InlineData("dataAssociations")]
    [InlineData("annotations")]
    [InlineData("groups")]
    [InlineData("dataObjects")]
    [InlineData("dataStores")]
    public void SemanticInputChangesMatchFreshGraph(string input)
    {
        var document = Document();
        using var services = new ServiceCollection().BuildServiceProvider();
        var session = new BpmnEditorSession(new BpmnOptions(), services) { Document = document };
        var original = session.AccessibleGraph;
        session.Document = input switch
        {
            "nodes" => document with { FlowNodes = [.. document.FlowNodes.Select(node => node with { Name = "Renamed" })] },
            "flows" => document with { SequenceFlows = [] },
            "pools" => document with { Collaboration = null },
            "laneSets" => document with { LaneSets = [] },
            "lanes" => document with { Lanes = [] },
            "associations" => document with { Associations = [] },
            "dataAssociations" => document with { DataAssociations = [] },
            "annotations" => document with { TextAnnotations = [] },
            "groups" => document with { Groups = [] },
            "dataObjects" => document with { DataObjectReferences = [] },
            "dataStores" => document with { DataStoreReferences = [] },
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };

        var graph = session.AccessibleGraph;
        var expected = BpmnAccessibleGraph.For(session.Document, null);

        Assert.NotSame(original, graph);
        Assert.Equal(expected.Count, graph.Count);
        Assert.All(expected.Zip(graph), pair => Assert.True(BpmnGraphNode.SameContent(pair.First, pair.Second)));
    }

    [Theory]
    [InlineData("membership")]
    [InlineData("expansion")]
    [InlineData("missing")]
    [InlineData("empty")]
    public void PlaneInputChangesMatchFreshGraph(string input)
    {
        var document = Document();
        using var services = new ServiceCollection().BuildServiceProvider();
        var session = new BpmnEditorSession(new BpmnOptions(), services) { Document = document };
        _ = session.AccessibleGraph;
        var diagram = document.Diagrams[0];
        session.Document = input switch
        {
            "membership" => document with { Diagrams = [diagram with { Plane = diagram.Plane with { Shapes = [] } }] },
            "expansion" => document with
            {
                Diagrams = [diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Shapes = [.. diagram.Plane.Shapes.Select(shape =>
                            shape.BpmnElement == "A" ? shape with { IsExpanded = true } : shape)],
                    },
                }],
            },
            "missing" => document with { Diagrams = [diagram with { Id = "Other" }] },
            "empty" => document with { Diagrams = [] },
            _ => throw new ArgumentOutOfRangeException(nameof(input)),
        };

        var graph = session.AccessibleGraph;
        var expected = BpmnAccessibleGraph.For(session.Document, null);

        Assert.Equal(expected.Count, graph.Count);
        Assert.All(expected.Zip(graph), pair => Assert.True(BpmnGraphNode.SameContent(pair.First, pair.Second)));
    }

    [Fact]
    public void MutableOverrideAndRemovalPreserveDocumentGraph()
    {
        var document = Document();
        using var services = new ServiceCollection().BuildServiceProvider();
        var session = new BpmnEditorSession(new BpmnOptions(), services) { Document = document };
        var expected = session.AccessibleGraph;
        var graph = new List<BpmnGraphNode> { expected[0] };
        session.AccessibleGraphOverride = graph;
        _ = session.AccessibleGraph;
        graph[0] = graph[0] with { Label = "Changed override" };
        Assert.Equal("Changed override", Assert.Single(session.AccessibleGraph).Label);

        session.AccessibleGraphOverride = null;
        Assert.Equal(expected.Count, session.AccessibleGraph.Count);
        Assert.All(expected.Zip(session.AccessibleGraph), pair => Assert.True(BpmnGraphNode.SameContent(pair.First, pair.Second)));
    }

    private static BpmnDocument Document()
    {
        var document = new BpmnDocumentBuilder()
            .Pool("Pool", "Pool", new Bounds(0, 0, 800, 400))
            .Lane("Lane", "Lane", new Bounds(30, 0, 770, 400), ["A"])
            .Node("A", BpmnFlowNodeType.SubProcess, new Bounds(100, 100, 100, 80), "First")
            .Node("B", BpmnFlowNodeType.Task, new Bounds(300, 100, 100, 80), "Second")
            .Flow("Flow", "A", "B", [new DiPoint(200, 140), new DiPoint(300, 140)])
            .TextAnnotation("Note", "Note", new Bounds(100, 250, 100, 50))
            .Association("Association", "Note", "A", [new DiPoint(150, 250), new DiPoint(150, 180)])
            .DataObject("Data", "Data", new Bounds(450, 100, 50, 80))
            .DataStore("Store", "Store", new Bounds(550, 100, 50, 80))
            .Group("Group", "Group", new Bounds(70, 70, 600, 250))
            .Build();
        return document with
        {
            DataAssociations = [new BpmnDataAssociation("Input", BpmnDataAssociationKind.Input, ["Data"], "B", "Process_1")],
        };
    }

    private sealed class CountingNodes(IReadOnlyList<BpmnFlowNode> nodes) : IReadOnlyList<BpmnFlowNode>
    {
        public int Enumerations { get; private set; }
        public int Count => nodes.Count;
        public BpmnFlowNode this[int index] => nodes[index];

        public IEnumerator<BpmnFlowNode> GetEnumerator()
        {
            Enumerations++;
            return nodes.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
