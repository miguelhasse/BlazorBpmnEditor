using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Execution;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Hasseware.AspNetCore.Components.Bpmn.Storage;
using Hasseware.AspNetCore.Components.Bpmn.WebMcp;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Options;

namespace Bpmn.Demo.Client.Pages;

/// <summary>
/// Loads a sample BPMN file and renders it, exercising the whole path from XML through the
/// semantic model, across interop, onto the JointJS paper and back out as events.
/// </summary>
public sealed partial class EditorDemo : ComponentBase, IAsyncDisposable
{
    private FluentBpmnEditor? _fluentEditor;
    private BpmnEditor? _plainEditor;

    /// <summary>Renders the standard HTML kit (<c>BpmnEditor</c>) instead of the Fluent UI kit.</summary>
    [Parameter]
    public bool Plain { get; set; }

    private bool HasEditor => _fluentEditor is not null || _plainEditor is not null;

    [Inject]
    private IOptions<BpmnOptions> BpmnOptions { get; set; } = default!;
    private BpmnDocument? _document;
    private string? _exportedXml;
    private string _exportedFormat = "XML";

    private void ExportXml()
    {
        if (_document is not null)
        {
            _exportedXml = BpmnXmlWriter.Write(_document, BpmnWriteOptions.Fidelity).Xml;
            _exportedFormat = "XML";
        }
    }

    private void ExportJson()
    {
        if (_document is not null)
        {
            _exportedXml = BpmnJsonWriter.Write(_document);
            _exportedFormat = "JSON";
        }
    }

    /// <summary>The largest file the page will read: the JSON reader's own default limit.</summary>
    private const long MaxOpenBytes = BpmnJsonFormat.DefaultMaxInputBytes;

    /// <summary>Opens a BPMN XML or JSON file through the editor's import preview.</summary>
    private async Task OpenFileAsync(InputFileChangeEventArgs args)
    {
        if (!HasEditor) return;

        try
        {
            using var reader = new StreamReader(args.File.OpenReadStream(MaxOpenBytes));
            var content = await reader.ReadToEndAsync().ConfigureAwait(true);
            if (Plain) _plainEditor!.PreviewImport(args.File.Name, content);
            else _fluentEditor!.PreviewImport(args.File.Name, content);
        }
        catch (IOException failure)
        {
            _status = Localizer["CouldNotOpen", args.File.Name, failure.Message];
        }
    }
    private IReadOnlyList<BpmnImportDiagnostic> _diagnostics = [];
    private string _selectedSample = "simple-process.bpmn";
    private string _status = string.Empty;
    private string _editStatus = string.Empty;
    private bool _snapToGrid;

    /// <summary>
    /// The editor configuration, so the grid can be switched on from the page.
    /// </summary>
    /// <remarks>
    /// Section 4.6 makes every connector endpoint depend on this setting — the pointer is snapped
    /// to the grid before it is projected onto the outline — so without a way to turn it on, half
    /// of that behaviour has no surface to be tried on at all.
    /// </remarks>
    private EditorConfig _configuration = EditorConfig.CreateDefault();

