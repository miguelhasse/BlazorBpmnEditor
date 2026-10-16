using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// Keeping a Boundary Event in place on a host that changed size.
/// </summary>
/// <remarks>
/// <para>
/// Section 1.2.1: Boundary Events "remain attached to and move with the Sub-Process in both
/// states". When the host merely moves that is a translation and there is nothing to decide. When
/// it grows — which is exactly what expanding does — there is: the Event's old centre now falls
/// <em>inside</em> the new rectangle, and every point of the outline is a candidate.
/// </para>
/// <para>
/// <c>OnOutline</c> answers "nearest", which is right for a drop, where the pointer says what the
/// user meant. Here it is wrong, and wrong in a way that looks like a bug rather than a rounding
/// difference: an Event on the bottom edge surfaces on the left.
/// </para>
/// </remarks>
public sealed class BoundaryRehostTests
{
    private const double Tolerance = 1e-9;

    /// <summary>A 100x80 Activity at the origin, the footprint a collapsed Sub-Process takes.</summary>
    private static readonly Bounds Collapsed = new(100, 100, 100, 80);

    /// <summary>The same Activity expanded, which is the case this exists for.</summary>
    private static readonly Bounds Expanded = new(100, 100, 350, 200);

    [Fact]
    public void AnEventOnTheBottomEdgeStaysOnTheBottomEdge()
    {
        // Centre at (150, 180): the middle of the bottom edge.
        var moved = BpmnSubProcessLayout.Resized(Collapsed, (350, 200));
        var rehosted = BpmnBoundaryPlacement.Rehost(Collapsed, moved, Event(150, 180));

        Assert.Equal(moved.Y + moved.Height, Centre(rehosted).Y, Tolerance);
    }

    [Fact]
    public void AndAtTheSameFractionAlongIt()
    {
        // A quarter of the way along, and still a quarter of the way along afterwards — which is
        // what makes two boundary events keep their order and their spacing.
        var rehosted = BpmnBoundaryPlacement.Rehost(Collapsed, Expanded, Event(125, 180));

        Assert.Equal(0.25d, (Centre(rehosted).X - Expanded.X) / Expanded.Width, Tolerance);
    }

    [Fact]
    public void AnEventOnTheRightEdgeStaysOnTheRightEdge()
    {
        var rehosted = BpmnBoundaryPlacement.Rehost(Collapsed, Expanded, Event(200, 140));

        Assert.Equal(Expanded.X + Expanded.Width, Centre(rehosted).X, Tolerance);
        Assert.Equal(0.5d, (Centre(rehosted).Y - Expanded.Y) / Expanded.Height, Tolerance);
    }

    [Fact]
    public void AnEventOnTheLeftEdgeStaysOnTheLeftEdge()
    {
        var rehosted = BpmnBoundaryPlacement.Rehost(Collapsed, Expanded, Event(100, 140));

        Assert.Equal(Expanded.X, Centre(rehosted).X, Tolerance);
    }

    [Fact]
    public void AnEventOnTheTopEdgeStaysOnTheTopEdge()
    {
        var rehosted = BpmnBoundaryPlacement.Rehost(Collapsed, Expanded, Event(150, 100));

        Assert.Equal(Expanded.Y, Centre(rehosted).Y, Tolerance);
    }

    [Fact]
    public void NearestWouldHaveGotThisWrong()
    {
        /*
         * The whole reason this exists, stated as a test rather than a comment.
         *
         * A container grows from its corner, so its left and top edges stay put while the right
         * and bottom run away. An Event in the middle of what was the bottom edge is then 120 from
         * the new bottom but only 50 from the left, so "nearest point of the perimeter" moves it
         * onto a different side of the shape — which reads as the Event having jumped rather than
         * the host having grown.
         */
        var boundary = Event(150, 180);

        var nearest = BpmnBoundaryPlacement.OnOutline(Expanded, boundary);
        var rehosted = BpmnBoundaryPlacement.Rehost(Collapsed, Expanded, boundary);

        Assert.NotEqual(Expanded.Y + Expanded.Height, Centre(nearest).Y, Tolerance);
        Assert.Equal(Expanded.Y + Expanded.Height, Centre(rehosted).Y, Tolerance);
    }

    [Fact]
    public void ItKeepsTheEventsOwnSize()
    {
        var rehosted = BpmnBoundaryPlacement.Rehost(Collapsed, Expanded, Event(150, 180));

        Assert.Equal(36d, rehosted.Width);
        Assert.Equal(36d, rehosted.Height);
    }

    [Fact]
    public void ACentreSlightlyOffTheOutlineStillCountsAsOnIt()
    {
        // Bounds are doubles and drags report integers, so an Event's centre is routinely a pixel
        // off its host's edge. Deciding by which edge is nearest survives that; an equality test
        // would not.
        var rehosted = BpmnBoundaryPlacement.Rehost(Collapsed, Expanded, Event(150, 178.5));

        Assert.Equal(Expanded.Y + Expanded.Height, Centre(rehosted).Y, Tolerance);
    }

    [Fact]
    public void AHostWithNoAreaDoesNotDivideByIt()
    {
        // Degenerate, but reachable from a hand-edited file, and a NaN here would spread into every
        // coordinate downstream.
        var rehosted = BpmnBoundaryPlacement.Rehost(new Bounds(0, 0, 0, 0), Expanded, Event(0, 0));

        Assert.False(double.IsNaN(rehosted.X));
        Assert.False(double.IsNaN(rehosted.Y));
    }

    private static Bounds Event(double centreX, double centreY) => new(centreX - 18, centreY - 18, 36, 36);

    private static (double X, double Y) Centre(Bounds bounds) =>
        (bounds.X + (bounds.Width / 2d), bounds.Y + (bounds.Height / 2d));
}
