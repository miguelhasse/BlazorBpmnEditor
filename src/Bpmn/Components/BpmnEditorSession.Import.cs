using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;

using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// Importing a file: offered first, committed only if accepted (implementation plan M6).
/// </summary>
/// <remarks>
/// <para>
/// An import replaces the whole document, which makes it the one operation undo cannot help with —
/// the history belongs to the file being replaced. So it is offered rather than performed: the file
/// is parsed into a preview held to one side, and the editor's own document is not touched until
/// someone says yes.
/// </para>
/// <para>
/// Rejecting therefore cannot corrupt anything, and neither can a file that will not parse. That is
/// the milestone's exit criterion, and it holds structurally rather than by being careful about the
/// order of assignments.
/// </para>
/// </remarks>
public sealed partial class BpmnEditorSession
{
    private BpmnImportPreview? _import;

    /// <summary>The import waiting for an answer, if any.</summary>
    public BpmnImportPreview? PendingImport => _import;

    /// <summary>Whether an import is waiting for an answer.</summary>
    public bool IsImporting => _import is not null;

    /// <summary>
    /// Raised when an import is accepted, with the document that replaced the current one.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="OnDocumentChanged"/>, which reports edits. A host usually wants to
    /// know that the file on screen is a different file — to retitle a tab, to forget a path — and
    /// that is not the same event as a shape moving.
    /// </remarks>
    internal EventCallback<BpmnImportPreview> OnImported { get; set; }

    /// <summary>Offers a file the canvas was given, without changing anything.</summary>
    internal Task HandleFileDroppedAsync(FileDropped payload)
    {
        PreviewImport(payload.FileName, payload.Xml);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads a file and offers what it contains, without changing anything.
    /// </summary>
    /// <remarks>
    /// BPMN XML and BPMN JSON are both accepted; the format is detected from the content, then
    /// from the file extension (see <see cref="Serialization.BpmnFileFormats.Detect"/>).
    /// </remarks>
    /// <param name="fileName">The file's name, for the offer.</param>
    /// <param name="xml">Its contents, as BPMN XML or BPMN JSON.</param>
    /// <returns>The preview, so a caller can inspect it as well as see it.</returns>
    public BpmnImportPreview PreviewImport(string fileName, string xml)
    {
        _import = BpmnImportPreview.Of(fileName, xml);
        NotifyStateChanged();

        return _import;
    }

    /// <summary>
    /// Takes the offered import, replacing the document and everything that described the old one.
    /// </summary>
    /// <returns>A task that completes once the new document is on screen.</returns>
    public async Task AcceptImportAsync()
    {
        if (_import is not { Document: { } document } accepted) return;

        _import = null;

        /*
         * A new document in every sense: the history, the selection and the navigation path all
         * described a file that is no longer open. Keeping any of them would offer an undo into a
         * document the user cannot see.
         */
        _history = new BpmnEditHistory(document, HistoryOptions());
        _lastPublished = null;
        _selection = [];
        _selectionSnapshot = null;
        _selectionAnchor = null;
        _accessibleCurrent = null;
        _labelEdit = null;
        _pendingCall = null;
        _armedTool = null;
        _menu = null;
        _menuSnapshot = null;
        _activeDiagramId = null;
        _path.Clear();

        // Remembered expansion sizes describe a document that is no longer open.
        ForgetExpansions();

        await PushDocumentAsync(document).ConfigureAwait(true);
        await OnImported.InvokeAsync(accepted).ConfigureAwait(true);

        NotifyStateChanged();
    }

    /// <summary>
    /// Declines the offered import.
    /// </summary>
    /// <remarks>
    /// Nothing to undo, because nothing was done: the parsed document is simply dropped.
    /// </remarks>
    public void CancelImport()
    {
        if (_import is null) return;

        _import = null;
        NotifyStateChanged();
    }
}
