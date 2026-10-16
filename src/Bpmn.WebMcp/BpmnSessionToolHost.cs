using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.Tools;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

/// <summary>An open editor, as a document the shared agent tools can read and edit.</summary>
/// <remarks>
/// <para>
/// Edits go through the editor's own session, so they join the user's undo history, are drawn at once
/// and carry an agent origin. It also forwards saving to the editor's document binding, when it has one.
/// </para>
/// <para>
/// Members must be used on the renderer's synchronization context, like the session itself.
/// </para>
/// </remarks>
public sealed class BpmnSessionToolHost : IBpmnToolHost, IBpmnSelectionHost, IBpmnPersistenceHost, IBpmnInteractiveHost
{
    private readonly BpmnEditorSession _session;
    private BpmnDocument? _observed;
    private long _revision;

    /// <summary>Creates a host over an editor's session.</summary>
    /// <param name="session">The editor.</param>
    /// <param name="limits">Per-call limits; the defaults when null.</param>
    /// <param name="origin">The agent edits are attributed to; <c>webmcp</c> when null.</param>
    /// <exception cref="ArgumentException"><paramref name="origin"/> is not an agent.</exception>
    public BpmnSessionToolHost(BpmnEditorSession session, BpmnToolLimits? limits = null, BpmnEditOrigin? origin = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        Origin = origin ?? BpmnEditOrigin.FromAgent("webmcp");
        if (!Origin.IsAgent) throw new ArgumentException("Tools act for an agent, never as the user.", nameof(origin));

        _session = session;
        Limits = limits ?? BpmnToolLimits.Default;
        _observed = session.EditedDocument;
    }

    /// <inheritdoc />
    public BpmnEditOrigin Origin { get; }

    /// <inheritdoc />
    public bool IsReadOnly => _session.State.IsReadOnly;

    /// <inheritdoc />
    public string? ActiveDiagramId => _session.State.ActiveDiagramId;

    /// <inheritdoc />
    public EditingOptions Editing => _session.State.Configuration.Editing;

    /// <inheritdoc />
    public IReadOnlyList<IBpmnProfileValidator> Profiles => _session.Profiles;

    /// <inheritdoc />
    public BpmnToolLimits Limits { get; }

    /// <inheritdoc />
    /// <remarks>
    /// Moves when the document the editor holds is a different one, whoever changed it, so an agent can
    /// tell that the user edited in between. It describes the open session and is unrelated to any stored revision.
    /// </remarks>
    public string? Revision
    {
        get
        {
            var current = _session.EditedDocument;
            if (!ReferenceEquals(current, _observed))
            {
                _observed = current;
                _revision++;
            }

            return $"r{_revision.ToString(CultureInfo.InvariantCulture)}";
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> SelectedIds => _session.State.Selection;

    /// <inheritdoc />
    public ValueTask<BpmnDocument?> GetDocumentAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(_session.EditedDocument);

    /// <inheritdoc />
    public bool IsBound => _session.DocumentBinding?.Key is not null;

    /// <inheritdoc />
    public string? DocumentName => _session.DocumentBinding?.Key is { IsValid: true } key ? key.Path.Name : null;

    /// <inheritdoc />
    public string? StoredRevision => _session.DocumentBinding?.StoredRevision;

    /// <inheritdoc />
    public bool IsDirty => _session.DocumentBinding?.IsDirty ?? false;

    /// <inheritdoc />
    public BpmnExternalChange? ExternalChange => _session.DocumentBinding?.ExternalChange;

    /// <inheritdoc />
    public Task<BpmnStoreResult> SaveAsync(CancellationToken cancellationToken = default) =>
        _session.DocumentBinding is { } binding
            ? binding.SaveAsync(cancellationToken)
            : throw new InvalidOperationException("The editor is not bound to a stored document.");

    /// <inheritdoc />
    public BpmnEditOrigin? UndoOrigin => _session.UndoOrigin;

    /// <inheritdoc />
    public BpmnEditOrigin? RedoOrigin => _session.RedoOrigin;

    /// <inheritdoc />
    public Task UndoAsync() => _session.UndoAsync();

    /// <inheritdoc />
    public Task RedoAsync() => _session.RedoAsync();

    /// <inheritdoc />
    public Task SelectAsync(IReadOnlyList<string> elementIds) => _session.SetSelectionAsync(elementIds);

    /// <inheritdoc />
    /// <exception cref="InvalidOperationException">The editor is read-only or has no document; the tools check first.</exception>
    public async ValueTask<BpmnEditResult> ApplyAsync(BpmnEditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        return await _session.ApplyAsync(command).ConfigureAwait(true);
    }
}
