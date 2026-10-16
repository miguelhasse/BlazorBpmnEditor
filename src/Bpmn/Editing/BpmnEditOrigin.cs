namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Who asked for an edit: the user, or a named agent acting for them.</summary>
/// <remarks>
/// <para>
/// History keeps the origin of every entry, so tooling can tell an agent's edits from the user's
/// without reading labels, which are display text and localized. An agent's undo tool uses it to
/// undo only what that agent did: it refuses when the entry it would undo is the user's.
/// </para>
/// <para>
/// <see langword="default"/> is <see cref="User"/>, so every existing command is the user's unless it
/// says otherwise.
/// </para>
/// </remarks>
public readonly record struct BpmnEditOrigin
{
    private BpmnEditOrigin(string agent) => Agent = agent;

    /// <summary>The user, directly.</summary>
    public static BpmnEditOrigin User => default;

    /// <summary>The agent that asked for the edit, such as <c>webmcp</c>; null for the user.</summary>
    public string? Agent { get; }

    /// <summary>Whether an agent, rather than the user, asked for the edit.</summary>
    public bool IsAgent => Agent is not null;

    /// <summary>An edit asked for by an agent.</summary>
    /// <param name="agent">A short, stable name for the agent or module, such as <c>webmcp</c>.</param>
    /// <returns>The origin.</returns>
    public static BpmnEditOrigin FromAgent(string agent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agent);
        return new BpmnEditOrigin(agent);
    }

    /// <summary>The origin as text, for logs: <c>user</c> or <c>agent:name</c>.</summary>
    /// <returns>A diagnostic representation.</returns>
    public override string ToString() => Agent is null ? "user" : $"agent:{Agent}";
}
