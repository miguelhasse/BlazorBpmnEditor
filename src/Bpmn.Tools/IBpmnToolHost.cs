using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>
/// One document that agent tools read and edit, wherever it lives: an open editor, or a stored document
/// an agent server loaded.
/// </summary>
/// <remarks>
/// <para>
/// One host per document. A browser module wraps the editor session, whose edits then join the user's
/// undo history; a server wraps a document loaded from a store. <see cref="BpmnTools"/> only ever talks to
/// this interface, so both surfaces run the same handlers.
/// </para>
/// <para>
/// Confirmation is not the host's concern and not the handlers': an adapter asks the user before calling a
/// consequential handler, because only the adapter knows whether a user is there to ask.
/// </para>
/// </remarks>
public interface IBpmnToolHost
{
    /// <summary>The agent the edits are attributed to; never <see cref="BpmnEditOrigin.User"/>.</summary>
    public BpmnEditOrigin Origin { get; }

    /// <summary>Whether the document may not be changed; mutating tools then refuse.</summary>
    public bool IsReadOnly { get; }

    /// <summary>The diagram tools place on and describe by default; the first one when null.</summary>
    public string? ActiveDiagramId { get; }

    /// <summary>Collision clearance and other editing rules, as the editor applies them.</summary>
    public EditingOptions Editing { get; }

    /// <summary>The profile validators in force, such as element templates and script checks.</summary>
    public IReadOnlyList<IBpmnProfileValidator> Profiles { get; }

    /// <summary>How much a single call may read or write.</summary>
    public BpmnToolLimits Limits { get; }

    /// <summary>An opaque token that changes whenever the document does, reported with every result.</summary>
    public string? Revision { get; }

    /// <summary>Reads the current document.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The document, or null when none is open.</returns>
    public ValueTask<BpmnDocument?> GetDocumentAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies one edit, exactly as the editor would, including its history.</summary>
    /// <param name="command">The edit; its origin is the host's agent.</param>
    /// <param name="cancellationToken">Cancels the edit.</param>
    /// <returns>The editor's result; a refusal leaves the document unchanged.</returns>
    public ValueTask<BpmnEditResult> ApplyAsync(BpmnEditCommand command, CancellationToken cancellationToken = default);
}
