using Hasseware.AspNetCore.Components.Bpmn.Interop;

namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>Canvas surface options.</summary>
/// <param name="Background">CSS colour of the paper background.</param>
public sealed record CanvasOptions(string Background)
{
    /// <summary>Theme-token-backed default, so a host restyles the canvas without recompiling.</summary>
    public static CanvasOptions Default { get; } = new("var(--bpmn-canvas-background)");
}

/// <summary>Grid rendering and snapping options.</summary>
/// <param name="Size">Grid spacing in paper units. Must be positive.</param>
/// <param name="IsVisible">Whether the grid is drawn.</param>
/// <param name="SnapEnabled">
/// Whether pointer positions are snapped to the grid before being projected onto a shape
/// outline (specification section 4.6). Snapping never reduces an outline to fixed ports.
/// </param>
public sealed record GridOptions(double Size, bool IsVisible, bool SnapEnabled)
{
    /// <summary>10-unit grid, visible, snapping off — the conventional BPMN editor default.</summary>
    public static GridOptions Default { get; } = new(10d, true, false);
}

/// <summary>Zoom and pan bounds.</summary>
/// <param name="MinZoom">Smallest permitted scale.</param>
/// <param name="MaxZoom">Largest permitted scale.</param>
/// <param name="ZoomStep">Multiplier applied per zoom step. Must be greater than 1.</param>
/// <param name="FitPadding">Padding left around the content by fit-to-content.</param>
/// <param name="WheelMode">What an unmodified wheel does. Ctrl or Cmd always zooms.</param>
/// <param name="WheelPanSpeed">Multiplier applied to a wheel delta when panning. Must be positive.</param>
/// <param name="DragPanEnabled">Whether dragging blank paper pans the canvas.</param>
/// <param name="TouchEnabled">
/// Whether touch gestures pan and pinch-zoom the canvas. Enabling it suppresses the browser's own
/// scroll and pinch over the canvas, which is why it is a setting rather than always on.
/// </param>
public sealed record ViewportOptions(
    double MinZoom,
    double MaxZoom,
    double ZoomStep,
    double FitPadding,
    BpmnWheelMode WheelMode,
    double WheelPanSpeed,
    bool DragPanEnabled,
    bool TouchEnabled)
{
    /// <summary>20%-400% in 10% steps, matching common BPMN tooling, with the wheel panning.</summary>
    public static ViewportOptions Default { get; } =
        new(0.2d, 4d, 1.1d, 20d, BpmnWheelMode.Pan, 1d, true, true);
}

/// <summary>
/// The authoritative editor configuration snapshot.
/// </summary>
/// <remarks>
/// Blazor resolves defaults, DI-registered options and component parameters into one immutable,
/// versioned snapshot, and sends it before the canvas is constructed. TypeScript derives its
/// JointJS options from this and never maintains competing public defaults, which is what keeps
/// the canvas from appearing briefly with settings the host did not choose (implementation plan
/// section 2, "Blazor-owned configuration flow").
///
/// <see cref="Revision"/> increases whenever the snapshot changes so a patch can be rejected as
/// stale rather than applied out of order.
/// </remarks>
public sealed record EditorConfig
{
    /// <summary>Interop contract this snapshot targets.</summary>
    public required string ContractVersion { get; init; }

    /// <summary>Monotonic revision of this snapshot.</summary>
    public required int Revision { get; init; }

    /// <summary>
    /// Whether the editor forbids mutation. Selection, inspection, navigation, validation and
    /// export all remain available in read-only mode (implementation plan section 3).
    /// </summary>
    public bool IsReadOnly { get; init; }

    /// <summary>Canvas surface options.</summary>
    public required CanvasOptions Canvas { get; init; }

    /// <summary>Grid rendering and snapping options.</summary>
    public required GridOptions Grid { get; init; }

    /// <summary>Zoom and pan bounds.</summary>
    public required ViewportOptions Viewport { get; init; }

    /// <summary>Keyboard shortcuts and the step they pan by.</summary>
    public required KeyboardOptions Keyboard { get; init; }

    /// <summary>Alignment guide behaviour.</summary>
    public required SnappingOptions Snapping { get; init; }

    /// <summary>What the editor enforces while the user changes a diagram.</summary>
    public required EditingOptions Editing { get; init; }

    /// <summary>How connectors are routed.</summary>
    public required RoutingOptions Routing { get; init; }

    /// <summary>Localized canvas display text, resolved by the canvas before initialization.</summary>
    public CanvasStrings? Strings { get; init; }

    /// <summary>Creates the built-in default snapshot at revision 1.</summary>
    public static EditorConfig CreateDefault() => new()
    {
        ContractVersion = BpmnInteropContract.Version,
        Revision = 1,
        IsReadOnly = false,
        Canvas = CanvasOptions.Default,
        Grid = GridOptions.Default,
        Viewport = ViewportOptions.Default,
        Keyboard = KeyboardOptions.Default,
        Snapping = SnappingOptions.Default,
        Editing = EditingOptions.Default,
        Routing = RoutingOptions.Default,
    };
}
