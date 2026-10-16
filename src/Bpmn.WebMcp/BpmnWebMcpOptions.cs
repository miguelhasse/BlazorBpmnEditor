using Hasseware.AspNetCore.Components.Bpmn.Tools;

namespace Hasseware.AspNetCore.Components.Bpmn.WebMcp;

/// <summary>What the editor offers a browser agent, and how much of it.</summary>
/// <remarks>
/// Preview. Out of the box an agent can only look: editing is a decision the host makes with
/// <see cref="AllowMutations"/>, and even then deleting and saving ask the user first.
/// </remarks>
public sealed class BpmnWebMcpOptions
{
    /// <summary>
    /// Whether tools that change the diagram are offered at all. False by default: no editing tool is even
    /// registered with the browser, so an agent cannot call what it cannot see.
    /// </summary>
    public bool AllowMutations { get; set; }

    /// <summary>
    /// Whether the user is asked before the agent deletes, as the editor's confirmation prompt. True by default;
    /// a host may turn it off, because edits stay undoable.
    /// </summary>
    public bool RequireConfirmationForConsequential { get; set; } = true;

    /// <summary>
    /// Whether the user is asked before the agent saves to storage. True by default, and separate from
    /// <see cref="RequireConfirmationForConsequential"/> because saving leaves the browser session.
    /// </summary>
    public bool RequireConfirmationForPersistence { get; set; } = true;

    /// <summary>The prefix of every tool name. The default, <c>bpmn_</c>, keeps the names the catalog documents.</summary>
    public string ToolPrefix { get; set; } = "bpmn_";

    /// <summary>
    /// Tools to leave out, by their catalog name (<c>bpmn_export</c>), whatever the prefix. An editor that must
    /// not hand its whole document to an agent disables <c>bpmn_export</c>.
    /// </summary>
    public ISet<string> DisabledTools { get; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>The most elements, connectors or findings one call lists.</summary>
    public int MaxElementsPerCall
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 200;

    /// <summary>The largest export one call returns, in UTF-8 bytes.</summary>
    public int MaxXmlBytes
    {
        get;
        set
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            field = value;
        }
    } = 1_000_000;

    internal BpmnToolLimits ToLimits() => BpmnToolLimits.Default with { MaxElements = MaxElementsPerCall, MaxExportBytes = MaxXmlBytes };
}
