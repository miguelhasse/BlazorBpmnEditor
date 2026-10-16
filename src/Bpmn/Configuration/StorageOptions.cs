namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>What an editor does when the document it has open is changed by someone else.</summary>
public enum BpmnExternalChangePolicy
{
    /// <summary>Offer Reload, Save as copy and Dismiss; change nothing until the user chooses.</summary>
    Notify,

    /// <summary>
    /// Reload silently when there are no unsaved edits and no label is being edited; otherwise behave as
    /// <see cref="Notify"/>. Undo history is cleared by the reload.
    /// </summary>
    AutoReloadWhenClean,

    /// <summary>Say nothing; the next save reports the conflict.</summary>
    Ignore,
}

/// <summary>Document storage behaviour applied to every <see cref="Storage.BpmnDocumentBinding"/>.</summary>
public sealed record StorageOptions
{
    /// <summary>The defaults: notify about external changes.</summary>
    public static StorageOptions Default { get; } = new();

    /// <summary>What an editor does when its open document is changed by someone else.</summary>
    /// <remarks>
    /// <see cref="BpmnExternalChangePolicy.Notify"/> by default, so no document is replaced without a
    /// click unless the host opts in.
    /// </remarks>
    public BpmnExternalChangePolicy ExternalChangePolicy { get; init; } = BpmnExternalChangePolicy.Notify;
}
