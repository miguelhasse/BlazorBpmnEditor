using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Renders the one canvas attached to an editor scope, with its canvas-relative overlays.</summary>
public sealed partial class BpmnEditorCanvas : ComponentBase, IDisposable
{
    [CascadingParameter] private BpmnEditorState State { get; set; } = default!;
    [CascadingParameter] private BpmnEditorSession Session { get; set; } = default!;
    /// <summary>Additional overlays rendered inside the canvas host.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    private readonly BpmnStateRenderGate _renderGate = new();
    private BpmnEditorState? _previousState;
    private bool _stateOnlyRender;
    private bool _inputsChanged;
    private (string? Style, string AriaLabel, EventCallback<ConfigRejected> ConfigRejected,
        EventCallback<EditRejected> EditRejected) _inputs;

    /// <inheritdoc />
    public override Task SetParametersAsync(ParameterView parameters)
    {
        _previousState = State;
        _stateOnlyRender = _renderGate.IsStateOnly(parameters, nameof(State), State);
        try { return base.SetParametersAsync(parameters); }
        finally { _stateOnlyRender = false; }
    }

    /// <inheritdoc />
    protected override bool ShouldRender()
    {
        var stateOnly = _stateOnlyRender;
        _stateOnlyRender = false;
        return !stateOnly || ChildContent is not null || _inputsChanged || _previousState is not { } previous
            || !ReferenceEquals(previous.DrawnDocument, State.DrawnDocument)
            || !ReferenceEquals(previous.Configuration, State.Configuration)
            || previous.ActiveDiagramId != State.ActiveDiagramId
            || previous.Overlays.AccessibleTree.IsOpen != State.Overlays.AccessibleTree.IsOpen;
    }

    private BpmnCanvas? _canvas;
    private BpmnEditorSession? _attachedSession;
    private BpmnCanvas Canvas
    {
        set
        {
            Session.AttachCanvas(value);
            _canvas = value;
            _attachedSession = Session;
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (Session is null)
            throw new InvalidOperationException("BpmnEditorCanvas requires a surrounding BpmnEditorScope.");
        var inputs = (Session.Theme.Style, Session.AriaLabel, Session.OnConfigRejected, Session.OnEditRejected);
        _inputsChanged = !_inputs.Equals(inputs);
        _inputs = inputs;
        if (_attachedSession is not null && !ReferenceEquals(Session, _attachedSession))
            throw new InvalidOperationException("BpmnEditorCanvas cannot change its editor session.");
    }

    /// <summary>Detaches the canvas when this composition is removed.</summary>
    public void Dispose()
    {
        if (_canvas is not null) _attachedSession?.DetachCanvas(_canvas);
    }
}
