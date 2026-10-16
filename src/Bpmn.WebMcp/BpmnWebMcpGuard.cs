using System.Text.Json;

using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Tools;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

/// <summary>Asks the user whether an agent may go ahead; the editor's confirmation prompt answers.</summary>
/// <param name="request">The question.</param>
/// <param name="cancellationToken">Withdraws the question when the agent gives up.</param>
/// <returns>True when the user allowed it.</returns>
public delegate Task<bool> BpmnWebMcpConfirm(BpmnConfirmationRequest request, CancellationToken cancellationToken);

/// <summary>
/// What stands between an agent and a change: the checks every tool that changes the diagram passes before
/// its handler runs.
/// </summary>
/// <remarks>
/// <para>
/// In order: a document that moved in storage behind the editor is refused (the user decides what to do about
/// it, never the agent); then a consequential tool asks the user, with the module's own words as the question
/// and diagram text only as quoted details. Annotations are hints an agent may ignore, so this is where the
/// rules are enforced. A tool that changes anything and reaches a bridge without a guard is refused.
/// </para>
/// <para>Use on the renderer's context, like the session it asks.</para>
/// </remarks>
public sealed class BpmnWebMcpGuard
{
    private const int MaxDetails = 8;
    private const int MaxNameLength = 60;

    private readonly bool _confirmConsequential;
    private readonly bool _confirmPersistence;
    private readonly BpmnWebMcpConfirm? _confirm;
    private readonly Func<string, object[], string> _text;
    private readonly Func<bool>? _questionOpen;

    /// <summary>Creates a guard.</summary>
    /// <param name="confirmConsequential">Whether deleting asks the user.</param>
    /// <param name="confirmPersistence">Whether saving asks the user.</param>
    /// <param name="confirm">How to ask; null when there is no way to, which refuses what would have asked.</param>
    /// <param name="text">Localized text by key, with arguments.</param>
    /// <param name="questionOpen">Whether a question to the user is already open; a second is answered <see cref="BpmnToolCodes.Busy"/> instead of being refused as if the user had declined it.</param>
    public BpmnWebMcpGuard(bool confirmConsequential, bool confirmPersistence, BpmnWebMcpConfirm? confirm, Func<string, object[], string> text, Func<bool>? questionOpen = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        _confirmConsequential = confirmConsequential;
        _confirmPersistence = confirmPersistence;
        _confirm = confirm;
        _text = text;
        _questionOpen = questionOpen;
    }

    /// <summary>Checks a call to a tool that changes the diagram.</summary>
    /// <param name="host">The document.</param>
    /// <param name="tool">The catalog tool being called.</param>
    /// <param name="arguments">Its arguments, already known to be a JSON object.</param>
    /// <param name="origin">The agent asking.</param>
    /// <param name="cancellationToken">Cancels the call, and withdraws a question the user has not answered.</param>
    /// <returns>A refusal to return to the agent, or null when the call may go ahead.</returns>
    public async ValueTask<BpmnToolResult?> CheckAsync(
        IBpmnToolHost host,
        BpmnToolDescriptor tool,
        JsonElement arguments,
        BpmnEditOrigin origin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(tool);

        // Showing the user something changes no content, so it works on a document that has gone stale.
        if (tool.Name != "bpmn_select" && StaleRefusal(host) is { } stale) return stale;

        if (!tool.Annotations.Destructive) return null;

        var persistence = tool.Name == BpmnWebMcpToolSet.PersistenceToolSave;
        if (!(persistence ? _confirmPersistence : _confirmConsequential)) return null;

        if (_confirm is null) return Refusal(host, BpmnToolCodes.Denied, "There is no way to ask the user, so this was not done.");

        IReadOnlyList<string> ids = [];
        if (!persistence)
        {
            // A call the tool will refuse anyway is not worth putting to the user, and neither is one that is too big to run.
            if (!BpmnToolJson.TryRead<BpmnDeleteRequest>(arguments, out var request) || request!.ElementIds.Count == 0) return null;
            if (request.ElementIds.Count > host.Limits.MaxBatch) return Refusal(host, BpmnToolCodes.TooLarge, "That is more elements than one call may delete.");

            ids = request.ElementIds;
        }

        // Not "the user said no": they were never asked, and the agent should try again once the question in front of them is answered.
        if (_questionOpen?.Invoke() == true) return Refusal(host, BpmnToolCodes.Busy, "The user is already being asked another question. Nothing was done; try again after it has been answered.");

        var question = persistence ? SaveQuestion(host, origin) : await DeleteQuestionAsync(host, ids, origin, cancellationToken).ConfigureAwait(true);
        var allowed = await _confirm(question, cancellationToken).ConfigureAwait(true);
        cancellationToken.ThrowIfCancellationRequested();

        if (!allowed) return Refusal(host, BpmnToolCodes.Denied, "The user did not allow this, and nothing was changed. Do not ask again unless the user does.");

        // The user took their time, and the stored document may have moved while they did: what they allowed was a change to the one they were shown.
        return StaleRefusal(host);
    }

    private static BpmnToolResult? StaleRefusal(IBpmnToolHost host) =>
        host is IBpmnPersistenceHost { IsBound: true, ExternalChange: not null }
            ? Refusal(host, BpmnToolCodes.StaleDocument,
                "The stored document changed behind the editor. The user must choose Reload, Save as copy or Dismiss in the editor before anything can change.")
            : null;

    private BpmnConfirmationRequest SaveQuestion(IBpmnToolHost host, BpmnEditOrigin origin)
    {
        var name = (host as IBpmnPersistenceHost)?.DocumentName;
        return new BpmnConfirmationRequest(
            _text("AgentConfirmation.Save.Title", []),
            _text("AgentConfirmation.Save.Summary", []),
            origin,
            name is null ? [] : [name],
            IsDestructive: true);
    }

    private async ValueTask<BpmnConfirmationRequest> DeleteQuestionAsync(IBpmnToolHost host, IReadOnlyList<string> ids, BpmnEditOrigin origin, CancellationToken cancellationToken)
    {
        var document = await host.GetDocumentAsync(cancellationToken).ConfigureAwait(true);

        // What will be asked about is what the user can recognise: the names the diagram gives the elements, shortened because the name is the diagram's to choose.
        var details = ids.Take(MaxDetails).Select(id => document?.FindFlowNode(id)?.Name is { Length: > 0 } name ? $"{Shorten(name)} ({id})" : id).ToList();
        if (ids.Count > MaxDetails) details.Add(_text("AgentConfirmation.Delete.More", [ids.Count - MaxDetails]));

        // And the count is of what will go, not of what was named: a Sub-Process takes its contents with it.
        var items = document is null
            ? ids.Count
            : BpmnDocumentEditor.DeletionClosure(document, ids).Count(id => document.Find(id) is not (BpmnSequenceFlow or BpmnMessageFlow or BpmnAssociation or BpmnDataAssociation));

        return new BpmnConfirmationRequest(
            _text("AgentConfirmation.Delete.Title", []),
            _text("AgentConfirmation.Delete.Summary", [Math.Max(items, ids.Count)]),
            origin,
            details,
            IsDestructive: true);
    }

    private static string Shorten(string name) => name.Length <= MaxNameLength ? name : name[..MaxNameLength] + "…";

    private static BpmnToolResult Refusal(IBpmnToolHost host, string code, string message) =>
        new() { Code = code, Message = message, Revision = host.Revision };
}
