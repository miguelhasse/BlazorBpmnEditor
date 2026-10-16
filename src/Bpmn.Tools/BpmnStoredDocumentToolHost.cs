using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>A stored document, loaded for one call, as a document the read tools can describe.</summary>
/// <remarks>
/// For an agent server: the document came out of a store and is not edited here, so the host is read-only and
/// its <see cref="Revision"/> is the stored revision, which agents compare for equality like any other.
/// </remarks>
public sealed class BpmnStoredDocumentToolHost : IBpmnToolHost
{
    private readonly BpmnDocument _document;

    /// <summary>Creates a host.</summary>
    /// <param name="document">The loaded document.</param>
    /// <param name="storedRevision">The revision the store gave it.</param>
    /// <param name="origin">The agent the call is attributed to.</param>
    /// <param name="profiles">Profile validators; none when null.</param>
    /// <param name="limits">Per-call limits; the defaults when null.</param>
    /// <exception cref="ArgumentException"><paramref name="origin"/> is not an agent.</exception>
    public BpmnStoredDocumentToolHost(
        BpmnDocument document,
        string? storedRevision,
        BpmnEditOrigin origin,
        IReadOnlyList<IBpmnProfileValidator>? profiles = null,
        BpmnToolLimits? limits = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!origin.IsAgent) throw new ArgumentException("Tools act for an agent, never as the user.", nameof(origin));

        _document = document;
        Revision = storedRevision;
        Origin = origin;
        Profiles = profiles ?? [];
        Limits = limits ?? BpmnToolLimits.Default;
    }

    /// <inheritdoc />
    public BpmnEditOrigin Origin { get; }

    /// <inheritdoc />
    public bool IsReadOnly => true;

    /// <inheritdoc />
    public string? ActiveDiagramId => null;

    /// <inheritdoc />
    public EditingOptions Editing => EditingOptions.Default;

    /// <inheritdoc />
    public IReadOnlyList<IBpmnProfileValidator> Profiles { get; }

    /// <inheritdoc />
    public BpmnToolLimits Limits { get; }

    /// <inheritdoc />
    public string? Revision { get; }

    /// <inheritdoc />
    public ValueTask<BpmnDocument?> GetDocumentAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<BpmnDocument?>(_document);

    /// <inheritdoc />
    /// <exception cref="NotSupportedException">Always: the document is read-only, and the tools check that first.</exception>
    public ValueTask<BpmnEditResult> ApplyAsync(BpmnEditCommand command, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("A stored document is read-only here.");
}
