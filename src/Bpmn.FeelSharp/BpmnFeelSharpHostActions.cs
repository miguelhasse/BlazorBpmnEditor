namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Explicit host-owned actions. No network transport, model client, or storage is supplied.</summary>
public sealed class BpmnFeelSharpHostActions
{
    /// <summary>Requests a proposed edit. The workspace requires separate user acceptance before insertion.</summary>
    public Func<BpmnFeelSharpDraft, CancellationToken, Task<string>>? Suggest { get; init; }

    /// <summary>Shares a user-requested snapshot through the host's chosen transport and privacy policy.</summary>
    public Func<BpmnFeelSharpDraft, CancellationToken, Task>? Share { get; init; }

    /// <summary>Requests a collaboration revision; it is treated as a proposal, not an automatic write.</summary>
    public Func<BpmnFeelSharpDraft, CancellationToken, Task<string>>? Collaborate { get; init; }
}
