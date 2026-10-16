using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Where an expanded Sub-Process sits and how big it is.
/// </summary>
/// <remarks>
/// Arithmetic with no document in it, which is why it is worth having on its own: the command that
/// uses it has a great deal else going on, and the one property everything depends on — that
/// expanding and collapsing is a cycle rather than a drift — is invisible inside a document test.
/// </remarks>
public sealed class SubProcessLayoutTests
{
    private const double Tolerance = 1e-9;

    private static readonly EditingOptions Options = new(PreventCollisions: true, CollisionClearance: 5d, HistoryDepth: 50);

    // --- the content box ---------------------------------------------------------------

    [Fact]
    public void TheContentBoxEnclosesEveryShape()
    {
        var box = BpmnSubProcessLayout.ContentBox(
            [Shape("A", new Bounds(100, 50, 40, 20)), Shape("B", new Bounds(200, 90, 60, 30))],
            []);

        Assert.Equal(new Bounds(100, 50, 160, 70), box);
    }

    [Fact]
    public void AndEveryWaypointToo()
    {
        /*
         * A connector can loop out past both its endpoints — a boundary event's flow going back to
         * a task above it does exactly that — so sizing a container from its shapes alone clips
         * routes that are just as much its content.
         */
        var box = BpmnSubProcessLayout.ContentBox(
            [Shape("A", new Bounds(100, 100, 40, 20))],
            [Edge("Flow_1", [new DiPoint(120, 110), new DiPoint(120, 40), new DiPoint(300, 40)])]);

        Assert.Equal(new Bounds(100, 40, 200, 80), box);
    }

    [Fact]
    public void AndIsNothingAtAllWhenThereIsNothingDrawn()
    {
        // Distinct from a zero-size box: "no content" is what makes the minimum size apply.
        Assert.Null(BpmnSubProcessLayout.ContentBox([], []));
    }

    // --- the expanded size -------------------------------------------------------------

    [Fact]
    public void AnExpandedSubProcessLeavesPaddingRoundItsContent()
    {
        var size = BpmnSubProcessLayout.ExpandedSize(new Bounds(0, 0, 500, 300), Options);

        Assert.Equal((540d, 340d), size);
    }

    [Fact]
    public void AndIsNeverSmallerThanTheMinimum()
    {
        // Two elements in a corner should not produce a container too small to drop anything into.
        var size = BpmnSubProcessLayout.ExpandedSize(new Bounds(0, 0, 60, 40), Options);

        Assert.Equal((Options.MinimumExpandedWidth, Options.MinimumExpandedHeight), size);
    }

    [Fact]
    public void AndTakesTheMinimumWhenThereIsNothingToHold()
    {
        Assert.Equal(
            (Options.MinimumExpandedWidth, Options.MinimumExpandedHeight),
            BpmnSubProcessLayout.ExpandedSize(null, Options));
    }

    // --- centring, and the cycle it makes possible -------------------------------------

    [Fact]
    public void GrowingKeepsTheShapeWhereTheUserPutIt()
    {
        /*
         * The alternative — keep the content where it is and draw the container round it — is the
         * literal reading of "never changes child BPMNDI coordinates", and on a real file it makes
         * the Sub-Process teleport across the diagram and land on its neighbours.
         */
        var grown = BpmnSubProcessLayout.Resized(new Bounds(345, 80, 100, 80), (350, 200));

        Assert.Equal(new Bounds(345, 80, 350, 200), grown);
        Assert.Equal(new Bounds(345, 80, 100, 80), BpmnSubProcessLayout.CollapsedBounds(grown, Options));
    }

    [Fact]
    public void AndGrowsOnlyDownAndToTheRight()
    {
        // One-directional on purpose. Growing from the centre shoves the neighbours on every side,
        // including out of the leading edge of the Pool around it, which then has to grow backwards
        // to follow them — and the whole diagram appears to slide.
        var grown = BpmnSubProcessLayout.Resized(new Bounds(345, 80, 100, 80), (350, 200));

        Assert.Equal(345, grown.X);
        Assert.Equal(80, grown.Y);
    }

    [Fact]
    public void ExpandingAndCollapsingAgainReturnsToTheSameRectangle()
    {
        // The property the whole feature rests on. Centring is its own inverse, so this holds for
        // any starting rectangle rather than for the one that happened to be tested.
        var start = new Bounds(345, 80, Options.CollapsedWidth, Options.CollapsedHeight);

        var expanded = BpmnSubProcessLayout.Resized(
            start,
            BpmnSubProcessLayout.ExpandedSize(new Bounds(0, 0, 500, 300), Options));

        var collapsed = BpmnSubProcessLayout.CollapsedBounds(expanded, Options);

        Assert.Equal(start.X, collapsed.X, Tolerance);
        Assert.Equal(start.Y, collapsed.Y, Tolerance);
        Assert.Equal(start.Width, collapsed.Width, Tolerance);
        Assert.Equal(start.Height, collapsed.Height, Tolerance);
    }

    // --- moving content between planes -------------------------------------------------

    [Fact]
    public void ContentLandsAPaddingInFromTheContainersCorner()
    {
        // A plane's origin is arbitrary, so bringing content across is a rigid translation and the
        // only question is which corner it is measured from.
        var offset = BpmnSubProcessLayout.OffsetInto(
            new Bounds(1000, 2000, 300, 150),
            new Bounds(100, 100, 340, 190),
            Options);

        Assert.Equal((-880d, -1880d), offset);

        var moved = BpmnSubProcessLayout.Shift(new Bounds(1000, 2000, 300, 150), offset);

        Assert.Equal(new Bounds(120, 120, 300, 150), moved);
    }

    [Fact]
    public void AndTheContentKeepsItsShape()
    {
        /*
         * What "never changes child BPMNDI coordinates" is actually protecting: the children move
         * as one block, so every distance between them survives the trip between planes.
         */
        var offset = (X: -880d, Y: -1880d);

        var first = BpmnSubProcessLayout.Shift(new Bounds(1000, 2000, 40, 20), offset);
        var second = BpmnSubProcessLayout.Shift(new Bounds(1120, 2060, 40, 20), offset);

        Assert.Equal(120d, second.X - first.X);
        Assert.Equal(60d, second.Y - first.Y);
    }

    [Fact]
    public void AndSoDoTheRoutes()
    {
        var moved = BpmnSubProcessLayout.Shift(new DiPoint(1000, 2000), (-880d, -1880d));

        Assert.Equal(new DiPoint(120, 120), moved);
    }

    // --- fixtures ----------------------------------------------------------------------

    private static BpmnShape Shape(string id, Bounds bounds) => new($"{id}_di", id, bounds);

    private static BpmnEdge Edge(string id, IReadOnlyList<DiPoint> waypoints) => new($"{id}_di", id, waypoints);
}
