using System.Globalization;

namespace Hasseware.AspNetCore.Components.Bpmn.Accessibility;

/// <summary>
/// WCAG contrast ratios, for checking a palette (implementation plan M7).
/// </summary>
/// <remarks>
/// <para>
/// Public because a host may override any theme token, and an override is exactly where a palette
/// stops being the one that was checked. The plan asks for host overrides to be flagged rather than
/// refused — some are deliberately decorative — so this answers the question and leaves the decision
/// to whoever asked.
/// </para>
/// <para>
/// The arithmetic is WCAG 2.2's, which is not the obvious one: channels are linearised before they
/// are weighted, so a naive average of the two colours' brightness gives a different and wrong
/// answer. The thresholds are 4.5 for text, 3.0 for large text and for anything that is not text —
/// a shape's outline against its fill is the second kind.
/// </para>
/// </remarks>
public static class BpmnContrast
{
    /// <summary>The ratio WCAG asks of ordinary text.</summary>
    public const double TextThreshold = 4.5d;

    /// <summary>The ratio WCAG asks of large text, and of anything that is not text.</summary>
    public const double NonTextThreshold = 3.0d;

    /// <summary>
    /// The contrast ratio between two colours, from 1 (identical) to 21 (black on white).
    /// </summary>
    /// <param name="foreground">A CSS hex colour, with or without the leading hash.</param>
    /// <param name="background">The colour behind it.</param>
    /// <exception cref="FormatException">Either colour is not a hex colour this can read.</exception>
    public static double Ratio(string foreground, string background)
    {
        var first = Luminance(foreground);
        var second = Luminance(background);

        var lighter = Math.Max(first, second);
        var darker = Math.Min(first, second);

        return (lighter + 0.05d) / (darker + 0.05d);
    }

    /// <summary>Whether two colours are far enough apart for the given use.</summary>
    /// <param name="foreground">A CSS hex colour.</param>
    /// <param name="background">The colour behind it.</param>
    /// <param name="threshold">One of <see cref="TextThreshold"/> or <see cref="NonTextThreshold"/>.</param>
    public static bool IsSufficient(string foreground, string background, double threshold) =>
        Ratio(foreground, background) >= threshold;

    /// <summary>WCAG relative luminance, which linearises each channel before weighting it.</summary>
    private static double Luminance(string colour)
    {
        var (red, green, blue) = Parse(colour);

        return (0.2126d * Linear(red)) + (0.7152d * Linear(green)) + (0.0722d * Linear(blue));
    }

    private static double Linear(int channel)
    {
        var value = channel / 255d;

        return value <= 0.04045d ? value / 12.92d : Math.Pow((value + 0.055d) / 1.055d, 2.4d);
    }

    /// <summary>Reads <c>#rgb</c>, <c>#rrggbb</c> or either without the hash.</summary>
    private static (int Red, int Green, int Blue) Parse(string colour)
    {
        ArgumentNullException.ThrowIfNull(colour);

        var hex = colour.Trim().TrimStart('#');

        if (hex.Length == 3)
        {
            hex = string.Create(6, hex, (span, source) =>
            {
                for (var index = 0; index < 3; index++)
                {
                    span[index * 2] = source[index];
                    span[(index * 2) + 1] = source[index];
                }
            });
        }

        if (hex.Length != 6)
        {
            throw new FormatException($"'{colour}' is not a hex colour; only hex colours can be checked.");
        }

        return (Channel(hex, 0), Channel(hex, 2), Channel(hex, 4));
    }

    private static int Channel(string hex, int offset) =>
        int.Parse(hex.AsSpan(offset, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
}
