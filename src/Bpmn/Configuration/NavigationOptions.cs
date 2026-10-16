using System.Text.Json.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>What an unmodified mouse wheel does over the canvas.</summary>
/// <remarks>
/// A wheel event is what a trackpad sends for a two-finger scroll, so <see cref="Pan"/> is the
/// default. Holding Ctrl (or Cmd) zooms whichever mode is configured, because that is what a
/// trackpad pinch reports and what every browser already treats as zoom.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnWheelMode>))]
public enum BpmnWheelMode
{
    /// <summary>The wheel scrolls the canvas.</summary>
    [JsonStringEnumMemberName("pan")] Pan,

    /// <summary>The wheel zooms the canvas.</summary>
    [JsonStringEnumMemberName("zoom")] Zoom,
}

/// <summary>A canvas command that a key combination can be bound to.</summary>
/// <remarks>
/// Deliberately small. This is the navigation milestone's vocabulary; editing, selection and
/// clipboard commands join it when those interactions exist, and adding one here without a
/// canvas-side handler would bind a key to nothing.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnShortcutCommand>))]
public enum BpmnShortcutCommand
{
    /// <summary>Zoom in one step.</summary>
    [JsonStringEnumMemberName("zoomIn")] ZoomIn,

    /// <summary>Zoom out one step.</summary>
    [JsonStringEnumMemberName("zoomOut")] ZoomOut,

    /// <summary>Return to 100% at the origin.</summary>
    [JsonStringEnumMemberName("resetZoom")] ResetZoom,

    /// <summary>Scale and centre the drawn content.</summary>
    [JsonStringEnumMemberName("fitToContent")] FitToContent,

    /// <summary>Scroll the view left.</summary>
    [JsonStringEnumMemberName("panLeft")] PanLeft,

    /// <summary>Scroll the view right.</summary>
    [JsonStringEnumMemberName("panRight")] PanRight,

    /// <summary>Scroll the view up.</summary>
    [JsonStringEnumMemberName("panUp")] PanUp,

    /// <summary>Scroll the view down.</summary>
    [JsonStringEnumMemberName("panDown")] PanDown,

    /// <summary>Open the shortcut help surface. Handled by the Blazor chrome, not the canvas.</summary>
    [JsonStringEnumMemberName("showShortcuts")] ShowShortcuts,

    /// <summary>Select every element on the active plane.</summary>
    [JsonStringEnumMemberName("selectAll")] SelectAll,

    /// <summary>Clear the selection.</summary>
    [JsonStringEnumMemberName("clearSelection")] ClearSelection,

    /// <summary>Delete the selection, with everything that cannot outlive it.</summary>
    [JsonStringEnumMemberName("delete")] Delete,

    /// <summary>Undo the last edit.</summary>
    [JsonStringEnumMemberName("undo")] Undo,

    /// <summary>Redo the last undone edit.</summary>
    [JsonStringEnumMemberName("redo")] Redo,

    /// <summary>Put the selection on the clipboard. Not a mutation, so it works read-only.</summary>
    [JsonStringEnumMemberName("copy")] Copy,

    /// <summary>Copy the selection and delete it, as one history entry.</summary>
    [JsonStringEnumMemberName("cut")] Cut,

    /// <summary>Add the clipboard's contents near where they were copied from.</summary>
    [JsonStringEnumMemberName("paste")] Paste,

    /// <summary>Copy and immediately paste the selection, without touching the clipboard.</summary>
    [JsonStringEnumMemberName("duplicate")] Duplicate,
}

