using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>A tool host over a document held in memory, with its own edit history.</summary>
/// <remarks>
/// For tests, and for an agent server that loads a document, lets tools edit it and then stores
/// <see cref="Document"/>. The revision is a counter that moves with every applied edit.
/// </remarks>
public sealed class BpmnDocumentToolHost : IBpmnToolHost
{
    private long _revision;

    /// <summary>Creates a host.</summary>
    /// <param name="document">The document to work on.</param>
    /// <param name="origin">The agent the edits are attributed to.</param>
    /// <param name="editing">Editing rules; the defaults when null.</param>
    /// <param name="profiles">Profile validators; none when null.</param>
    /// <param name="limits">Per-call limits; the defaults when null.</param>
    /// <exception cref="ArgumentException"><paramref name="origin"/> is not an agent.</exception>
    public BpmnDocumentToolHost(
        BpmnDocument document,
        BpmnEditOrigin origin,
        EditingOptions? editing = null,
        IReadOnlyList<IBpmnProfileValidator>? profiles = null,
        BpmnToolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!origin.IsAgent) throw new ArgumentException("Tools act for an agent, never as the user.", nameof(origin));

        Origin = origin;
        Editing = editing ?? EditingOptions.Default;
        Profiles = profiles ?? [];
        Limits = limits ?? BpmnToolLimits.Default;
        History = new BpmnEditHistory(document, Editing);
    }

    /// <inheritdoc />
    public BpmnEditOrigin Origin { get; }

    /// <inheritdoc />
    public bool IsReadOnly { get; init; }

    /// <inheritdoc />
    public string? ActiveDiagramId { get; init; }

    /// <inheritdoc />
    public EditingOptions Editing { get; }

    /// <inheritdoc />
    public IReadOnlyList<IBpmnProfileValidator> Profiles { get; }

    /// <inheritdoc />
    public BpmnToolLimits Limits { get; }

    /// <inheritdoc />
    public string Revision => $"r{_revision.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>The document's history, including the origin of every edit.</summary>
    public BpmnEditHistory History { get; }

    /// <summary>The document as it stands.</summary>
    public BpmnDocument Document => History.Document;

    /// <inheritdoc />
    public ValueTask<BpmnDocument?> GetDocumentAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<BpmnDocument?>(History.Document);

    /// <inheritdoc />
    public ValueTask<BpmnEditResult> ApplyAsync(BpmnEditCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        var result = History.Apply(command);
        if (result.IsApplied) _revision++;

        return ValueTask.FromResult(result);
    }
}