    private void ToggleSnap(bool enabled)
    {
        _snapToGrid = enabled;

        _configuration = _configuration with
        {
            Grid = _configuration.Grid with { SnapEnabled = enabled },
        };
    }

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
        "pizza-store.bpmn",
        "recruitment-and-selection.bpmn",
        "self-serve-restaurant.bpmn",
        "smart-parking.bpmn",
        "zeebe-process.bpmn",
        "zeebe-extensions.bpmn",
        "large-virtualization-stress.bpmn",
        "vendor-extensions.bpmn",
        "global-catalog.bpmn",
    ];

    [Inject]
    private IBpmnDocumentStore Store { get; init; } = default!;

    [Inject]
    private IBpmnDocumentBindingFactory Bindings { get; init; } = default!;

    /// <summary>The stored document the editor shows; created once, loaded with each sample.</summary>
    private BpmnDocumentBinding _binding = default!;

    private string? _saveMessage;

    /// <summary>Where the binding stands: a save result, unsaved edits, or the stored revision.</summary>
    private string SaveStatus => _saveMessage
        ?? (_binding.IsDirty ? Localizer["Editor.Unsaved"].Value : _binding.StoredRevision is { } revision ? Localizer["Editor.Saved", revision].Value : string.Empty);

    protected override Task OnInitializedAsync()
    {
        _editStatus = Localizer["Editor.NothingSelected"];

        _binding = Bindings.Create();
        _binding.Changed += (_, _) =>
        {
            _saveMessage = null;
            _ = InvokeAsync(StateHasChanged);
        };

        return LoadSampleAsync(_selectedSample);
    }

    /// <summary>Releases the binding's change subscription when the page goes away.</summary>
    /// <returns>A task that completes once the binding is disposed.</returns>
    public ValueTask DisposeAsync() => _binding?.DisposeAsync() ?? ValueTask.CompletedTask;

    private async Task SaveAsync()
    {
        var result = await _binding.SaveAsync().ConfigureAwait(true);

        // A conflict also raises the editor's own notice; this line only says why nothing was saved.
        _saveMessage = result.Succeeded ? null : Localizer["Editor.SaveConflict"].Value;
    }

    /// <summary>
    /// Plays another user saving the same document: renames its first named element through the store,
    /// at the stored revision, exactly as a second editor or an agent would.
    /// </summary>
    private async Task SimulateExternalChangeAsync()
    {
        if (_binding.Key is not { } key || await Store.LoadDocumentAsync(key).ConfigureAwait(true) is not { } stored) return;

        var nodes = stored.Document.FlowNodes;
        var node = nodes.FirstOrDefault(candidate => !string.IsNullOrEmpty(candidate.Name)) ?? (nodes.Count > 0 ? nodes[0] : null);
        if (node is null) return;

        var renamed = BpmnDocumentEditor.Apply(
            stored.Document,
            new UpdatePropertiesCommand(node.Id, "name", Localizer["Editor.EditedElsewhere", node.Name ?? node.Id]));
        if (renamed.IsApplied) await Store.SaveDocumentAsync(key, renamed.Document, stored.Revision).ConfigureAwait(true);
    }

    private async Task LoadSampleAsync(string sample)
    {
        // Clearing the picker is not a request to load a file called nothing. It happens when an
        // imported file takes over, and fetching "samples/" for it answers 404.
        if (string.IsNullOrEmpty(sample))
        {
            _selectedSample = sample;
            return;
        }

        _exportedXml = null;
        _selectedSample = sample;
        _status = Localizer["Editor.Reading", sample];

        try
        {
            // The samples are copied into the store the first time they are chosen, so edits saved to
            // one survive switching to another sample and back.
            var key = new BpmnDocumentKey(null, BpmnDocumentPath.Parse($"samples/{sample}"));
            if (await Store.StatAsync(key).ConfigureAwait(true) is null)
            {
                var xml = await Http.GetByteArrayAsync($"samples/{sample}").ConfigureAwait(true);
                await Store.SaveAsync(key, new BpmnDocumentContent(xml, BpmnFileFormat.Xml), ifMatchRevision: null).ConfigureAwait(true);
            }

            await _binding.LoadAsync(key).ConfigureAwait(true);
            var document = _binding.Document!;

            _document = document;
            _executionStale = true;
            _diagnostics = _binding.ImportDiagnostics;
            _status = Localizer["DocumentSummary", document.FlowNodes.Count,
                document.SequenceFlows.Count, document.Diagrams.Count];
        }
        catch (BpmnXmlException exception)
        {
            _document = null;
            _diagnostics = [];
            _status = Localizer["CouldNotRead", sample, exception.Message];
        }
        catch (HttpRequestException exception)
        {
            _document = null;
            _diagnostics = [];
            _status = Localizer["CouldNotFetch", sample, exception.Message];
        }
    }

    private void OnCanvasInitialized(CanvasInitialized payload) =>
        _status = Localizer["Editor.CanvasReady", payload.InstanceId[..8], payload.ConfigRevision];

    private async Task OnDocumentLoadedAsync(DocumentLoaded payload)
    {
        _status = Localizer["Editor.Rendered", payload.ShapeCount, payload.EdgeCount, payload.DiagramId];

        if (payload.UnrenderedElementIds.Count > 0)
        {
            _status += Localizer["Editor.Unrendered", payload.UnrenderedElementIds.Count];
        }

        // The invented state names the previous document's elements, so a new one needs its own.
        if (_executionStale && _showExecution && HasEditor)
        {
            _executionStale = false;
            await (Plain ? _plainEditor!.SetExecutionStateAsync(InventedState()) : _fluentEditor!.SetExecutionStateAsync(InventedState()));
        }
    }

    private void OnConfigRejected(ConfigRejected payload) =>
        _status = Localizer["Editor.ConfigurationRejected", payload.Code, payload.Path, payload.Message];

    /// <summary>
    /// Follows the edited document, so the page's own reference stays current.
    /// </summary>
    /// <remarks>
    /// The editor does not write back to the Document parameter — that would mean a component
    /// silently replacing a value its host passed in. Following the event is the host's side of
    /// that contract, and it is what makes a later reload of the same sample show the edits.
    /// </remarks>
    private void OnDocumentChanged(BpmnDocument document)
    {
        _exportedXml = null;
        _document = document;
        _editStatus = Localizer["Editor.Edited", document.FlowNodes.Count, document.SequenceFlows.Count];
    }

    /// <summary>The path the editor reported, which this page renders as a breadcrumb.</summary>
    private IReadOnlyList<BpmnDiagramStep> _path = [];

    /// <summary>Where the selected element leads, refreshed when the selection changes.</summary>
    private BpmnDrillTarget? _drill;

    private string? _drillElementId;

    private void OnImported(BpmnImportPreview preview)
    {
        // The picker no longer describes what is on screen, and it does not list this file.
        _selectedSample = string.Empty;
        _executionStale = true;
        _status = Localizer["Editor.Opened", preview.FileName];
        StateHasChanged();
    }

    private bool _highContrast;

    private bool _readOnly;

    private ThemeOptions _theme = ThemeOptions.Default;

    private void OnContrastChanged(ChangeEventArgs args)
    {
        _highContrast = args.Value is true;
        _theme = _theme with { Contrast = _highContrast ? BpmnContrast.High : BpmnContrast.Normal };
    }

    private void OnReadOnlyChanged(ChangeEventArgs args) => _readOnly = args.Value is true;

    private bool _webMcp;

    private bool _webMcpEdit;

    private string _webMcpStatus = string.Empty;

    private BpmnWebMcp? _webMcpPart;

    private void OnWebMcpChanged(ChangeEventArgs args)
    {
        _webMcp = args.Value is true;
        _webMcpStatus = string.Empty;
        if (!_webMcp) _webMcpPart = null;
    }

    // The component reads the option once, at registration, so changing it makes a new component.
    private void OnWebMcpEditChanged(ChangeEventArgs args)
    {
        _webMcpEdit = args.Value is true;
        _webMcpStatus = string.Empty;
    }

    private void OnWebMcpSettled() => _webMcpStatus = _webMcpPart?.Status switch
    {
        BpmnWebMcpState.Registered => Localizer[_webMcpEdit ? "Editor.WebMcpRegisteredEditing" : "Editor.WebMcpRegistered", _webMcpPart.RegisteredTools.Count],
        BpmnWebMcpState.Unsupported => Localizer["Editor.WebMcpUnsupported"],
        BpmnWebMcpState.Failed => Localizer["Editor.WebMcpFailed"],
        _ => string.Empty,
    };

    private bool _showExecution;

    /// <summary>Whether the pushed execution state was invented for a document no longer shown.</summary>
    private bool _executionStale;

    private bool _animateTokens;

    /// <summary>
    /// Pushes or withdraws a made-up execution state.
    /// </summary>
    /// <remarks>
    /// The demo is standing in for a host watching an engine. There is no engine here, so the
    /// state is invented from whatever sample is loaded — the point being demonstrated is that the
    /// editor draws what it is given, not that it can work out what a process is doing. It cannot,
    /// and the specification says so.
    /// </remarks>
    private async Task OnExecutionChangedAsync(ChangeEventArgs args)
    {
        _showExecution = args.Value is true;

        if (!HasEditor) return;

        if (_showExecution)
        {
            _executionStale = false;
            await (Plain ? _plainEditor!.SetExecutionStateAsync(InventedState()) : _fluentEditor!.SetExecutionStateAsync(InventedState()));
        }
        else
        {
            await (Plain ? _plainEditor!.ClearExecutionStateAsync() : _fluentEditor!.ClearExecutionStateAsync());
        }
    }

    private void OnAnimationChanged(ChangeEventArgs args) => _animateTokens = args.Value is true;

    /// <summary>A plausible run over the loaded sample.</summary>
    /// <remarks>
    /// Walks the flow nodes in document order and hands out one of each status, so every branch of
    /// the overlay is on screen at once: something finished, something running with tokens on it,
    /// something waiting, and something that failed with an incident to report.
    /// </remarks>
    private BpmnExecutionState InventedState()
    {
        var document = (Plain ? _plainEditor?.EditedDocument : _fluentEditor?.EditedDocument) ?? _document;
        if (document is null) return new BpmnExecutionState("demo-instance");

        var statuses = new[]
        {
            (BpmnActivityStatus.Completed, 0),
            (BpmnActivityStatus.Active, 2),
            (BpmnActivityStatus.Waiting, 1),
            (BpmnActivityStatus.Failed, 0),
            (BpmnActivityStatus.Canceled, 0),
        };

        var elements = document.FlowNodes
            .Take(statuses.Length)
            .Select((node, index) =>
            {
                var (status, tokens) = statuses[index];
                var incidents = status == BpmnActivityStatus.Failed
                    ? new[] { new BpmnIncident("demo-1", Localizer["Editor.ServiceTimeout"]) }
                    : [];

                return new BpmnElementExecution(node.Id, status, tokens, incidents);
            })
            .ToList();

        // Only the flows into whatever has already been reached, so the highlighted path stops
        // where the run did rather than lighting up the whole diagram.
        var reached = elements
            .Where(element => element.Status is BpmnActivityStatus.Completed or BpmnActivityStatus.Active)
            .Select(element => element.ElementId)
            .ToHashSet(StringComparer.Ordinal);

        var flows = document.SequenceFlows
            .Where(flow => reached.Contains(flow.SourceRef))
            .Select(flow => new BpmnFlowExecution(flow.Id, Traversed: true, TraversalCount: 1))
            .ToList();

        return new BpmnExecutionState("demo-instance", elements, flows);
    }

    private void OnDiagramNavigated(BpmnDiagramNavigated payload)
    {
        _path = payload.Path;
        _status = Localizer["Editor.Navigated", payload.Origin, string.Join(" / ", payload.Path.Select(step => step.Label))];
        StateHasChanged();
    }

    private async Task AscendAsync(string diagramId)
    {
        if (!HasEditor) return;

        await (Plain ? _plainEditor!.AscendToAsync(diagramId) : _fluentEditor!.AscendToAsync(diagramId));
    }

    private void OnSelectionChanged(SelectionChanged payload)
    {
        _editStatus = payload.ElementIds.Count switch
        {
            0 => Localizer["Editor.NothingSelected"],
            1 => Localizer["Editor.SelectedElement", payload.ElementIds[0]],
            var count => Localizer["Editor.SelectedElements", count],
        };

        // What the Open button does next, asked once per selection rather than on every render.
        _drillElementId = payload.ElementIds.Count == 1 ? payload.ElementIds[0] : null;
        _drill = _drillElementId is null ? null : (Plain ? _plainEditor?.DrillTargetFor(_drillElementId) : _fluentEditor?.DrillTargetFor(_drillElementId));
    }

    private bool _showProperties;

    private void OnShowPropertiesChanged(ChangeEventArgs args) =>
        _showProperties = args.Value is true;

    private void OnEditRejected(EditRejected payload) =>
        _editStatus = Localizer["Editor.EditRejected", payload.Code, payload.Message];
}