/// <summary>
/// One key combination bound to one canvas command.
/// </summary>
/// <remarks>
/// Modifiers are matched exactly rather than as a minimum, so <c>Ctrl+0</c> and
/// <c>Ctrl+Shift+0</c> are different bindings and neither shadows the other.
/// </remarks>
/// <param name="Command">What the combination does.</param>
/// <param name="Key">
/// The DOM <c>KeyboardEvent.key</c> value: a single printable character, matched
/// case-insensitively, or a named key such as <c>ArrowLeft</c>, matched exactly.
/// </param>
/// <param name="Ctrl">Whether Ctrl must be held.</param>
/// <param name="Shift">Whether Shift must be held.</param>
/// <param name="Alt">Whether Alt must be held.</param>
/// <param name="Meta">Whether the platform meta key (Cmd, Windows) must be held.</param>
/// <param name="Description">The text shown in the shortcut help surface.</param>
public sealed record ShortcutBinding(
    BpmnShortcutCommand Command,
    string Key,
    bool Ctrl = false,
    bool Shift = false,
    bool Alt = false,
    bool Meta = false,
    string Description = "")
{
    /// <summary>
    /// The combination as a reader would write it, for the help surface and for tooltips.
    /// </summary>
    /// <remarks>
    /// Built here rather than in the markup so the same text appears everywhere a shortcut is
    /// mentioned, and so a rebound key never disagrees with its own label.
    /// </remarks>
    public string DisplayText
    {
        get
        {
            var parts = new List<string>(5);
            if (Ctrl) parts.Add("Ctrl");
            if (Meta) parts.Add("Cmd");
            if (Alt) parts.Add("Alt");
            if (Shift) parts.Add("Shift");
            parts.Add(Key.Length == 1 ? Key.ToUpperInvariant() : Key);

            return string.Join(" + ", parts);
        }
    }
}

/// <summary>
/// Keyboard navigation options.
/// </summary>
/// <remarks>
/// The host owns every binding. The canvas hard-codes none, which is what the specification's
/// configuration boundary requires — a canvas that bound its own keys would fight whatever the
/// host bound, and silently win or lose depending on listener order.
/// </remarks>
/// <param name="Enabled">Whether keyboard shortcuts are bound at all.</param>
/// <param name="PanStep">Paper units moved per arrow-key press. Must be positive.</param>
/// <param name="Bindings">The bound combinations, in precedence order.</param>
public sealed record KeyboardOptions(bool Enabled, double PanStep, IReadOnlyList<ShortcutBinding> Bindings)
{
    /// <summary>
    /// The conventional defaults: <c>+</c>/<c>-</c> zoom, <c>0</c> resets, <c>F</c> fits, arrows
    /// pan, <c>?</c> opens help.
    /// </summary>
    /// <remarks>
    /// Unmodified single keys, because the canvas only receives them when it has focus, and a
    /// modifier-free <c>+</c> is what every diagram tool uses. The equals key is bound alongside
    /// <c>+</c> because that is the same physical key on most layouts without Shift.
    /// </remarks>
    public static KeyboardOptions Default { get; } = new(
        Enabled: true,
        PanStep: 50d,
        Bindings:
        [
            new(BpmnShortcutCommand.ZoomIn, "+", Description: "Zoom in"),
            new(BpmnShortcutCommand.ZoomIn, "=", Description: "Zoom in"),
            new(BpmnShortcutCommand.ZoomOut, "-", Description: "Zoom out"),
            new(BpmnShortcutCommand.ResetZoom, "0", Description: "Reset zoom to 100%"),
            new(BpmnShortcutCommand.FitToContent, "f", Description: "Fit the diagram to the view"),
            new(BpmnShortcutCommand.PanLeft, "ArrowLeft", Description: "Pan left"),
            new(BpmnShortcutCommand.PanRight, "ArrowRight", Description: "Pan right"),
            new(BpmnShortcutCommand.PanUp, "ArrowUp", Description: "Pan up"),
            new(BpmnShortcutCommand.PanDown, "ArrowDown", Description: "Pan down"),
            new(BpmnShortcutCommand.SelectAll, "a", Ctrl: true, Description: "Select everything"),
            new(BpmnShortcutCommand.ClearSelection, "Escape", Description: "Clear the selection"),
            new(BpmnShortcutCommand.Delete, "Delete", Description: "Delete the selection"),
            new(BpmnShortcutCommand.Undo, "z", Ctrl: true, Description: "Undo"),
            new(BpmnShortcutCommand.Redo, "y", Ctrl: true, Description: "Redo"),
            new(BpmnShortcutCommand.Redo, "z", Ctrl: true, Shift: true, Description: "Redo"),
            new(BpmnShortcutCommand.Copy, "c", Ctrl: true, Description: "Copy the selection"),
            new(BpmnShortcutCommand.Cut, "x", Ctrl: true, Description: "Cut the selection"),
            new(BpmnShortcutCommand.Paste, "v", Ctrl: true, Description: "Paste"),
            new(BpmnShortcutCommand.Duplicate, "d", Ctrl: true, Description: "Duplicate the selection"),
            new(BpmnShortcutCommand.ShowShortcuts, "?", Shift: true, Description: "Show keyboard shortcuts"),
        ]);
}

