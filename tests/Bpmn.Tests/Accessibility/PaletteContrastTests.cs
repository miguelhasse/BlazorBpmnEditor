using System.Text.RegularExpressions;

using Hasseware.AspNetCore.Components.Bpmn.Accessibility;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Accessibility;

/// <summary>
/// Every shipped palette against WCAG AA (implementation plan M7).
/// </summary>
/// <remarks>
/// <para>
/// Read out of <c>BpmnTheme.css</c> rather than from a copy of the values here, because a copy is a
/// second source of truth that goes stale the first time someone adjusts a hue and this then keeps
/// passing on colours nothing renders.
/// </para>
/// <para>
/// The pairings are the ones that are actually drawn on top of each other: a shape's outline
/// against its own fill and against the paper, a label against the shape it sits in, an execution
/// status against both surfaces it can appear on, and a badge's digits against the badge. Anything
/// that is not text is held to 3:1 and text to 4.5:1, which is what WCAG asks of each.
/// </para>
/// </remarks>
public sealed partial class PaletteContrastTests
{
    public static TheoryData<string> Palettes()
    {
        var data = new TheoryData<string>();

        foreach (var name in Read().Keys) data.Add(name);

        return data;
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void ShapesAreVisibleAgainstWhatTheyAreDrawnOn(string palette)
    {
        var tokens = Read()[palette];

        // A shape's outline is not text, so 3:1 — but it is the line that makes the shape a shape,
        // and a palette where it fades into the paper has no notation left.
        AssertContrast(palette, tokens, "--bpmn-stroke", "--bpmn-fill", BpmnContrast.NonTextThreshold);
        AssertContrast(palette, tokens, "--bpmn-stroke", "--bpmn-canvas-background", BpmnContrast.NonTextThreshold);
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void LabelsAreReadable(string palette)
    {
        var tokens = Read()[palette];

        AssertContrast(palette, tokens, "--bpmn-label-color", "--bpmn-fill", BpmnContrast.TextThreshold);
        AssertContrast(palette, tokens, "--bpmn-label-color", "--bpmn-canvas-background", BpmnContrast.TextThreshold);
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void ExecutionStatusesAreVisibleOnBothSurfacesTheyAppearOn(string palette)
    {
        /*
         * A status outline is drawn over a shape, which sits on the paper — so it has to hold
         * against both. This is also the check that keeps the execution palette from drifting into
         * the semantic one as it is tuned: a colour that has to clear the background twice cannot
         * quietly become the stroke colour.
         */
        var tokens = Read()[palette];

        foreach (var status in new[] { "active", "completed", "failed", "canceled", "waiting", "path" })
        {
            AssertContrast(palette, tokens, $"--bpmn-exec-{status}", "--bpmn-fill", BpmnContrast.NonTextThreshold);
            AssertContrast(palette, tokens, $"--bpmn-exec-{status}", "--bpmn-canvas-background", BpmnContrast.NonTextThreshold);
        }
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void BadgeDigitsAreReadableOnTheirBadge(string palette)
    {
        var tokens = Read()[palette];

        AssertContrast(palette, tokens, "--bpmn-exec-token-text", "--bpmn-exec-token-fill", BpmnContrast.TextThreshold);
        AssertContrast(palette, tokens, "--bpmn-exec-incident-text", "--bpmn-exec-incident-fill", BpmnContrast.TextThreshold);
    }

    [Theory]
    [MemberData(nameof(Palettes))]
    public void SeverityColoursAreReadableOnTheSurfaceTheyAreShownOn(string palette)
    {
        var tokens = Read()[palette];

        AssertContrast(palette, tokens, "--bpmn-error-color", "--bpmn-surface-background", BpmnContrast.TextThreshold);
        AssertContrast(palette, tokens, "--bpmn-warning-color", "--bpmn-surface-background", BpmnContrast.TextThreshold);
    }

    [Fact]
    public void EveryPaletteDefinesEveryTokenTheOthersDo()
    {
        /*
         * A token missing from one palette is not a compile error and does not fail to render — it
         * inherits whatever the base block said, which is a light-theme colour showing up in the
         * dark one. The contrast checks above cannot catch it, because they would be reading the
         * value that leaked in.
         */
        var palettes = Read();
        var expected = palettes["base"].Keys.ToHashSet(StringComparer.Ordinal);

        foreach (var (name, tokens) in palettes)
        {
            if (name == "base") continue;

            var missing = expected.Except(tokens.Keys, StringComparer.Ordinal).ToList();

            Assert.True(missing.Count == 0, $"the '{name}' palette never defines {string.Join(", ", missing)}");
        }
    }

    [Fact]
    public void TheCheckerAgreesWithTheKnownAnswers()
    {
        // Black on white is 21:1 and a colour on itself is 1:1; if those are wrong nothing above
        // means anything.
        Assert.Equal(21d, BpmnContrast.Ratio("#000000", "#ffffff"), 2);
        Assert.Equal(1d, BpmnContrast.Ratio("#7f7f7f", "#7f7f7f"), 2);
        Assert.Equal(BpmnContrast.Ratio("#abc", "#ffffff"), BpmnContrast.Ratio("#aabbcc", "#ffffff"), 6);
        Assert.True(BpmnContrast.IsSufficient("#1a1a1a", "#ffffff", BpmnContrast.TextThreshold));
        Assert.False(BpmnContrast.IsSufficient("#cccccc", "#ffffff", BpmnContrast.NonTextThreshold));
    }

    // --- reading the stylesheet --------------------------------------------------------

    private static void AssertContrast(
        string palette,
        Dictionary<string, string> tokens,
        string foreground,
        string background,
        double threshold)
    {
        Assert.True(tokens.ContainsKey(foreground), $"the '{palette}' palette never defines {foreground}");
        Assert.True(tokens.ContainsKey(background), $"the '{palette}' palette never defines {background}");

        var ratio = BpmnContrast.Ratio(tokens[foreground], tokens[background]);

        Assert.True(
            ratio >= threshold,
            $"in '{palette}', {foreground} ({tokens[foreground]}) on {background} ({tokens[background]}) "
            + $"is {ratio:0.00}:1, below the {threshold:0.0}:1 WCAG AA minimum");
    }

    /// <summary>
    /// Every palette in the stylesheet, as a name and its tokens.
    /// </summary>
    /// <remarks>
    /// A palette is one declaration block. Each is read on its own rather than cascaded, because
    /// each is a complete restatement — which is how the stylesheet is written, and the reason the
    /// missing-token test above is worth having.
    /// </remarks>
    private static Dictionary<string, Dictionary<string, string>> Read()
    {
        var css = File.ReadAllText(Path.Combine(Root(), "src", "Bpmn", "wwwroot", "css", "BpmnTheme.css"));
        var palettes = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        var seen = 0;

        foreach (var block in Blocks().Matches(css).Cast<Match>())
        {
            var tokens = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var declaration in Declarations().Matches(block.Groups["body"].Value).Cast<Match>())
            {
                var value = declaration.Groups["value"].Value.Trim();

                // Only the flat colours. `rgb(0 0 0 / 35%)` is a scrim over unknown content, which
                // is not a foreground-on-background pairing anything can be checked against.
                if (value.StartsWith('#')) tokens[declaration.Groups["name"].Value] = value;
            }

            if (tokens.Count < 5) continue;

            palettes[Name(block.Groups["selector"].Value, seen++)] = tokens;
        }

        return palettes;
    }

    /// <summary>A readable name for a block, so a failure says which palette is wrong.</summary>
    private static string Name(string selector, int ordinal) => selector switch
    {
        _ when selector.Contains("theme='dark'", StringComparison.Ordinal)
            && selector.Contains("contrast='high'", StringComparison.Ordinal) => $"dark + high contrast ({ordinal})",
        _ when selector.Contains("contrast='high'", StringComparison.Ordinal) => $"high contrast ({ordinal})",
        _ when selector.Contains("theme='dark'", StringComparison.Ordinal) => $"dark ({ordinal})",
        _ when selector.Contains("theme='light'", StringComparison.Ordinal) => $"light ({ordinal})",
        _ when selector.Contains(":not(", StringComparison.Ordinal) => $"system dark ({ordinal})",
        _ => ordinal == 0 ? "base" : $"system dark ({ordinal})",
    };

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "playground")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("the repository root was not found");
    }

    [GeneratedRegex(@"(?<selector>[^{}/]+)\{(?<body>[^{}]*--bpmn[^{}]*)\}", RegexOptions.Singleline)]
    private static partial Regex Blocks();

    [GeneratedRegex(@"(?<name>--bpmn-[a-z0-9-]+)\s*:\s*(?<value>[^;]+);")]
    private static partial Regex Declarations();
}
