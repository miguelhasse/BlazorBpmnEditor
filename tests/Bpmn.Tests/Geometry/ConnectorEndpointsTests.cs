using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Geometry;

/// <summary>
/// A connector end is always on the outline of its shape (specification section 4.6).
/// </summary>
/// <remarks>
/// The projection is held to the canvas's by one table of cases that both suites run, so a document
/// normalised on import is drawn where it is stored.
/// </remarks>
public sealed class ConnectorEndpointsTests
{
    private const double Tolerance = 1e-4;

    // --- the shared table ------------------------------------------------------------

    [Fact]
    public void ProjectsEveryCaseInTheSharedTableWhereTheCanvasDoes()
    {
        foreach (var c in SharedCases().GetProperty("projections").EnumerateArray())
        {
            var outline = OutlineFor(c);
            var point = BpmnOutline.Project(Point(c, "point"), outline);
            var expected = Point(c, "expected");

            Assert.True(
                Math.Abs(point.X - expected.X) < Tolerance && Math.Abs(point.Y - expected.Y) < Tolerance,
                $"{c.GetProperty("name")}: expected {expected} but was {point}");
        }
    }

    [Fact]
    public void NormalisesEveryEndpointCaseInTheSharedTableWhereTheCanvasDoes()
    {
        foreach (var c in SharedCases().GetProperty("endpoints").EnumerateArray())
        {
            var point = BpmnOutline.Normalize(Point(c, "waypoint"), OutlineFor(c), Point(c, "other"));
            var expected = Point(c, "expected");

            Assert.True(
                Math.Abs(point.X - expected.X) < Tolerance && Math.Abs(point.Y - expected.Y) < Tolerance,
                $"{c.GetProperty("name")}: expected {expected} but was {point}");
        }
    }

    // --- normalising a document --------------------------------------------------------

    [Fact]
    public void MovesAnEndOffAShapesOutlineOntoIt()
    {
        var document = Elbow(firstEnd: new DiPoint(105, 140));

        var normalised = ConnectorEndpoints.Normalize(document);

        Assert.Equal(
            [new DiPoint(100, 140), new DiPoint(200, 140), new DiPoint(200, 240), new DiPoint(300, 240)],
            RouteOf(normalised));
    }

    [Fact]
    public void PutsAnEndOnACirclesArcRatherThanItsBoundingBox()
    {
        var document = new BpmnDocumentBuilder()
            .Node("Start", BpmnFlowNodeType.StartEvent, new Bounds(0, 0, 36, 36))
            .Node("Task", BpmnFlowNodeType.Task, new Bounds(200, 0, 100, 80))
            .Flow("Flow", "Start", "Task", [new DiPoint(36, 36), new DiPoint(200, 40)])
            .Build();

        var first = RouteOf(ConnectorEndpoints.Normalize(document))[0];

        Assert.Equal(18d, Math.Sqrt(Math.Pow(first.X - 18, 2) + Math.Pow(first.Y - 18, 2)), 6);
    }

    [Fact]
    public void LeavesTheSameInstanceWhenEveryEndIsAlreadyOnItsOutline()
    {
        var document = Elbow(firstEnd: new DiPoint(100, 140));

        Assert.Same(document, ConnectorEndpoints.Normalize(document));
    }

    [Fact]
    public void IsStableOnItsOwnOutput()
    {
        var once = ConnectorEndpoints.Normalize(Elbow(firstEnd: new DiPoint(120, 150)));

        Assert.Same(once, ConnectorEndpoints.Normalize(once));
    }

