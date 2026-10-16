using System.Collections.Frozen;
using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Icons;

/// <summary>
/// Resolves BPMN glyphs from the shared icon manifest.
/// </summary>
/// <remarks>
/// <para>
/// The manifest is embedded in this assembly, so a consuming application needs no webfont, no
/// bpmn-io package and no network access (specification section 4.5). It is the same file the
/// TypeScript bundle imports, which is what guarantees the stencil and the canvas draw the same
/// notation.
/// </para>
/// <para>
/// Loaded once and cached: the file is small, but a stencil asks for dozens of glyphs while it
/// renders and parsing per lookup would be wasteful.
/// </para>
/// </remarks>
public sealed class BpmnIconRegistry
{
    private const string ResourceName = "Hasseware.AspNetCore.Components.Bpmn.bpmn-icons.json";

    private static readonly Lazy<BpmnIconRegistry> LazyDefault = new(Load, isThreadSafe: true);

    private readonly FrozenDictionary<string, BpmnIcon> _icons;
    private readonly FrozenDictionary<string, string> _gateways;
    private readonly FrozenDictionary<string, string> _taskTypes;
    private readonly FrozenDictionary<string, string> _activityMarkers;
    private readonly FrozenDictionary<string, string> _eventTriggers;

    private BpmnIconRegistry(BpmnIconManifest manifest)
    {
        Manifest = manifest;

        _icons = manifest.Icons.ToFrozenDictionary(icon => icon.IconId, StringComparer.Ordinal);

        FrozenDictionary<string, string> Index(string kind, Func<BpmnIconAssignment, string?> key) =>
            manifest.Assignments
                .Where(assignment => assignment.Kind == kind && key(assignment) is not null)
                .ToFrozenDictionary(assignment => key(assignment)!, assignment => assignment.IconId, StringComparer.Ordinal);

        _gateways = Index("gateway", assignment => assignment.Type);
        _taskTypes = Index("taskType", assignment => assignment.Type);
        _activityMarkers = Index("activityMarker", assignment => assignment.Marker);
        _eventTriggers = Index("eventTrigger", assignment => $"{assignment.Context}/{assignment.Trigger}");
    }

    /// <summary>The registry over the manifest embedded in this assembly.</summary>
    public static BpmnIconRegistry Default => LazyDefault.Value;

    /// <summary>The manifest this registry was built from.</summary>
    public BpmnIconManifest Manifest { get; }

    /// <summary>Every glyph, in manifest order.</summary>
    public IReadOnlyList<BpmnIcon> Icons => Manifest.Icons;

    /// <summary>Finds a glyph by id, or returns <see langword="null"/>.</summary>
    public BpmnIcon? Find(string iconId) => _icons.GetValueOrDefault(iconId);

    /// <summary>The inner glyph for a Gateway, or <see langword="null"/> for an unmarked one.</summary>
    public BpmnIcon? ForGateway(BpmnFlowNodeType type) => Lookup(_gateways, WireName(type));

    /// <summary>
    /// The top-left type symbol for a Task, or <see langword="null"/> when it has none.
    /// </summary>
    /// <remarks>
    /// A generic Task has no symbol by definition, and a Call Activity is denoted by its thick
    /// border rather than a glyph (specification section 4.4).
    /// </remarks>
    public BpmnIcon? ForTaskType(BpmnFlowNodeType type) => Lookup(_taskTypes, WireName(type));

    /// <summary>A bottom-centre Activity marker (specification section 4.3).</summary>
    public BpmnIcon? ForActivityMarker(string marker) => Lookup(_activityMarkers, marker);

    /// <summary>
    /// The trigger glyph for an Event.
    /// </summary>
    /// <param name="context">
    /// One of <c>start</c>, <c>startNonInterrupting</c>, <c>intermediateCatch</c>,
    /// <c>intermediateCatchNonInterrupting</c>, <c>intermediateThrow</c>, <c>end</c>.
    /// </param>
    /// <param name="definition">The event definition; <c>none</c> has no glyph.</param>
    public BpmnIcon? ForEventTrigger(string context, BpmnEventDefinitionType definition) =>
        definition == BpmnEventDefinitionType.None
            ? null
            : Lookup(_eventTriggers, $"{context}/{WireName(definition)}");

    private BpmnIcon? Lookup(FrozenDictionary<string, string> index, string key) =>
        index.TryGetValue(key, out var iconId) ? Find(iconId) : null;

    /// <summary>
    /// The camelCase name an enum member serializes as, which is also the key the manifest uses.
    /// </summary>
    /// <remarks>
    /// Derived from the member name rather than read back through the serializer: the manifest is
    /// generated against these exact spellings, and going through JSON to recover a string this
    /// assembly already knows would be indirection for its own sake.
    /// </remarks>
    private static string WireName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        return string.Create(name.Length, name, static (span, source) =>
        {
            source.AsSpan().CopyTo(span);
            span[0] = char.ToLowerInvariant(span[0]);
        });
    }

    private static BpmnIconRegistry Load()
    {
        using var stream = typeof(BpmnIconRegistry).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"The embedded icon manifest '{ResourceName}' is missing from "
                + $"{typeof(BpmnIconRegistry).Assembly.GetName().Name}. The build did not embed icons/bpmn-icons.json.");

        var manifest = JsonSerializer.Deserialize(stream, BpmnIconManifestContext.Default.BpmnIconManifest)
            ?? throw new InvalidOperationException("The embedded icon manifest could not be read.");

        if (manifest.Icons.Count == 0)
        {
            throw new InvalidOperationException("The embedded icon manifest contains no icons.");
        }

        return new BpmnIconRegistry(manifest);
    }
}
