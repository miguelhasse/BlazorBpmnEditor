using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnEditorSession
{
    private ConfirmationAwaiter? _confirmation;

    /// <summary>The question awaiting the user's answer, or null.</summary>
    public BpmnConfirmationRequest? PendingConfirmation => _confirmation?.Request;

    /// <summary>
    /// Asks the user to allow an agent's action, and completes with their answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One question at a time: while one is pending, another is declined at once rather than queued,
    /// so a user is never faced with a backlog of requests they did not see being made. Cancelling the
    /// token withdraws the question and answers no. Disposal of the editor answers no.
    /// </para>
    /// <para>
    /// Call, and cancel, on the renderer's context, as interop calls arrive. Nothing is applied here;
    /// the caller acts only on a yes.
    /// </para>
    /// </remarks>
    /// <param name="request">The question.</param>
    /// <param name="cancellationToken">Withdraws the question.</param>
    /// <returns>True when the user allowed the action; false when they declined, or the question was withdrawn or refused.</returns>
    /// <exception cref="ArgumentException">The request does not come from an agent.</exception>
    public Task<bool> RequestConfirmationAsync(BpmnConfirmationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.Origin.IsAgent) throw new ArgumentException("Only an agent's action needs the user's confirmation.", nameof(request));

        if (_confirmation is not null || cancellationToken.IsCancellationRequested) return Task.FromResult(false);

        var pending = new ConfirmationAwaiter(request);
        _confirmation = pending;
        pending.Registration = cancellationToken.Register(() => _ = Resolve(pending, answer: false));
        NotifyStateChanged();

        return pending.Answer.Task;
    }

    /// <summary>Answers the pending question; does nothing when there is none.</summary>
    /// <param name="allowed">The user's answer.</param>
    public void ResolveConfirmation(bool allowed)
    {
        if (_confirmation is { } pending) Resolve(pending, allowed);
    }

    private bool Resolve(ConfirmationAwaiter pending, bool answer)
    {
        if (!ReferenceEquals(_confirmation, pending)) return false;

        _confirmation = null;
        pending.Registration.Dispose();
        NotifyStateChanged();
        return pending.Answer.TrySetResult(answer);
    }

    private sealed class ConfirmationAwaiter(BpmnConfirmationRequest request)
    {
        public BpmnConfirmationRequest Request { get; } = request;

        public TaskCompletionSource<bool> Answer { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public CancellationTokenRegistration Registration { get; set; }
    }
}