    [Fact]
    public void SendsAnEndThatCouldNotBelongToItsShapeTowardsTheOtherEnd()
    {
        // The real case is a file that declares every waypoint at the origin.
        var document = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(200, 100, 100, 60))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(450, 100, 100, 60))
            .Flow("Flow", "A", "B", [new DiPoint(0, 0), new DiPoint(0, 0)])
            .Build();

        var route = RouteOf(ConnectorEndpoints.Normalize(document));

        Assert.Equal(new DiPoint(300, 130), route[0]);
        Assert.Equal(new DiPoint(450, 130), route[^1]);
    }

    [Fact]
    public void LeavesTheBendsOfAnAngledRouteAlone()
    {
        // Associations are straight lines at any angle; only orthogonal flows are squared.
        var document = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(0, 100, 100, 80))
            .TextAnnotation("Note", "note", new Bounds(300, 0, 100, 30))
            .Association("Assoc", "A", "Note", [new DiPoint(105, 140), new DiPoint(200, 60), new DiPoint(300, 15)])
            .Build();

        var route = RouteOf(ConnectorEndpoints.Normalize(document), "Assoc_di");

        Assert.Equal(new DiPoint(200, 60), route[1]);
    }

    [Fact]
    public void AppliesAfterEveryEditNotJustOnImport()
    {
        // A move that leaves a route off its outline is put back: the editor is where a document is
        // made to say what the canvas draws.
        var document = Elbow(firstEnd: new DiPoint(100, 140));

        var result = BpmnDocumentEditor.Apply(
            document,
            new UpdateWaypointsCommand(
                "Flow",
                [new DiPoint(110, 140), new DiPoint(200, 140), new DiPoint(200, 240), new DiPoint(300, 240)]),
            Hasseware.AspNetCore.Components.Bpmn.Configuration.EditingOptions.Default);

        Assert.True(result.IsApplied);
        Assert.Equal(new DiPoint(100, 140), RouteOf(result.Document)[0]);
    }

    // --- right angles ----------------------------------------------------------------

    [Fact]
    public void ReplacesADiagonalFlowWithAZBetweenFacingSides()
    {
        var document = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(0, 100, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(300, 200, 100, 80))
            .Flow("Flow", "A", "B", [new DiPoint(100, 140), new DiPoint(300, 240)])
            .Build();

        var moved = new List<MovedConnector>();
        var result = ConnectorEndpoints.Normalize(document, moved);

        Assert.Equal(
            [new DiPoint(100, 140), new DiPoint(200, 140), new DiPoint(200, 240), new DiPoint(300, 240)],
            RouteOf(result));

        var reported = Assert.Single(moved);
        Assert.Equal("Flow", reported.ConnectorId);
        Assert.True(reported.Reshaped);
    }

    [Fact]
    public void JoinsEndsOnTopAndBottomSidesThroughTheGapBetweenThem()
    {
        var document = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(200, 200, 100, 80))
            .Flow("Flow", "A", "B", [new DiPoint(50, 80), new DiPoint(250, 200)])
            .Build();

        Assert.Equal(
            [new DiPoint(50, 80), new DiPoint(50, 140), new DiPoint(250, 140), new DiPoint(250, 200)],
            RouteOf(ConnectorEndpoints.Normalize(document)));
    }

    [Fact]
    public void UsesOneElbowWhenTheEndsLeaveAndEnterAlongDifferentAxes()
    {
        var document = new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(0, 0, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(200, 200, 100, 80))
            .Flow("Flow", "A", "B", [new DiPoint(100, 40), new DiPoint(250, 200)])
            .Build();

        Assert.Equal(
            [new DiPoint(100, 40), new DiPoint(250, 40), new DiPoint(250, 200)],
            RouteOf(ConnectorEndpoints.Normalize(document)));
    }

    [Fact]
    public void LeavesARightAngledRouteAloneAndIsStableOnItsOwnOutput()
    {
        var document = Elbow(firstEnd: new DiPoint(100, 140));

        Assert.Same(document, ConnectorEndpoints.Normalize(document));

        var reshaped = ConnectorEndpoints.Normalize(new BpmnDocumentBuilder()
            .Node("A", BpmnFlowNodeType.Task, new Bounds(0, 100, 100, 80))
            .Node("B", BpmnFlowNodeType.Task, new Bounds(300, 200, 100, 80))
            .Flow("Flow", "A", "B", [new DiPoint(100, 140), new DiPoint(300, 240)])
            .Build());

        Assert.Same(reshaped, ConnectorEndpoints.Normalize(reshaped));
    }

    [Fact]
    public void AlwaysLeavesAFlowRightAngledAfterAnEdit()
    {
        var result = BpmnDocumentEditor.Apply(
            Elbow(firstEnd: new DiPoint(100, 140)),
            new UpdateWaypointsCommand("Flow", [new DiPoint(100, 140), new DiPoint(300, 240)]),
            Hasseware.AspNetCore.Components.Bpmn.Configuration.EditingOptions.Default);

        var route = RouteOf(result.Document);
        Assert.All(route.Zip(route.Skip(1)), pair => Assert.True(
            Math.Abs(pair.First.X - pair.Second.X) < 0.5 || Math.Abs(pair.First.Y - pair.Second.Y) < 0.5));
    }

    // --- following a resized shape ----------------------------------------------------

    [Fact]
    public void KeepsASourceEndAtTheSamePlaceAlongTheSideWhenTheShapeGrowsTaller()
    {
        // Taller, not wider: the end was halfway down the right side and still is.
        var result = BpmnDocumentEditor.Apply(
            Elbow(firstEnd: new DiPoint(100, 140)),
            new ResizeShapeCommand("A_di", new Bounds(0, 100, 100, 160)),
            Hasseware.AspNetCore.Components.Bpmn.Configuration.EditingOptions.Default);

        Assert.True(result.IsApplied);
        Assert.Equal(
            [new DiPoint(100, 180), new DiPoint(200, 180), new DiPoint(200, 240), new DiPoint(300, 240)],
            RouteOf(result.Document));
    }

    [Fact]
    public void KeepsATargetEndAtTheSamePlaceAlongTheSideWhenTheShapeGrows()
    {
        var result = BpmnDocumentEditor.Apply(
            Elbow(firstEnd: new DiPoint(100, 140)),
            new ResizeShapeCommand("B_di", new Bounds(300, 200, 200, 160)),
            Hasseware.AspNetCore.Components.Bpmn.Configuration.EditingOptions.Default);

        Assert.True(result.IsApplied);
        Assert.Equal(
            [new DiPoint(100, 140), new DiPoint(200, 140), new DiPoint(200, 280), new DiPoint(300, 280)],
            RouteOf(result.Document));
    }

    [Fact]
    public void FollowsAShapeWhoseFarSideMovedWithoutJumpingToAnotherSide()
    {
        // The right side moves from x=100 to x=200. Re-projecting the old end would have picked the
        // top side (it is nearest to a point that is now inside the box); following it keeps the
        // end on the side it was on.
        var result = BpmnDocumentEditor.Apply(
            Elbow(firstEnd: new DiPoint(100, 140)),
            new ResizeShapeCommand("A_di", new Bounds(0, 100, 150, 80)),
            Hasseware.AspNetCore.Components.Bpmn.Configuration.EditingOptions.Default);

        Assert.Equal(150d, RouteOf(result.Document)[0].X, 6);
        Assert.Equal(140d, RouteOf(result.Document)[0].Y, 6);
    }

    [Fact]
    public void LeavesEndsAloneWhenNoShapeChangedSize()
    {
        var document = Elbow(firstEnd: new DiPoint(100, 140));

        Assert.Same(document, ConnectorEndpoints.FollowResizedShapes(document, document));
    }

    [Fact]
    public void DoesNotAttachAnEndThatWasNeverOnTheShape()
    {
        // Far from the old box, so there is no relationship to preserve; normalisation handles it.
        var document = Elbow(firstEnd: new DiPoint(60, 400));
        var after = document with
        {
            Diagrams =
            [
                .. document.Diagrams.Select(diagram => diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Shapes = [.. diagram.Plane.Shapes.Select(shape => shape.BpmnElement == "A"
                            ? shape with { Bounds = new Bounds(0, 100, 200, 80) }
                            : shape)],
                    },
                }),
            ],
        };

        Assert.Equal(new DiPoint(60, 400), RouteOf(ConnectorEndpoints.FollowResizedShapes(document, after))[0]);
    }

    // --- fixtures ------------------------------------------------------------------

    private static BpmnDocument Elbow(DiPoint firstEnd) => new BpmnDocumentBuilder()
        .Node("A", BpmnFlowNodeType.Task, new Bounds(0, 100, 100, 80))
        .Node("B", BpmnFlowNodeType.Task, new Bounds(300, 200, 100, 80))
        .Flow("Flow", "A", "B", [firstEnd, new DiPoint(200, 140), new DiPoint(200, 240), new DiPoint(300, 240)])
        .Build();

    private static IReadOnlyList<DiPoint> RouteOf(BpmnDocument document, string edgeId = "Flow_di") =>
        document.Diagrams.SelectMany(diagram => diagram.Plane.Edges).Single(edge => edge.Id == edgeId).Waypoints;

    private static DiPoint Point(JsonElement element, string name)
    {
        var values = element.GetProperty(name).EnumerateArray().Select(value => value.GetDouble()).ToArray();
        return new DiPoint(values[0], values[1]);
    }

    private static Outline OutlineFor(JsonElement c)
    {
        var box = c.GetProperty("bounds").EnumerateArray().Select(value => value.GetDouble()).ToArray();
        var bounds = new Bounds(box[0], box[1], box[2], box[3]);

        return c.GetProperty("elementType").GetString() switch
        {
            "task" => BpmnOutline.OfFlowNode(BpmnFlowNodeType.Task, bounds),
            "startEvent" => BpmnOutline.OfFlowNode(BpmnFlowNodeType.StartEvent, bounds),
            "exclusiveGateway" => BpmnOutline.OfFlowNode(BpmnFlowNodeType.ExclusiveGateway, bounds),
            "pool" => BpmnOutline.Rectangle(bounds),
            var other => throw new InvalidOperationException($"The shared table uses '{other}', which this test does not map."),
        };
    }

    private static JsonElement SharedCases()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Bpmn.Scripts", "test", "fixtures", "outline-cases.json");
            if (File.Exists(candidate)) return JsonDocument.Parse(File.ReadAllText(candidate)).RootElement;

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate outline-cases.json from the test output folder.");
    }
}
