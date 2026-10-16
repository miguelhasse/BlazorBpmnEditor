using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

public static partial class BpmnTools
{
    /// <summary>Compares the host's document with another one, element by element.</summary>
    /// <remarks>
    /// The baseline comes from the adapter: a stored revision, another stored document, or an earlier
    /// snapshot. That is why this tool is not in <see cref="BpmnToolCatalog"/>: its arguments name things
    /// only the adapter can resolve.
    /// </remarks>
    /// <param name="host">The later document.</param>
    /// <param name="baseline">The earlier document.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>What was added, removed and changed since the baseline.</returns>
    public static async ValueTask<BpmnToolResult<BpmnDocumentDiffResult>> DiffAsync(
        IBpmnToolHost host,
        BpmnDocument baseline,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(baseline);
        if (await host.GetDocumentAsync(cancellationToken).ConfigureAwait(false) is not { } document) return NoDocument<BpmnDocumentDiffResult>(host);

        return Succeeded(host, BpmnDocumentDiff.Compare(baseline, document, host.Limits));
    }
}
