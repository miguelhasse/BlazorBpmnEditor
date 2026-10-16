using Hasseware.AspNetCore.Components.Bpmn.Interop;

namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>
/// Validates a configuration snapshot before it crosses interop.
/// </summary>
/// <remarks>
/// The TypeScript side validates the same constraints again and refuses anything invalid — it
/// must, because it cannot trust its caller. Checking here as well means a host's own mistake
/// surfaces as a typed .NET failure at the point of configuration rather than as a rejection
/// event arriving asynchronously from the browser.
/// </remarks>
public static class EditorConfigValidator
{
    /// <summary>
    /// Returns the first violated constraint, or <see langword="null"/> when the snapshot is
    /// valid. Codes match the TypeScript validator so both sides report the same diagnostic.
    /// </summary>
    public static ConfigRejected? Validate(EditorConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        if (config.ContractVersion != BpmnInteropContract.Version)
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-001",
                nameof(EditorConfig.ContractVersion),
                $"Configuration targets interop contract '{config.ContractVersion}' but this assembly implements '{BpmnInteropContract.Version}'.");
        }

        if (config.Revision < 0)
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-002",
                nameof(EditorConfig.Revision),
                "Configuration revision must be a non-negative number.");
        }

        if (config.Grid.Size <= 0d || !double.IsFinite(config.Grid.Size))
        {
            return new ConfigRejected(config.Revision, "CFG-010", "Grid.Size", "Grid size must be a positive number.");
        }

        if (config.Viewport.MinZoom <= 0d || !double.IsFinite(config.Viewport.MinZoom))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-020",
                "Viewport.MinZoom",
                "Minimum zoom must be greater than zero.");
        }

        if (config.Viewport.MaxZoom < config.Viewport.MinZoom || !double.IsFinite(config.Viewport.MaxZoom))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-021",
                "Viewport.MaxZoom",
                "Maximum zoom must be greater than or equal to the minimum zoom.");
        }

        if (config.Viewport.ZoomStep <= 1d || !double.IsFinite(config.Viewport.ZoomStep))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-022",
                "Viewport.ZoomStep",
                "Zoom step is a multiplier and must be greater than 1.");
        }

        if (config.Viewport.FitPadding < 0d || !double.IsFinite(config.Viewport.FitPadding))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-023",
                "Viewport.FitPadding",
                "Fit padding must not be negative.");
        }

        if (config.Viewport.WheelPanSpeed <= 0d || !double.IsFinite(config.Viewport.WheelPanSpeed))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-025",
                "Viewport.WheelPanSpeed",
                "Wheel pan speed must be greater than zero.");
        }

        if (config.Keyboard.PanStep <= 0d || !double.IsFinite(config.Keyboard.PanStep))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-030",
                "Keyboard.PanStep",
                "Keyboard pan step must be greater than zero.");
        }

        for (var index = 0; index < config.Keyboard.Bindings.Count; index++)
        {
            if (!string.IsNullOrEmpty(config.Keyboard.Bindings[index].Key)) continue;

            return new ConfigRejected(
                config.Revision,
                "CFG-032",
                $"Keyboard.Bindings[{index}].Key",
                "A shortcut binding must name a key.");
        }

        if (config.Snapping.Tolerance < 0d || !double.IsFinite(config.Snapping.Tolerance))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-040",
                "Snapping.Tolerance",
                "Snapping tolerance must not be negative.");
        }

        if (config.Editing.CollisionClearance < 0d || !double.IsFinite(config.Editing.CollisionClearance))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-050",
                "Editing.CollisionClearance",
                "Collision clearance must not be negative.");
        }

        if (config.Editing.HistoryDepth < 1)
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-051",
                "Editing.HistoryDepth",
                "History depth must be at least one.");
        }

        if (!Enum.IsDefined(config.Editing.EventEditPolicy))
        {
            return new ConfigRejected(
                config.Revision,
                "CFG-053",
                "Editing.EventEditPolicy",
                "Event edit policy must be 'Reject' or 'AutoAdjustInterrupting'.");
        }

        return null;
    }
}
