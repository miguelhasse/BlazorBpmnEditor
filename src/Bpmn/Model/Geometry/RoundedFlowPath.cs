using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Geometry;

/// <summary>
/// The canvas's rounded flow path, flattened for persistent annotation attachments.
/// Keep in step with Bpmn.Scripts routing/rounded-path.ts and geometry/constants.ts.
/// </summary>
internal static class RoundedFlowPath
{
    private const double Radius = 8;
    private const double Epsilon = 0.01;
    private const double ShortSegment = 2.5;
    private const double MaximumJog = 5;
    private const int CurveSteps = 64;

    internal static IReadOnlyList<DiPoint> Flatten(IReadOnlyList<DiPoint> waypoints)
    {
        var route = waypoints.Where((point, index) => index == 0 || Distance(waypoints[index - 1], point) > Epsilon).ToArray();
        if (route.Length < 2) return route;
        var path = new List<DiPoint> { Rounded(route[0]) };
        for (var index = 1; index < route.Length - 1; index++)
        {
            var current = route[index];
            if (JogEnd(route, index) is var end && end > index)
            {
                var backward = Unit(current, route[index - 1]);
                var forward = Unit(route[end], route[end + 1]);
                var reach = Math.Min(Radius, Math.Min(Distance(route[index - 1], current), Distance(route[end], route[end + 1])) / 2);
                var start = Along(current, backward, reach);
                var finish = Along(route[end], forward, reach);
                AddCurve(path, start, Along(start, forward, reach), Along(finish, backward, reach), finish);
                index = end;
                continue;
            }
            var back = Unit(current, route[index - 1]);
            var ahead = Unit(current, route[index + 1]);
            if (Math.Abs((back.X * ahead.Y) - (back.Y * ahead.X)) < 0.02) continue;
            var from = Along(current, back, Math.Min(Radius, Distance(route[index - 1], current) / 2));
            var to = Along(current, ahead, Math.Min(Radius, Distance(current, route[index + 1]) / 2));
            AddCurve(path, from, Mix(from, current, 2d / 3), Mix(to, current, 2d / 3), to);
        }
        path.Add(Rounded(route[^1]));
        return path;
    }

    private static int JogEnd(DiPoint[] route, int index)
    {
        if (Distance(route[index - 1], route[index]) <= ShortSegment) return index;
        var end = index;
        double length = 0;
        while (end + 1 < route.Length)
        {
            var step = Distance(route[end], route[end + 1]);
            if (step > ShortSegment) break;
            length += step;
            end++;
        }
        if (end == index || end + 1 >= route.Length || length > MaximumJog) return index;
        var back = Unit(route[index], route[index - 1]);
        var ahead = Unit(route[end], route[end + 1]);
        if ((-back.X * ahead.X) - (back.Y * ahead.Y) < 0.9) return index;
        if (Distance(route[index], route[end]) < Epsilon) return index;
        var across = Unit(route[index], route[end]);
        return Math.Abs((across.X * ahead.X) + (across.Y * ahead.Y)) > 0.5 ? index : end;
    }

    private static void AddCurve(List<DiPoint> path, DiPoint start, DiPoint first, DiPoint second, DiPoint end)
    {
        start = Rounded(start);
        first = Rounded(first);
        second = Rounded(second);
        end = Rounded(end);
        path.Add(start);
        for (var index = 1; index <= CurveSteps; index++)
        {
            var fraction = (double)index / CurveSteps;
            var a = Mix(start, first, fraction);
            var b = Mix(first, second, fraction);
            var c = Mix(second, end, fraction);
            path.Add(Mix(Mix(a, b, fraction), Mix(b, c, fraction), fraction));
        }
    }

    private static DiPoint Rounded(DiPoint point) =>
        new(Math.Floor((point.X * 100) + 0.5) / 100, Math.Floor((point.Y * 100) + 0.5) / 100);

    private static DiPoint Unit(DiPoint from, DiPoint to)
    {
        var length = Distance(from, to);
        return length < Epsilon ? new DiPoint(0, 0) : new DiPoint((to.X - from.X) / length, (to.Y - from.Y) / length);
    }

    private static DiPoint Along(DiPoint point, DiPoint direction, double length) =>
        new(point.X + (direction.X * length), point.Y + (direction.Y * length));

    private static DiPoint Mix(DiPoint from, DiPoint to, double fraction) =>
        new(from.X + ((to.X - from.X) * fraction), from.Y + ((to.Y - from.Y) * fraction));

    private static double Distance(DiPoint from, DiPoint to) =>
        Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
}
