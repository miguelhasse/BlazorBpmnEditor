using System.Text;

using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Storage;

/// <summary>
/// Document paths are the one place naming and traversal rules live (design doc, "Hierarchical ids").
/// </summary>
/// <remarks>
/// The repo has no property-testing library, so the generated corpus at the end is a seeded,
/// deterministic stand-in: the same thousands of hostile strings on every run.
/// </remarks>
public sealed class BpmnDocumentPathTests
{
    // --- what is accepted ----------------------------------------------------------------

    public static TheoryData<string> ValidPaths =>
    [
        "a.bpmn",
        "claims/2026/intake.bpmn",
        "a b/c-d_e.(1).bpmn",
        "folder",
        "x/y",
        "Ünïcödé/файл.bpmn",
        "日本語/図.bpmn",
        "नमस्ते.bpmn",
        "ﺍﻟﻌﺮﺑﻴﺔ.json",
        "v2.0/notes.xml",
    ];

    [Theory]
    [MemberData(nameof(ValidPaths))]
    public void AcceptsPortableNames(string value)
    {
        Assert.True(BpmnDocumentPath.TryParse(value, out var path, out var error), $"{value}: {error}");

        Assert.Equal(BpmnDocumentPathError.None, error);
        Assert.Equal(value.Normalize(NormalizationForm.FormC), path.ToString());
    }

    // --- what is refused, and why ----------------------------------------------------------

