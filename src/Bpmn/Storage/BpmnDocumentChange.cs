namespace Hasseware.AspNetCore.Components.Bpmn.Storage;

/// <summary>What happened to a stored document.</summary>
public enum BpmnDocumentChangeKind
{
    /// <summary>The document was created or its content replaced.</summary>
    Updated,

    /// <summary>The document was deleted.</summary>
    Deleted,
}

/// <summary>A notification that a stored document changed.</summary>
/// <remarks>
/// <para>
/// A notification carries no content: a recipient that wants the new document reads it through the
/// store, under the store's normal authorization, so a notification can never deliver anything its
/// recipient could not open.
/// </para>
/// <para>
/// Delivery is best effort, at most once and unordered. Revisions are opaque, so a recipient compares
/// <see cref="Revision"/> only for equality with the revision it holds; a missed notification is caught
/// by the revision check every save makes.
/// </para>
/// </remarks>
/// <param name="Key">The document.</param>
/// <param name="Kind">What happened.</param>
/// <param name="Revision">The new revision; null when the document was deleted.</param>
/// <param name="Origin">Who wrote it, when the writer identifies itself; a hint only, never the rule.</param>
/// <param name="OccurredAt">When the change was published.</param>
public sealed record BpmnDocumentChange(
    BpmnDocumentKey Key,
    BpmnDocumentChangeKind Kind,
    string? Revision,
    string? Origin,
    DateTimeOffset OccurredAt);

/// <summary>Whether change notifications are currently arriving.</summary>
public enum BpmnChangeFeedState
{
    /// <summary>Notifications are being delivered.</summary>
    Live,

    /// <summary>The connection was lost and is being restored; changes may be reported late.</summary>
    Reconnecting,

    /// <summary>The connection was refused for lack of authorization; the host must re-authenticate.</summary>
    Unauthorized,

    /// <summary>The feed has been closed and will not reconnect.</summary>
    Closed,
}
