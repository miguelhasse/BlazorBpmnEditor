namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>Limits applied when reading BPMN JSON.</summary>
/// <remarks>
/// Both limits exist for the same reason the XML reader prohibits DTDs: a file picked or dropped by
/// a user is untrusted input, and a reader without bounds can be made to exhaust memory or stack
/// before it gets as far as saying the file is not BPMN.
/// </remarks>
public sealed record BpmnJsonReadOptions
{
    /// <summary>The defaults: 64 MiB of input and a nesting depth of 256.</summary>
    public static BpmnJsonReadOptions Default { get; } = new();

    /// <summary>
    /// The largest input accepted, in UTF-8 bytes. Zero or less disables the limit.
    /// </summary>
    /// <remarks>
    /// The default is far above any real diagram — the largest sample this library ships, a
    /// virtualization stress test, is well under a megabyte as JSON — and well below what would
    /// strain a browser tab.
    /// </remarks>
    public long MaxInputBytes { get; init; } = BpmnJsonFormat.DefaultMaxInputBytes;

    /// <summary>
    /// The deepest nesting of JSON objects and arrays accepted.
    /// </summary>
    /// <remarks>
    /// Most of a document is shallow; depth comes from preserved vendor <c>extensionElements</c>,
    /// whose children nest as deep as the XML they were read from. Must be positive.
    /// </remarks>
    public int MaxDepth { get; init; } = BpmnJsonFormat.DefaultMaxDepth;
}

/// <summary>How BPMN JSON is written.</summary>
public sealed record BpmnJsonWriteOptions
{
    /// <summary>Indented output, the default: diffable and readable in a review.</summary>
    public static BpmnJsonWriteOptions Default { get; } = new();

    /// <summary>Compact output, for transport rather than for people.</summary>
    public static BpmnJsonWriteOptions Compact { get; } = new() { Indent = false };

    /// <summary>Whether to indent the output.</summary>
    public bool Indent { get; init; } = true;

    /// <summary>
    /// The deepest nesting written. Writing fails rather than produce a file the reader's default
    /// limit would refuse. Must be positive.
    /// </summary>
    public int MaxDepth { get; init; } = BpmnJsonFormat.DefaultMaxDepth;
}