/// <summary>
/// Alignment guide ("snapline") options.
/// </summary>
/// <remarks>
/// Distinct from <see cref="GridOptions.SnapEnabled"/>, which snaps to fixed grid intervals.
/// These snap to whatever the neighbouring shapes happen to be, which is what makes a diagram
/// look deliberately laid out rather than merely quantised.
/// </remarks>
/// <param name="AlignmentGuidesEnabled">Whether moving a shape shows guides and snaps to peers.</param>
/// <param name="Tolerance">
/// How close two edges must be, in paper units, before they snap. Must not be negative; zero
/// disables snapping without disabling the feature.
/// </param>
public sealed record SnappingOptions(bool AlignmentGuidesEnabled, double Tolerance)
{
    /// <summary>Guides on, with a 6-unit tolerance — under a grid step, so the two do not fight.</summary>
    public static SnappingOptions Default { get; } = new(true, 6d);
}

/// <summary>How the editor handles an event edit that conflicts only with interruption rules.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnEventEditPolicy>))]
public enum BpmnEventEditPolicy
{
    /// <summary>Reject the whole edit and leave the document unchanged.</summary>
    [JsonStringEnumMemberName("reject")] Reject,

    /// <summary>Adjust only the interruption flag when BPMN requires a unique valid value.</summary>
    [JsonStringEnumMemberName("autoAdjustInterrupting")] AutoAdjustInterrupting,
}

/// <summary>
/// Editing behaviour: what the editor enforces while the user changes a diagram.
/// </summary>
/// <param name="PreventCollisions">
/// Whether two peer Flow Nodes may be left overlapping (DI-011). Off means an imported collision
/// stays reportable but not blocking, which is the right answer for a host that inherits diagrams
/// it did not lay out.
/// </param>
/// <param name="CollisionClearance">
/// Extra space, in paper units, required between peer Flow Nodes. Zero permits edge-to-edge
/// touching, which is legal; negative values are refused.
/// </param>
/// <param name="HistoryDepth">How many undo steps to keep. Must be at least one.</param>
public sealed record EditingOptions(bool PreventCollisions, double CollisionClearance, int HistoryDepth)
{
    /// <summary>
    /// Whether a Pool or expanded Sub-Process grows to keep content that was dragged past its edge.
    /// </summary>
    /// <remarks>
    /// On by default because the alternative is worse in both directions: refusing the drag makes
    /// a Pool a wall the user has to resize before working near it, and allowing the node to sit
    /// outside its own container produces a diagram whose geometry contradicts its containment. A
    /// host that lays out Pools itself can turn it off.
    /// </remarks>
    public bool AutoExpandContainers { get; init; } = true;

    /// <summary>How much room to leave past the content when a container grows.</summary>
    public double AutoExpandMargin { get; init; } = 20d;

    /// <summary>How much room to leave around a Sub-Process's content when it is expanded.</summary>
    /// <remarks>
    /// Section 1.2.1 calls for "configured padding" around the child content box, so it is a
    /// setting. The same number as <see cref="AutoExpandMargin"/> by default and deliberately
    /// separate: the two answer different questions, and a host that wants a roomier expanded
    /// container should not have to change how far a Pool grows when something is dragged past
    /// its edge.
    /// </remarks>
    public double SubProcessPadding { get; init; } = 20d;

    /// <summary>The smallest an expanded Sub-Process may be drawn, whatever it holds.</summary>
    /// <remarks>
    /// Section 1.2.1's "minimum size". It matters most when there is nothing to size from — a
    /// Sub-Process whose content has no DI, or none at all — where the content box is empty and
    /// padding alone would give a container too small to drop anything into. The default is what
    /// the canvas creates a new Sub-Process at, so expanding an empty one and dropping a new one
    /// look the same.
    /// </remarks>
    public double MinimumExpandedWidth { get; init; } = 350d;

    /// <inheritdoc cref="MinimumExpandedWidth"/>
    public double MinimumExpandedHeight { get; init; } = 200d;

    /// <summary>Host transforms run after paste id remapping; excluded from the browser configuration.</summary>
    [JsonIgnore]
    public IReadOnlyList<IBpmnPasteTransform> PasteTransforms { get; init; } = [];

    /// <summary>Schema-aware insertion order for newly-created extension children.</summary>
    public IBpmnExtensionChildOrder? ExtensionChildOrder { get; init; }

