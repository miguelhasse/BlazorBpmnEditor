using System.Buffers;
using System.Globalization;
using System.Text;

namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>
/// A validated, normalized, hierarchical name for a stored document or folder, such as
/// <c>claims/2026/intake.bpmn</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is a <em>logical</em> path inside one tenant's document space, never a file-system path, blob
/// URL or database key. Every store, tool and binding goes through this one parser, so traversal and
/// naming rules are implemented, tested and reviewed in exactly one place. Stores still apply their own
/// backend checks as defense in depth.
/// </para>
/// <para>
/// The rules are a contract shared by every store: they may be widened later without breaking stores,
/// but never narrowed, so every backend must be able to hold everything this type admits. That is why
/// the limits are constants rather than options.
/// </para>
/// <para>
/// Segments are separated by <c>/</c>. A segment holds Unicode letters, combining marks and decimal
/// digits, plus space and <c>- _ . ( )</c>, after NFC normalization. Empty, <c>.</c> and <c>..</c>
/// segments, a leading or trailing space or a trailing dot, and Windows device names are refused.
/// Paths compare ordinal and case-sensitive.
/// </para>
/// <para>
/// <see langword="default"/> is the root of the document space: it has no segments and is the parent of
/// every top-level entry.
/// </para>
/// </remarks>
public readonly record struct BpmnDocumentPath : IComparable<BpmnDocumentPath>
{
    private const char Separator = '/';

    private static readonly string[] DocumentExtensions = [".bpmn", ".xml", ".json"];

    private static readonly string[] ReservedNames =
    [
        "CON", "PRN", "AUX", "NUL",
        "COM0", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT0", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    ];

    private readonly string? _value;

    /// <summary>The longest path accepted, in UTF-16 code units after normalization.</summary>
    public const int MaxLength = 512;

    /// <summary>The longest segment accepted, in UTF-16 code units after normalization.</summary>
    public const int MaxSegmentLength = 128;

    /// <summary>The most segments a path may have.</summary>
    public const int MaxSegments = 16;

    private BpmnDocumentPath(string value) => _value = value;

    /// <summary>The root of the document space.</summary>
    public static BpmnDocumentPath Root => default;

    /// <summary>Whether this is the root, which has no segments.</summary>
    public bool IsRoot => _value is null;

    /// <summary>The last segment, or an empty string for the root.</summary>
    public string Name => _value is null ? string.Empty : _value[(_value.LastIndexOf(Separator) + 1)..];

    /// <summary>The containing folder; the root is its own parent.</summary>
    public BpmnDocumentPath Parent
    {
        get
        {
            if (_value is null) return default;

            var index = _value.LastIndexOf(Separator);
            return index < 0 ? default : new BpmnDocumentPath(_value[..index]);
        }
    }

    /// <summary>The segments from the top level down; empty for the root.</summary>
    public IReadOnlyList<string> Segments => _value is null ? [] : _value.Split(Separator);

    /// <summary>The number of segments; zero for the root.</summary>
    public int Depth => _value is null ? 0 : _value.Count(c => c == Separator) + 1;

    /// <summary>
    /// Whether the last segment names a document: it ends in <c>.bpmn</c>, <c>.xml</c> or <c>.json</c>,
    /// ignoring case.
    /// </summary>
    /// <remarks>
    /// A folder is a path too, so the extension rule is not part of path validity; document operations
    /// check it through <see cref="BpmnDocumentKey"/>.
    /// </remarks>
    public bool IsDocumentName
    {
        get
        {
            var name = Name;
            return DocumentExtensions.Any(extension =>
                name.Length > extension.Length && name.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>Parses a path, throwing when it is not valid.</summary>
    /// <param name="value">The path, such as <c>claims/2026/intake.bpmn</c>.</param>
    /// <returns>The normalized path.</returns>
    /// <exception cref="FormatException">The value is not a valid document path.</exception>
    public static BpmnDocumentPath Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return TryParse(value, out var path, out var error)
            ? path
            : throw new FormatException($"'{value}' is not a valid document path: {error}.");
    }

    /// <summary>Parses a path.</summary>
    /// <param name="value">The path, such as <c>claims/2026/intake.bpmn</c>.</param>
    /// <param name="path">The normalized path when parsing succeeded; otherwise the root.</param>
    /// <returns>Whether the value is a valid, non-root path.</returns>
    public static bool TryParse(string? value, out BpmnDocumentPath path) => TryParse(value, out path, out _);

    /// <summary>Parses a path, reporting why it was refused.</summary>
    /// <param name="value">The path, such as <c>claims/2026/intake.bpmn</c>.</param>
    /// <param name="path">The normalized path when parsing succeeded; otherwise the root.</param>
    /// <param name="error">Why the value was refused, or <see cref="BpmnDocumentPathError.None"/>.</param>
    /// <returns>Whether the value is a valid, non-root path.</returns>
    public static bool TryParse(string? value, out BpmnDocumentPath path, out BpmnDocumentPathError error)
    {
        path = default;
        error = Validate(value, out var normalized);
        if (error != BpmnDocumentPathError.None) return false;

        path = new BpmnDocumentPath(normalized!);
        return true;
    }

    /// <summary>Appends one segment.</summary>
    /// <param name="segment">A single segment; it may not contain <c>/</c>.</param>
    /// <returns>The longer path.</returns>
    /// <exception cref="FormatException">The segment, or the resulting path, is not valid.</exception>
    public BpmnDocumentPath Combine(string segment)
    {
        ArgumentNullException.ThrowIfNull(segment);

        if (segment.Contains(Separator, StringComparison.Ordinal))
        {
            throw new FormatException($"'{segment}' is not a single path segment.");
        }

        return Parse(_value is null ? segment : _value + Separator + segment);
    }

    /// <summary>Whether this path lies strictly beneath a folder.</summary>
    /// <param name="folder">The folder; the root contains every other path.</param>
    /// <returns>True for descendants of <paramref name="folder"/>; false for the folder itself.</returns>
    public bool IsUnder(BpmnDocumentPath folder)
    {
        if (_value is null) return false;
        if (folder._value is null) return true;

        return _value.Length > folder._value.Length + 1
            && _value[folder._value.Length] == Separator
            && _value.StartsWith(folder._value, StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="left"/> sorts before <paramref name="right"/>, ordinally.</summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns>The ordinal comparison.</returns>
    public static bool operator <(BpmnDocumentPath left, BpmnDocumentPath right) => left.CompareTo(right) < 0;

    /// <summary>Whether <paramref name="left"/> sorts before or equal to <paramref name="right"/>, ordinally.</summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns>The ordinal comparison.</returns>
    public static bool operator <=(BpmnDocumentPath left, BpmnDocumentPath right) => left.CompareTo(right) <= 0;

    /// <summary>Whether <paramref name="left"/> sorts after <paramref name="right"/>, ordinally.</summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns>The ordinal comparison.</returns>
    public static bool operator >(BpmnDocumentPath left, BpmnDocumentPath right) => left.CompareTo(right) > 0;

    /// <summary>Whether <paramref name="left"/> sorts after or equal to <paramref name="right"/>, ordinally.</summary>
    /// <param name="left">The first path.</param>
    /// <param name="right">The second path.</param>
    /// <returns>The ordinal comparison.</returns>
    public static bool operator >=(BpmnDocumentPath left, BpmnDocumentPath right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public int CompareTo(BpmnDocumentPath other) => string.CompareOrdinal(_value, other._value);

    /// <summary>The path as text; an empty string for the root.</summary>
    /// <returns>The normalized path.</returns>
    public override string ToString() => _value ?? string.Empty;

    private static BpmnDocumentPathError Validate(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrEmpty(value)) return BpmnDocumentPathError.Empty;

        // Checked before normalizing, so a refusal never depends on what normalization makes of a
        // backslash or a control character.
        if (value.Contains('\\', StringComparison.Ordinal)) return BpmnDocumentPathError.Backslash;
        if (value[0] == Separator || value[^1] == Separator) return BpmnDocumentPathError.LeadingOrTrailingSeparator;

        var error = Normalize(value, out var text);
        if (error != BpmnDocumentPathError.None) return error;

        if (text.Length > MaxLength) return BpmnDocumentPathError.TooLong;

        var segments = text.Split(Separator);
        if (segments.Length > MaxSegments) return BpmnDocumentPathError.TooManySegments;

        foreach (var segment in segments)
        {
            error = ValidateSegment(segment);
            if (error != BpmnDocumentPathError.None) return error;
        }

        normalized = text;
        return BpmnDocumentPathError.None;
    }

    private static BpmnDocumentPathError Normalize(string value, out string normalized)
    {
        normalized = value;
        if (Ascii.IsValid(value)) return BpmnDocumentPathError.None;

        try
        {
            normalized = value.Normalize(NormalizationForm.FormC);
            return BpmnDocumentPathError.None;
        }
        catch (ArgumentException)
        {
            // An unpaired surrogate cannot be normalized, and is not a character a store could hold.
            return BpmnDocumentPathError.InvalidCharacter;
        }
        catch (PlatformNotSupportedException)
        {
            // Hosts running with invariant globalization cannot normalize non-ASCII text, and accepting
            // it unnormalized would let two spellings of one name become two documents.
            return BpmnDocumentPathError.NormalizationUnavailable;
        }
    }

    private static BpmnDocumentPathError ValidateSegment(string segment)
    {
        if (segment.Length == 0) return BpmnDocumentPathError.EmptySegment;
        if (segment is "." or "..") return BpmnDocumentPathError.DotSegment;
        if (segment.Length > MaxSegmentLength) return BpmnDocumentPathError.SegmentTooLong;

        var index = 0;
        while (index < segment.Length)
        {
            if (Rune.DecodeFromUtf16(segment.AsSpan(index), out var rune, out var consumed) != OperationStatus.Done)
            {
                return BpmnDocumentPathError.InvalidCharacter;
            }

            if (!IsAllowed(rune, isFirst: index == 0)) return BpmnDocumentPathError.InvalidCharacter;
            index += consumed;
        }

        if (segment[0] == ' ' || segment[^1] is ' ' or '.') return BpmnDocumentPathError.LeadingOrTrailingSpaceOrDot;
        if (IsReservedName(segment)) return BpmnDocumentPathError.ReservedName;

        return BpmnDocumentPathError.None;
    }

    private static bool IsAllowed(Rune rune, bool isFirst)
    {
        if (rune.Value is ' ' or '-' or '_' or '.' or '(' or ')') return true;

        return Rune.GetUnicodeCategory(rune) switch
        {
            UnicodeCategory.UppercaseLetter
                or UnicodeCategory.LowercaseLetter
                or UnicodeCategory.TitlecaseLetter
                or UnicodeCategory.ModifierLetter
                or UnicodeCategory.OtherLetter
                or UnicodeCategory.DecimalDigitNumber => true,

            // Combining marks are part of many scripts' letters, but a segment that starts with one
            // has nothing to combine with.
            UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark => !isFirst,

            _ => false,
        };
    }

    private static bool IsReservedName(string segment)
    {
        var dot = segment.IndexOf('.', StringComparison.Ordinal);
        var stem = (dot < 0 ? segment : segment[..dot]).TrimEnd(' ');

        return ReservedNames.Contains(stem, StringComparer.OrdinalIgnoreCase);
    }
}

/// <summary>Why a value is not a valid <see cref="BpmnDocumentPath"/>.</summary>
public enum BpmnDocumentPathError
{
    /// <summary>The value is valid.</summary>
    None,

    /// <summary>The value is null or empty.</summary>
    Empty,

    /// <summary>The value contains a backslash, which is never a separator.</summary>
    Backslash,

    /// <summary>The value starts or ends with <c>/</c>.</summary>
    LeadingOrTrailingSeparator,

    /// <summary>The value is longer than <see cref="BpmnDocumentPath.MaxLength"/>.</summary>
    TooLong,

    /// <summary>The value has more than <see cref="BpmnDocumentPath.MaxSegments"/> segments.</summary>
    TooManySegments,

    /// <summary>The value contains <c>//</c>.</summary>
    EmptySegment,

    /// <summary>A segment is <c>.</c> or <c>..</c>; relative segments are refused, not resolved.</summary>
    DotSegment,

    /// <summary>A segment is longer than <see cref="BpmnDocumentPath.MaxSegmentLength"/>.</summary>
    SegmentTooLong,

    /// <summary>A segment contains a character outside the allowed set.</summary>
    InvalidCharacter,

    /// <summary>A segment starts with a space, or ends with a space or a dot.</summary>
    LeadingOrTrailingSpaceOrDot,

    /// <summary>A segment is a Windows device name such as <c>CON</c> or <c>NUL.bpmn</c>.</summary>
    ReservedName,

    /// <summary>
    /// The value contains non-ASCII text and the host cannot normalize it, typically because it runs
    /// with invariant globalization.
    /// </summary>
    NormalizationUnavailable,
}
