using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.AspNetCore.Components;

namespace Bpmn.Demo.Client.Pages;

/// <summary>
/// Runs a real import → export → import cycle in the browser and shows what survived.
/// </summary>
/// <remarks>
/// The round-trip suite asserts the same properties, but a green test is a claim and this is the
/// thing itself: the two modes side by side over a real vendor file, with the export diagnostics
/// manifest section 10.6 requires and the XML each mode actually produced.
/// </remarks>
public sealed partial class Interchange : ComponentBase
{
    private string _selectedSample = "zeebe-extensions.bpmn";
    private string _status = string.Empty;
    private bool _allowLossyCanonicalExport;
    private Report? _report;

    [Inject]
    private HttpClient Http { get; init; } = default!;

    /// <summary>The fixtures staged in the host application's wwwroot/samples folder.</summary>
    private static string[] SampleFiles { get; } =
    [
        "simple-process.bpmn",
        "order-process.bpmn",
        "collaboration.bpmn",
        "linked-processes.bpmn",
        "nested-subprocesses.bpmn",
        "zeebe-process.bpmn",
        "zeebe-extensions.bpmn",
        "bug-triage.bpmn",
        "bug-triage.bpmn",
        "gdpr-data-request.bpmn",
        "global-catalog.bpmn",
        "it-asset-request.bpmn",
        "leave-request.bpmn",
        "loan-approval.bpmn",
        "order-fulfilment.bpmn",
        "pizza-store.bpmn",
        "product-return.bpmn",
        "recruitment-and-selection.bpmn",
        "recruitment-process.bpmn",
        "self-serve-restaurant.bpmn",
        "smart-parking.bpmn",
        "support-ticket-escalation.bpmn",
        "tender-process.bpmn",
        "training-request.bpmn",
        "travel-request.bpmn",
        "vendor-extensions.bpmn",
        "vendor-onboarding.bpmn",
        "large-virtualization-stress.bpmn"
    ];

    protected override Task OnInitializedAsync() => RunAsync(_selectedSample);

    private Task RunSelectedAsync() => RunAsync(_selectedSample);

    private async Task RunAsync(string sample)
    {
        _selectedSample = sample;
        _report = null;
        _status = Localizer["Interchange.RoundTripping", sample];

        try
        {
            var xml = await Http.GetStringAsync($"samples/{sample}").ConfigureAwait(true);
            var imported = BpmnXmlReader.Read(xml);

            var canonicalOptions = BpmnWriteOptions.Canonical with
            {
                AllowLossyCanonicalExport = _allowLossyCanonicalExport,
            };
            var canonical = Run(imported.Document, canonicalOptions, Localizer["Interchange.Canonical"], xml);
            var fidelity = Run(imported.Document, BpmnWriteOptions.Fidelity, Localizer["Interchange.Fidelity"], xml);

            _report = new Report([canonical, fidelity], Compare(imported.Document, canonical, fidelity));
            _status = Localizer["Interchange.ImportSummary", Describe(imported.Document), imported.Diagnostics.Count];
        }
        catch (BpmnXmlException exception)
        {
            _status = Localizer["CouldNotRead", sample, exception.Message];
        }
        catch (BpmnLossyExportException exception)
        {
            var firstLoss = exception.Diagnostics.Count > 0
                ? exception.Diagnostics[0].Message
                : Localizer["Interchange.NoLossDetails"].Value;
            _status = Localizer["Interchange.ExportRefused", exception.Diagnostics.Count, firstLoss];
        }
        catch (HttpRequestException exception)
        {
            _status = Localizer["CouldNotFetch", sample, exception.Message];
        }
    }

    private ModeReport Run(BpmnDocument document, BpmnWriteOptions options, string title, string original)
    {
        var export = BpmnXmlWriter.Write(document, options);
        var reread = BpmnXmlReader.Read(export.Xml).Document;

        // Written twice on purpose: an export that is not stable would keep producing a new diff
        // every time the user saves a file they never edited.
        var stable = BpmnXmlWriter.Write(reread, options).Xml == export.Xml;
        var changedLines = CountChangedLines(original, export.Xml);

        var summary = Localizer["Interchange.ModeSummary", export.Xml.Length, changedLines,
            Localizer[stable ? "Yes" : "No"].Value,
            Localizer[export.Mode == BpmnWriteMode.Canonical ? "Interchange.Canonical" : "Interchange.Fidelity"].Value];

        return new ModeReport(title, summary, export.Diagnostics, export.Xml, reread, stable);
    }

