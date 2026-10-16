using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>A tool host whose document is bound to a stored one, so edits can be saved.</summary>
/// <remarks>
/// Optional: a host with no store has only an in-memory document, and the persistence tools are then not
/// offered. The host adds no storage logic of its own; it forwards to the document binding.
/// </remarks>
public interface IBpmnPersistenceHost
{
    /// <summary>Whether the open document is bound to a stored one; the other members mean nothing when it is not.</summary>
    public bool IsBound { get; }

    /// <summary>The stored document's name, or null.</summary>
    public string? DocumentName { get; }

    /// <summary>The revision the stored document had when it was last loaded or saved.</summary>
    public string? StoredRevision { get; }

    /// <summary>Whether the open document has edits that are not saved.</summary>
    public bool IsDirty { get; }

    /// <summary>What happened to the stored document behind the editor's back, or null when nothing did.</summary>
    public BpmnExternalChange? ExternalChange { get; }

    /// <summary>Writes the open document back to where it was loaded from.</summary>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>How the store answered; nothing is overwritten on a conflict.</returns>
    public Task<BpmnStoreResult> SaveAsync(CancellationToken cancellationToken = default);
}
