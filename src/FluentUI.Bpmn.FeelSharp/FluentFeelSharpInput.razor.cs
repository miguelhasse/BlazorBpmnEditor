using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Owns one generation of native Fluent input without reapplying delayed parent values.</summary>
public partial class FluentFeelSharpInput
{
    private FluentTextArea? _input;

    /// <summary>Native custom-element handle.</summary>
    public ElementReference Element => _input?.Element ?? default;

    /// <summary>Stable control identifier.</summary>
    [Parameter] public string Id { get; set; } = "";
    /// <summary>Initial native value for this generation.</summary>
    [Parameter] public string InitialValue { get; set; } = "";
    /// <summary>Control label.</summary>
    [Parameter] public string Label { get; set; } = "";
    /// <summary>Initial placeholder.</summary>
    [Parameter] public string? Placeholder { get; set; }
    /// <summary>Control height.</summary>
    [Parameter] public string Height { get; set; } = "";
    /// <summary>Read-only state for this generation.</summary>
    [Parameter] public bool IsReadOnly { get; set; }
    /// <summary>Whether native snapshots own the live value.</summary>
    [Parameter] public bool Enhanced { get; set; }
    /// <summary>Advisory or binding-error state.</summary>
    [Parameter] public string? AriaInvalid { get; set; }
    /// <summary>Associated warning and binding-error identifiers.</summary>
    [Parameter] public string? DescribedBy { get; set; }
    /// <summary>Native value callback used when enhancement is unavailable.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    /// <summary>Fallback focus-loss callback.</summary>
    [Parameter] public EventCallback<FocusEventArgs> FocusOut { get; set; }
    /// <summary>Fallback key callback.</summary>
    [Parameter] public EventCallback<KeyboardEventArgs> KeyDown { get; set; }

    private Task DraftInputAsync(ChangeEventArgs args) => ValueChanged.InvokeAsync(args.Value as string);

    // Fluent reapplies Value to its native input on parameter updates, unlike an HTML attribute diff.
    // A keyed generation owns its input until reset; live accessibility is synchronized by the bridge.
    /// <inheritdoc />
    protected override bool ShouldRender() => !Enhanced;
}
