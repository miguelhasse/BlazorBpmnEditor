using System.Text.Json.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Icons;

/// <summary>One normalized BPMN glyph from the shared icon manifest.</summary>
/// <param name="IconId">Stable identifier, also the key hosts use to override an icon.</param>
/// <param name="UpstreamName">The bpmn-font glyph this was derived from.</param>
/// <param name="CodePoint">The upstream code point, retained for traceability only.</param>
/// <param name="IsInnerSymbol">
/// True when the outer ring or diamond was stripped during generation, so the renderer is still
/// responsible for drawing it (specification sections 4.1 and 4.2).
/// </param>
/// <param name="PathData">SVG path data, already normalized into <paramref name="ViewBox"/>.</param>
/// <param name="ViewBox">The box the path data is expressed in.</param>
/// <param name="Baseline">Where the font baseline falls in that box, for aligning a glyph with text.</param>
/// <param name="Alignment">The glyph's horizontal alignment hint.</param>
/// <param name="AccessibilityLabel">A human-readable name for assistive technology.</param>
public sealed record BpmnIcon(
    string IconId,
    string UpstreamName,
    string? CodePoint,
    bool IsInnerSymbol,
    string PathData,
    string ViewBox,
    double Baseline,
    string Alignment,
    string AccessibilityLabel);

/// <summary>Which glyph renders which BPMN construct.</summary>
/// <remarks>
/// The manifest carries this mapping so the canvas and the stencil cannot disagree about notation
/// (implementation plan section 2). <see cref="Kind"/> selects the family and the remaining
/// members narrow within it; those that do not apply to a family are null.
/// </remarks>
/// <param name="Kind">One of <c>gateway</c>, <c>taskType</c>, <c>activityMarker</c>, <c>eventTrigger</c>.</param>
/// <param name="IconId">The glyph to use.</param>
/// <param name="Type">The flow node type, for gateway and task-type assignments.</param>
/// <param name="Context">The event context, for event-trigger assignments.</param>
/// <param name="Trigger">The event definition, for event-trigger assignments.</param>
/// <param name="Marker">The marker, for activity-marker assignments.</param>
public sealed record BpmnIconAssignment(
    string Kind,
    string IconId,
    string? Type = null,
    string? Context = null,
    string? Trigger = null,
    string? Marker = null);

/// <summary>Where the glyphs came from, and under what licence.</summary>
public sealed record BpmnIconSource(
    string Repository,
    string Package,
    string Version,
    string SourceFile,
    string License,
    string LicenseHolder,
    string Attribution);

/// <summary>The generated icon manifest.</summary>
/// <remarks>
/// Deserialized from the file embedded in this assembly, which is byte-identical to the one the
/// TypeScript bundle imports. That shared origin is the whole point: it is what stops stencil and
/// canvas notation from drifting apart.
/// </remarks>
public sealed record BpmnIconManifest
{
    /// <summary>Manifest schema version.</summary>
    public required string ManifestVersion { get; init; }

    /// <summary>Digest over the icons and assignments, for diagnostics and compatibility checks.</summary>
    public required string ContentHash { get; init; }

    /// <summary>Provenance and licence of the vendored source.</summary>
    public required BpmnIconSource Source { get; init; }

    /// <summary>Every glyph, in stable order.</summary>
    public IReadOnlyList<BpmnIcon> Icons { get; init; } = [];

    /// <summary>The semantic mapping from BPMN constructs onto glyphs.</summary>
    public IReadOnlyList<BpmnIconAssignment> Assignments { get; init; } = [];
}

/// <summary>
/// Source-generated serializer context for the manifest.
/// </summary>
/// <remarks>
/// Reflection-based deserialization is avoided deliberately: the plan's code-quality policy rules
/// it out, and it would also break a consuming WebAssembly app published with trimming.
/// </remarks>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(BpmnIconManifest))]
internal sealed partial class BpmnIconManifestContext : JsonSerializerContext;
