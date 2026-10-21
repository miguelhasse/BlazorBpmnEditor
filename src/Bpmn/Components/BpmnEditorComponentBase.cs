using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic-only base of every component that belongs to an editor: it requires the editor's
/// session and state, and supplies the localizer and the shared presentation parameters.
/// </summary>
/// <remarks>
/// <para>
/// A concrete UI kit derives from the specific base — <see cref="BpmnToolbarBase"/>,
/// <see cref="BpmnContextMenuBase"/> and so on — and supplies markup only. Everything a component
/// needs to show or do comes from <see cref="Session"/> and <see cref="State"/>, so there is no
/// second set of parameters to keep in step with them.
/// </para>
/// <para>
/// Rendered outside a <see cref="BpmnEditorScope"/> the component throws rather than falling
/// back to defaults: a toolbar that silently does nothing is a worse failure than one that says
/// where it must be placed.
/// </para>
/// </remarks>
public abstract class BpmnEditorComponentBase : ComponentBase
{
    private readonly BpmnStateRenderGate _renderGate = new();
    private BpmnEditorState? _previousState;
    private bool _stateOnlyRender;

    [CascadingParameter]
    private BpmnEditorSession? CascadedSession { get; set; }

    [CascadingParameter]
    private BpmnEditorState? CascadedState { get; set; }

    /// <summary>Extra CSS classes for the component's root element.</summary>
    [Parameter]
    public string? Class { get; set; }

    /// <summary>Attributes the component does not declare, for the root element.</summary>
    [Parameter(CaptureUnmatchedValues = true)]
    public IReadOnlyDictionary<string, object>? AdditionalAttributes { get; set; }

    /// <summary>The localizer for the editor's own strings.</summary>
    [Inject]
    protected IStringLocalizer<BpmnStrings> L { get; set; } = default!;

    /// <summary>Semantic display labels, kept separate from invariant snapshot values.</summary>
    protected BpmnDisplayLocalizer Display => field ??= new(L);

    /// <summary>Localizes validation messages; falls back to the diagnostic's own text.</summary>
    [Inject]
    protected BpmnDiagnosticLocalizer DiagnosticLocalizer { get; set; } = default!;

    /// <summary>The editor's commands and read models.</summary>
    protected BpmnEditorSession Session => CascadedSession ?? throw MissingParent();

    /// <summary>The editor's current immutable state.</summary>
    protected BpmnEditorState State => CascadedState ?? throw MissingParent();

    /// <summary>The name used in the error raised when the required parent is absent.</summary>
    protected virtual string ComponentName => GetType().Name;

    /// <summary>Verifies the component is inside an editor before any derived logic runs.</summary>
    /// <exception cref="InvalidOperationException">There is no <see cref="BpmnEditorScope"/> above the component.</exception>
    protected override void OnParametersSet()
    {
        if (CascadedSession is null || CascadedState is null) throw MissingParent();
    }

    /// <summary>Whether a state-only cascade update changes this component's output.</summary>
    /// <remarks>Other parameter updates, local events and asynchronous work still render. Derived
    /// ShouldRender overrides should combine their decision with base.ShouldRender().</remarks>
    /// <param name="previous">The state before parameter assignment.</param>
    /// <param name="current">The newly assigned state.</param>
    /// <returns>True when the component needs to render; the default is conservative.</returns>
    protected virtual bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) => true;

    /// <inheritdoc />
    public override Task SetParametersAsync(ParameterView parameters)
    {
        _previousState = CascadedState;
        _stateOnlyRender = _renderGate.IsStateOnly(parameters, nameof(CascadedState), CascadedState);
        try { return base.SetParametersAsync(parameters); }
        finally { _stateOnlyRender = false; }
    }

    /// <summary>Requests a local render independently of state-cascade suppression.</summary>
    protected new void StateHasChanged()
    {
        _stateOnlyRender = false;
        base.StateHasChanged();
    }

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var stateOnly = _stateOnlyRender;
        _stateOnlyRender = false;
        return !stateOnly || _previousState is null || ShouldRenderOnStateChange(_previousState, State);
    }

    /// <summary>Joins the optional <see cref="Class"/> onto a component's own classes.</summary>
    /// <param name="own">The component's own classes.</param>
    /// <returns>The combined class string.</returns>
    protected string CssClass(string own) => string.IsNullOrWhiteSpace(Class) ? own : $"{own} {Class}";

    /// <summary>The localized string for a key.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The localized text.</returns>
    protected string Text(string key) => L[key];

    /// <summary>The localized, formatted string for a key.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="arguments">The format arguments.</param>
    /// <returns>The localized text.</returns>
    protected string Text(string key, params object[] arguments) => L[key, arguments];

    /// <summary>The localized string for a key, or <paramref name="fallback"/> when there is none.</summary>
    /// <param name="key">The resource key.</param>
    /// <param name="fallback">The text to use when the key is not defined.</param>
    /// <returns>The localized text.</returns>
    protected string TextOr(string key, string fallback)
    {
        var value = L[key];
        return value.ResourceNotFound ? fallback : value.Value;
    }

    private InvalidOperationException MissingParent() => new(
        $"{ComponentName} must be rendered inside a BpmnEditorScope (or a BpmnEditor).");
}
