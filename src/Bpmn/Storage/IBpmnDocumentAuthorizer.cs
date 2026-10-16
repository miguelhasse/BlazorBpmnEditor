namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>What a caller wants to do with a document.</summary>
public enum BpmnDocumentAction
{
    /// <summary>Open, describe, validate, export or compare it.</summary>
    Read,

    /// <summary>Change a document that exists.</summary>
    Write,

    /// <summary>Make a document that does not exist yet.</summary>
    Create,
}

/// <summary>Decides, per document, what the current caller may do, for hosts whose documents have owners or ACLs.</summary>
/// <remarks>
/// A refusal is answered exactly as a missing document is, so a caller learns nothing about what exists. The
/// default allows everything inside the caller's tenant. Every surface that serves documents to a caller (the MCP
/// server, the live-update transports) asks it before touching the store.
/// </remarks>
public interface IBpmnDocumentAuthorizer
{
    /// <summary>Whether the current caller may do <paramref name="action"/> to the document.</summary>
    /// <param name="action">What they want to do.</param>
    /// <param name="key">The document, with the tenant the call runs in.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <returns>True when allowed.</returns>
    public ValueTask<bool> IsAllowedAsync(BpmnDocumentAction action, BpmnDocumentKey key, CancellationToken cancellationToken);
}

/// <summary>Allows everything; documents are protected by the tenant alone.</summary>
public sealed class AllowAllBpmnDocumentAuthorizer : IBpmnDocumentAuthorizer
{
    /// <inheritdoc />
    public ValueTask<bool> IsAllowedAsync(BpmnDocumentAction action, BpmnDocumentKey key, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);
}