    /// <summary>The footprint a Sub-Process collapses to.</summary>
    /// <remarks>
    /// A Task's, because that is what section 1.2 says a collapsed Sub-Process looks like: a
    /// rounded rectangle with a "+" marker, indistinguishable in size from the Activities beside
    /// it. Anything larger reads as a container that has not finished collapsing.
    /// </remarks>
    public double CollapsedWidth { get; init; } = 100d;

    /// <inheritdoc cref="CollapsedWidth"/>
    public double CollapsedHeight { get; init; } = 80d;

    /// <summary>
    /// How long a touch must be held before it opens the context menu, in milliseconds.
    /// </summary>
    /// <remarks>
    /// Section 7 calls this "a configured touch long-press", so it is a setting rather than a
    /// constant. Zero turns the gesture off, which is what a host with its own touch handling
    /// wants; the default is the interval Windows and Android both use for a press-and-hold.
    /// </remarks>
    public double LongPressMilliseconds { get; init; } = 500d;

    /// <summary>Whether invalid event interruption edits are rejected or adjusted.</summary>
    public BpmnEventEditPolicy EventEditPolicy { get; init; } = BpmnEventEditPolicy.Reject;

    /// <summary>Collisions blocked with no extra clearance, and a 100-step history.</summary>
    /// <remarks>
    /// Zero clearance because BPMN does not require a gap and a default that did would refuse
    /// layouts other tools produce; the setting exists for hosts that want breathing room.
    /// </remarks>
    public static EditingOptions Default { get; } = new(true, 0d, 100);
}

/// <summary>
/// Which automatic router computes a connector's shape.
/// </summary>
/// <remarks>
/// One router, and deliberately so: JointJS+'s obstacle-avoiding router is not licensable for this
/// library, so the choice was between the core Manhattan router and writing one. The enum exists
/// anyway because the implementation plan asks for a stable router abstraction — a later
/// alternative should be a new member here rather than a change to every command and contract.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnRouterMode>))]
public enum BpmnRouterMode
{
    /// <summary>Orthogonal routing through the JointJS Manhattan router.</summary>
    [JsonStringEnumMemberName("manhattan")] Manhattan,

    /// <summary>
    /// No automatic routing at all.
    /// </summary>
    /// <remarks>
    /// Not a second router — the absence of one. A host that computes its own routes turns ours off
    /// with this rather than having the two disagree about the same line.
    /// </remarks>
    [JsonStringEnumMemberName("straight")] Straight,
}

/// <summary>
/// How connectors are routed (implementation plan, M5).
/// </summary>
/// <remarks>
/// These decide what a connector the <em>editor</em> draws looks like. A connector whose waypoints
/// came from a document is redrawn exactly as the document states them, because sections 10.6 and 11
/// keep imported geometry as the document has it — routing never re-shapes an imported diagram.
/// </remarks>
/// <param name="Mode">Which router to use, or none.</param>
/// <param name="Step">The Manhattan router's grid step, in paper units.</param>
/// <param name="Padding">How far a route keeps clear of the shapes it passes.</param>
public sealed record RoutingOptions(BpmnRouterMode Mode, double Step, double Padding)
{
    /// <summary>
    /// How far apart two connectors between the same pair of shapes are pulled.
    /// </summary>
    /// <remarks>
    /// Zero lets them overlap. The separation moves the route's interior rather than its endpoints:
    /// section 4.6 requires an attachment to sit on the outline, so a nudged endpoint has to be
    /// projected back onto it and the projection undoes most of the nudge. The corridor between the
    /// two attachments is the only part with room to move.
    /// </remarks>
    public double ParallelSeparation { get; init; } = 12d;

    /// <summary>
    /// Whether a new route leaves and enters along the Pool's dominant flow axis.
    /// </summary>
    /// <remarks>
    /// Section 1.5.3 requires auto-routing to branch on <c>isHorizontal</c>, so a connector in a
    /// horizontal Pool reads left-to-right and one in a vertical Pool reads top-to-bottom instead
    /// of whichever way happened to be one step shorter.
    /// </remarks>
    public bool FollowPoolOrientation { get; init; } = true;

    /// <summary>
    /// Manhattan routing on a 10-unit step with 20 units of clearance.
    /// </summary>
    /// <remarks>
    /// The step matches the default grid, so a route and the shapes it joins land on the same
    /// lattice. The clearance is a little over half an Event's diameter: enough that a line passing
    /// a shape reads as passing it rather than touching it.
    /// </remarks>
    public static RoutingOptions Default { get; } = new(BpmnRouterMode.Manhattan, 10d, 20d);
}