    public static TheoryData<string?, BpmnDocumentPathError> InvalidPaths => new()
    {
        { null, BpmnDocumentPathError.Empty },
        { "", BpmnDocumentPathError.Empty },
        { "/a.bpmn", BpmnDocumentPathError.LeadingOrTrailingSeparator },
        { "a/", BpmnDocumentPathError.LeadingOrTrailingSeparator },
        { "a//b.bpmn", BpmnDocumentPathError.EmptySegment },
        { "a/./b.bpmn", BpmnDocumentPathError.DotSegment },
        { "a/../b.bpmn", BpmnDocumentPathError.DotSegment },
        { "..", BpmnDocumentPathError.DotSegment },
        { "a\\b.bpmn", BpmnDocumentPathError.Backslash },
        { "..\\secrets", BpmnDocumentPathError.Backslash },
        { "a:b.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "a*b.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "a?b.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "a\"b.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "a<b>.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "a|b.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "a%b.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "tab\tname.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "line\nbreak.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "nul\0.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "rtl‮gpj.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "zero​width.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "joiner‍.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "bom﻿.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "lone\uD800surrogate.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "́accent-first.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "smile\U0001F600.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { "full／width.bpmn", BpmnDocumentPathError.InvalidCharacter },
        { " leading.bpmn", BpmnDocumentPathError.LeadingOrTrailingSpaceOrDot },
        { "trailing /a.bpmn", BpmnDocumentPathError.LeadingOrTrailingSpaceOrDot },
        { "trailing./a.bpmn", BpmnDocumentPathError.LeadingOrTrailingSpaceOrDot },
        { "CON", BpmnDocumentPathError.ReservedName },
        { "con.bpmn", BpmnDocumentPathError.ReservedName },
        { "folder/Nul.xml", BpmnDocumentPathError.ReservedName },
        { "LPT1.json", BpmnDocumentPathError.ReservedName },
        { "COM9 .bpmn", BpmnDocumentPathError.ReservedName },
        { new string('a', 129), BpmnDocumentPathError.SegmentTooLong },
        { string.Join('/', Enumerable.Repeat(new string('a', 128), 5)), BpmnDocumentPathError.TooLong },
        { string.Join('/', Enumerable.Repeat("a", 17)), BpmnDocumentPathError.TooManySegments },
    };

    [Theory]
    [MemberData(nameof(InvalidPaths))]
    public void RefusesUnsafeOrNonPortableNamesWithAReason(string? value, BpmnDocumentPathError expected)
    {
        Assert.False(BpmnDocumentPath.TryParse(value, out var path, out var error));

        Assert.Equal(expected, error);
        Assert.True(path.IsRoot);
    }

    [Fact]
    public void ParseThrowsAFormatExceptionNamingTheReason()
    {
        var exception = Assert.Throws<FormatException>(() => BpmnDocumentPath.Parse("a/../b.bpmn"));

        Assert.Contains(nameof(BpmnDocumentPathError.DotSegment), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsTheLongestPathAndSegmentAllowed()
    {
        var segment = new string('a', BpmnDocumentPath.MaxSegmentLength);
        var path = string.Join('/', Enumerable.Repeat(new string('b', 31), BpmnDocumentPath.MaxSegments));

        Assert.True(BpmnDocumentPath.TryParse(segment, out _));
        Assert.True(BpmnDocumentPath.TryParse(path, out _));
    }

    // --- normalization and comparison ----------------------------------------------------

    [Fact]
    public void TreatsComposedAndDecomposedSpellingsAsOnePath()
    {
        var composed = BpmnDocumentPath.Parse("café.bpmn");
        var decomposed = BpmnDocumentPath.Parse("café.bpmn");

        Assert.Equal(composed, decomposed);
        Assert.Equal("café.bpmn", decomposed.ToString());
    }

    [Fact]
    public void ComparesCaseSensitively()
    {
        Assert.NotEqual(BpmnDocumentPath.Parse("Order.bpmn"), BpmnDocumentPath.Parse("order.bpmn"));
    }

    [Fact]
    public void KeepsLookalikeLettersFromDifferentScriptsDistinct()
    {
        // Cyrillic "а" and Latin "a" are both letters. They are accepted deterministically as two names,
        // never folded into one, because folding would let one user's document shadow another's.
        var cyrillic = BpmnDocumentPath.Parse("а.bpmn");
        var latin = BpmnDocumentPath.Parse("a.bpmn");

        Assert.NotEqual(latin, cyrillic);
    }

    [Fact]
    public void OrdersOrdinally()
    {
        List<string> unordered = ["b.bpmn", "B.bpmn", "a/z.bpmn", "a.bpmn"];
        var paths = unordered.Select(BpmnDocumentPath.Parse).Order().ToList();

        Assert.Equal(["B.bpmn", "a.bpmn", "a/z.bpmn", "b.bpmn"], paths.Select(path => path.ToString()));
        Assert.True(BpmnDocumentPath.Parse("a.bpmn") < BpmnDocumentPath.Parse("b.bpmn"));
    }

    // --- navigating ----------------------------------------------------------------------

    [Fact]
    public void ExposesNameParentSegmentsAndDepth()
    {
        var path = BpmnDocumentPath.Parse("claims/2026/intake.bpmn");

        Assert.Equal("intake.bpmn", path.Name);
        Assert.Equal("claims/2026", path.Parent.ToString());
        Assert.Equal(["claims", "2026", "intake.bpmn"], path.Segments);
        Assert.Equal(3, path.Depth);
        Assert.True(path.Parent.Parent.Parent.IsRoot);
    }

    [Fact]
    public void TheDefaultIsAUsableRoot()
    {
        var root = default(BpmnDocumentPath);

        Assert.True(root.IsRoot);
        Assert.Equal(BpmnDocumentPath.Root, root);
        Assert.Equal(string.Empty, root.ToString());
        Assert.Equal(string.Empty, root.Name);
        Assert.Empty(root.Segments);
        Assert.Equal(0, root.Depth);
        Assert.True(root.Parent.IsRoot);
        Assert.False(root.IsDocumentName);
    }

    [Fact]
    public void IsUnderMeansStrictlyBeneathAFolder()
    {
        var folder = BpmnDocumentPath.Parse("a");

        Assert.True(BpmnDocumentPath.Parse("a/b.bpmn").IsUnder(folder));
        Assert.True(BpmnDocumentPath.Parse("a/b/c.bpmn").IsUnder(folder));
        Assert.True(BpmnDocumentPath.Parse("a/b.bpmn").IsUnder(BpmnDocumentPath.Root));
        Assert.False(folder.IsUnder(folder));
        Assert.False(BpmnDocumentPath.Parse("ab/c.bpmn").IsUnder(folder));
        Assert.False(BpmnDocumentPath.Root.IsUnder(BpmnDocumentPath.Root));
    }

    [Fact]
    public void CombineAppendsOneValidatedSegment()
    {
        var folder = BpmnDocumentPath.Parse("claims");

        Assert.Equal("claims/intake.bpmn", folder.Combine("intake.bpmn").ToString());
        Assert.Equal("top.bpmn", BpmnDocumentPath.Root.Combine("top.bpmn").ToString());
        Assert.Throws<FormatException>(() => folder.Combine("a/b.bpmn"));
        Assert.Throws<FormatException>(() => folder.Combine(".."));
        Assert.Throws<FormatException>(() => folder.Combine("CON"));
    }

    [Theory]
    [InlineData("a.bpmn", true)]
    [InlineData("a.BPMN", true)]
    [InlineData("folder/a.xml", true)]
    [InlineData("a.json", true)]
    [InlineData("folder", false)]
    [InlineData("a.txt", false)]
    [InlineData(".bpmn", false)]
    public void RecognisesDocumentNamesByExtension(string value, bool expected)
    {
        Assert.Equal(expected, BpmnDocumentPath.Parse(value).IsDocumentName);
    }

    // --- generated corpus ----------------------------------------------------------------

    [Fact]
    public void NeverThrowsAndRoundTripsEveryGeneratedCandidate()
    {
        const string Alphabet = "aZ09 -_.()/\\:*?\"<>|%\t\n\0é́а‮​﻿𐀀日";
        var random = new Random(20261013);

        for (var i = 0; i < 5000; i++)
        {
            var length = random.Next(0, 24);
            var candidate = new string([.. Enumerable.Range(0, length).Select(_ => Alphabet[random.Next(Alphabet.Length)])]);

            if (!BpmnDocumentPath.TryParse(candidate, out var path)) continue;

            var text = path.ToString();
            Assert.Equal(path, BpmnDocumentPath.Parse(text));
            Assert.True(text.IsNormalized(NormalizationForm.FormC), candidate);
            Assert.DoesNotContain('\\', text);
            Assert.DoesNotContain("//", text, StringComparison.Ordinal);
            Assert.All(path.Segments, segment => Assert.False(segment is "." or "..", candidate));
        }
    }
}
