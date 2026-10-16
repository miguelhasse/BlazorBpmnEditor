using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Configuration;

/// <summary>
/// The navigation configuration the host owns.
/// </summary>
/// <remarks>
/// The rejection codes are asserted rather than just the fact of rejection: the TypeScript
/// validator uses the same codes, and a host that reads one from a diagnostic must get the same
/// answer whichever side refused the snapshot.
/// </remarks>
public sealed class NavigationOptionsTests
{
    [Fact]
    public void DefaultSnapshotIsValid() => Assert.Null(EditorConfigValidator.Validate(EditorConfig.CreateDefault()));

    [Fact]
    public void EventEditPolicyDefaultsToReject()
    {
        var policy = EditorConfig.CreateDefault().Editing.EventEditPolicy;

        Assert.Equal(BpmnEventEditPolicy.Reject, policy);
        Assert.Null(EditorConfigValidator.Validate(EditorConfig.CreateDefault()));
    }

    [Fact]
    public void RejectsAnUnknownEventEditPolicy()
    {
        var config = EditorConfig.CreateDefault() with
        {
            Editing = EditingOptions.Default with { EventEditPolicy = (BpmnEventEditPolicy)int.MaxValue },
        };

        var rejection = EditorConfigValidator.Validate(config);

        Assert.NotNull(rejection);
        Assert.Equal("CFG-053", rejection.Code);
        Assert.Equal("Editing.EventEditPolicy", rejection.Path);
    }

    [Fact]
    public void SerializesEventEditPolicyWithTheInteropNames()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var defaultJson = JsonDocument.Parse(JsonSerializer.Serialize(EditorConfig.CreateDefault(), options));
        var autoJson = JsonDocument.Parse(JsonSerializer.Serialize(
            EditorConfig.CreateDefault() with
            {
                Editing = EditingOptions.Default with
                {
                    EventEditPolicy = BpmnEventEditPolicy.AutoAdjustInterrupting,
                },
            },
            options));

        Assert.Equal("reject", defaultJson.RootElement.GetProperty("editing").GetProperty("eventEditPolicy").GetString());
        Assert.Equal(
            "autoAdjustInterrupting",
            autoJson.RootElement.GetProperty("editing").GetProperty("eventEditPolicy").GetString());
    }

    [Fact]
    public void DefaultsBindEveryCommandTheCanvasImplements()
    {
        // A command with no binding is unreachable, and a binding for a command the canvas does
        // not handle does nothing. Every enum member except the ones the chrome owns must appear.
        var bound = KeyboardOptions.Default.Bindings.Select(binding => binding.Command).Distinct().ToList();

        Assert.Equal(Enum.GetValues<BpmnShortcutCommand>().Order(), bound.Order());
    }

    [Fact]
    public void DefaultsWheelToPanning()
    {
        // A wheel event is what a trackpad sends for a two-finger scroll, so panning is what a
        // user expects; Ctrl+wheel zooms regardless.
        Assert.Equal(BpmnWheelMode.Pan, ViewportOptions.Default.WheelMode);
    }

    [Fact]
    public void SnappingToleranceStaysBelowAGridStep()
    {
        // Grid snapping and alignment guides would fight each other if a peer could be captured
        // from further away than the nearest grid line.
        Assert.True(SnappingOptions.Default.Tolerance < GridOptions.Default.Size);
    }

    [Theory]
    [InlineData("CFG-025", "Viewport.WheelPanSpeed")]
    [InlineData("CFG-030", "Keyboard.PanStep")]
    [InlineData("CFG-032", "Keyboard.Bindings[0].Key")]
    [InlineData("CFG-040", "Snapping.Tolerance")]
    public void RejectsInvalidNavigationOptions(string code, string path)
    {
        var config = Invalid(code);

        var rejection = EditorConfigValidator.Validate(config);

        Assert.NotNull(rejection);
        Assert.Equal(code, rejection.Code);
        Assert.Equal(path, rejection.Path);
    }

    [Fact]
    public void AcceptsAZeroToleranceAsSnappingTurnedOff()
    {
        // Zero is a legitimate setting — guides stay enabled but nothing captures — so it must
        // not be rejected alongside the negative values.
        var config = EditorConfig.CreateDefault() with { Snapping = new SnappingOptions(true, 0d) };

        Assert.Null(EditorConfigValidator.Validate(config));
    }

    [Theory]
    [InlineData("+", false, false, false, false, "+")]
    [InlineData("f", false, false, false, false, "F")]
    [InlineData("ArrowLeft", false, false, false, false, "ArrowLeft")]
    [InlineData("0", true, false, false, false, "Ctrl + 0")]
    [InlineData("s", true, true, false, false, "Ctrl + Shift + S")]
    [InlineData("k", false, false, false, true, "Cmd + K")]
    [InlineData("z", false, false, true, false, "Alt + Z")]
    public void RendersTheCombinationAsAReaderWouldWriteIt(
        string key,
        bool ctrl,
        bool shift,
        bool alt,
        bool meta,
        string expected)
    {
        var binding = new ShortcutBinding(BpmnShortcutCommand.ZoomIn, key, ctrl, shift, alt, meta);

        Assert.Equal(expected, binding.DisplayText);
    }

    [Fact]
    public void KeepsANamedKeyIntactWhileUpperCasingAPrintableOne()
    {
        // 'f' reads as 'F' on a key cap, but 'ArrowLeft' is a name and ARROWLEFT is not one.
        Assert.Equal("F", new ShortcutBinding(BpmnShortcutCommand.FitToContent, "f").DisplayText);
        Assert.Equal(
            "ArrowLeft",
            new ShortcutBinding(BpmnShortcutCommand.PanLeft, "ArrowLeft").DisplayText);
    }

    private static EditorConfig Invalid(string code)
    {
        var config = EditorConfig.CreateDefault();

        return code switch
        {
            "CFG-025" => config with { Viewport = config.Viewport with { WheelPanSpeed = 0d } },
            "CFG-030" => config with { Keyboard = KeyboardOptions.Default with { PanStep = 0d } },
            "CFG-032" => config with
            {
                Keyboard = KeyboardOptions.Default with
                {
                    Bindings = [new ShortcutBinding(BpmnShortcutCommand.ZoomIn, string.Empty)],
                },
            },
            "CFG-040" => config with { Snapping = new SnappingOptions(true, -1d) },
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown rejection code."),
        };
    }
}
