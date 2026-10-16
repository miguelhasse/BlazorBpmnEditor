using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// Execution overlays (implementation plan M6.5).
/// </summary>
/// <remarks>
/// <para>
/// A host watching a running instance pushes a whole state and the canvas draws it over the
/// diagram. Nothing here touches the document: an execution state is an observation about an
/// engine, not a fact about the model, so it never enters history, never makes the editor dirty
/// and is never written to XML.
/// </para>
/// <para>
/// Which is also why it all works in read-only. Section 7 asks read-only to keep everything except
/// mutation, and watching a process run is the case the mode exists for.
/// </para>
/// </remarks>
public sealed partial class BpmnEditorSession
{
    /// <summary>
    /// The last state pushed, kept so a rebuilt canvas can be told again.
    /// </summary>
    /// <remarks>
    /// The canvas redraws its own overlay after every render, so this is not about diagram
    /// switches. It is about the canvas being replaced entirely — a disposed and re-created
    /// component starts with no state, and an overlay that vanished on a re-render the host did
    /// not cause would look like a dropped update from the engine.
    /// </remarks>
    private BpmnExecutionState? _execution;

    /// <summary>The execution state currently drawn, or <see langword="null"/> when none is.</summary>
    public BpmnExecutionState? ExecutionState => _execution;

    /// <summary>
    /// Draws an engine's view of a running instance over the diagram.
    /// </summary>
    /// <remarks>
    /// A whole snapshot each time, not a delta. An overlay that accumulated patches would drift
    /// from the engine the first time one was lost, and a host that can describe a change can
    /// describe the state it produced.
    /// </remarks>
    /// <param name="state">The state to draw.</param>
    public async Task SetExecutionStateAsync(BpmnExecutionState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        _execution = state;
        NotifyStateChanged();

        if (_canvas is not null)
        {
            await _canvas.SetExecutionStateAsync(state).ConfigureAwait(true);
        }
    }

    /// <summary>Removes every execution overlay.</summary>
    public async Task ClearExecutionStateAsync()
    {
        _execution = null;
        NotifyStateChanged();

        if (_canvas is not null)
        {
            await _canvas.ClearExecutionStateAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Re-pushes the overlay to a canvas that has just come up, then tells the host.
    /// </summary>
    /// <remarks>
    /// The host's callback runs last so that by the time it does, the canvas is in the state this
    /// component believes it is in — a host that reacts by reading <see cref="ExecutionState"/>
    /// should not be looking at a canvas that has not been told about it yet.
    /// </remarks>
    internal async Task HandleCanvasInitializedAsync(CanvasInitialized payload)
    {
        if (_execution is { } state && _canvas is not null)
        {
            await _canvas.SetExecutionStateAsync(state).ConfigureAwait(true);
        }

        await OnCanvasInitialized.InvokeAsync(payload).ConfigureAwait(true);
    }
}