    /// <summary>A crude line-level difference count, enough to see which mode rewrote the file.</summary>
    private static int CountChangedLines(string original, string written)
    {
        var left = original.ReplaceLineEndings("\n").Split('\n');
        var right = written.ReplaceLineEndings("\n").Split('\n');

        var changed = Math.Abs(left.Length - right.Length);
        for (var index = 0; index < Math.Min(left.Length, right.Length); index++)
        {
            if (!string.Equals(left[index], right[index], StringComparison.Ordinal)) changed++;
        }

        return changed;
    }

    private List<Row> Compare(BpmnDocument before, ModeReport canonical, ModeReport fidelity)
    {
        List<(string Name, Func<BpmnDocument, string> Read)> measures =
        [
            ("Interchange.FlowNodes", d => Count(d.FlowNodes.Count)),
            ("Interchange.SequenceFlows", d => Count(d.SequenceFlows.Count)),
            ("Interchange.MessageFlows", d => Count(d.MessageFlows.Count)),
            ("Interchange.Lanes", d => Count(d.Lanes.Count)),
            ("Interchange.Artifacts", d => Count(d.TextAnnotations.Count + d.Groups.Count + d.Associations.Count)),
            ("Interchange.DataElements", d => Count(d.DataObjects.Count + d.DataObjectReferences.Count + d.DataStoreReferences.Count)),
            ("Interchange.GlobalDefinitions", d => Count(GlobalCount(d))),
            ("Interchange.PreservedElements", d => Count(d.Catalog.Preserved.Count)),
            ("Interchange.Shapes", d => Count(d.Diagrams.Sum(x => x.Plane.Shapes.Count))),
            ("Interchange.Edges", d => Count(d.Diagrams.Sum(x => x.Plane.Edges.Count))),
            ("Interchange.Waypoints", d => Count(d.Diagrams.SelectMany(x => x.Plane.Edges).Sum(e => e.Waypoints.Count))),
            ("Interchange.VendorExtensions", d => Count(d.FlowNodes.Sum(n => n.Extras.Extensions.Count))),
            ("Interchange.ForeignAttributes", d => Count(d.FlowNodes.Sum(n => n.Extras.ForeignAttributes.Count))),
        ];

        var rows = measures
            .Select(measure =>
            {
                var expected = measure.Read(before);
                var left = measure.Read(canonical.Document);
                var right = measure.Read(fidelity.Document);

                return new Row(
                    Localizer[measure.Name],
                    Cell(expected, left),
                    Cell(expected, right),
                    left == expected && right == expected);
            })
            .ToList();

        rows.Add(new Row(
            Localizer["Interchange.Stable"],
            Localizer[canonical.IsStable ? "Yes" : "No"],
            Localizer[fidelity.IsStable ? "Yes" : "No"],
            canonical.IsStable && fidelity.IsStable));

        return rows;
    }

    private static int GlobalCount(BpmnDocument document) =>
        document.Catalog.Messages.Count
        + document.Catalog.Signals.Count
        + document.Catalog.Errors.Count
        + document.Catalog.Escalations.Count
        + document.Catalog.ItemDefinitions.Count
        + document.Catalog.DataStores.Count
        + document.Catalog.Categories.Count
        + document.Catalog.Resources.Count
        + document.Catalog.Interfaces.Count
        + document.Catalog.GlobalTasks.Count;

    private static string Count(int value) => value.ToString(CultureInfo.CurrentCulture);

    private string Cell(string expected, string actual) =>
        expected == actual ? actual : Localizer["Interchange.Was", actual, expected];

    private string Describe(BpmnDocument document) =>
        Localizer["DocumentSummary", document.FlowNodes.Count, document.SequenceFlows.Count, document.Diagrams.Count];

    private sealed record Row(string Name, string Canonical, string Fidelity, bool IsOk);

    private sealed record ModeReport(
        string Title,
        string Summary,
        IReadOnlyList<BpmnExportDiagnostic> Diagnostics,
        string Xml,
        BpmnDocument Document,
        bool IsStable);

    private sealed record Report(IReadOnlyList<ModeReport> Modes, IReadOnlyList<Row> Rows);
}
