using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Shared draft and interaction logic for document Message catalog renderers.</summary>
public abstract class BpmnMessageCatalogBase : BpmnEditorComponentBase
{
    private BpmnMessageDraft? _draft;
    private string? _deleteId;
    private bool _focusPending;
    private bool _renderedOpen;
    private BpmnDocument? _inspectedDocument;
    private readonly Dictionary<string, (bool CanDelete, IReadOnlyList<BpmnReferenceSite> Usages)> _inspections = [];

    /// <summary>Catalog visibility and current action.</summary>
    protected BpmnMessageCatalogState Catalog => State.MessageCatalog;

    /// <summary>Definitions from the authoritative document, not an expanded diagram projection.</summary>
    protected IReadOnlyList<BpmnMessage> Messages => State.Document?.Catalog.Messages ?? [];

    /// <summary>Whether the form must prevent mutations.</summary>
    protected bool ReadOnly => State.IsReadOnly;

    /// <summary>Whether this renderer releases its modal synchronously with DOM removal.</summary>
    protected virtual bool RestoreFocusOnClose => true;

    /// <summary>Releases deferred focus after a native modal has closed.</summary>
    protected void CompleteClose() => Session.CompleteMessageCatalogClose();

    /// <summary>Restores focus after replacing the focused draft or confirmation content.</summary>
    protected virtual Task FocusDialogAsync() => Task.CompletedTask;

    /// <summary>The pending Message name; empty names are legal.</summary>
    protected string? DraftName { get; set; }

    /// <summary>Accessible heading identity.</summary>
    protected string TitleId { get; } = "bpmn-messages-" + Guid.NewGuid().ToString("N");

    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        previous.MessageCatalog != current.MessageCatalog || previous.Document != current.Document
        || previous.IsReadOnly != current.IsReadOnly;

    /// <inheritdoc />
    protected override bool ShouldRender() => (Catalog.IsOpen || _renderedOpen) && base.ShouldRender();

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        if (_draft != Catalog.Draft)
        {
            _draft = Catalog.Draft;
            DraftName = _draft?.Name;
            _focusPending = _renderedOpen && Catalog.IsOpen;
        }
        if (_deleteId != Catalog.DeleteId)
        {
            _deleteId = Catalog.DeleteId;
            _focusPending = _renderedOpen && Catalog.IsOpen;
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        _renderedOpen = Catalog.IsOpen;

        if (!Catalog.IsOpen && RestoreFocusOnClose)
            CompleteClose();

        if (_focusPending && Catalog.IsOpen)
        {
            _focusPending = false;
            await FocusDialogAsync();
        }
    }

    /// <summary>A display name which does not confuse unnamed definitions with IDs.</summary>
    protected string NameOf(BpmnMessage message) => string.IsNullOrEmpty(message.Name) ? Text("MessageCatalog.Unnamed") : message.Name;

    /// <summary>Whether deleting the current definition can be proved safe.</summary>
    protected bool CanDelete(string id) => Inspect(id).CanDelete;

    /// <summary>Cached deletion safety and reference sites for a definition.</summary>
    protected (bool CanDelete, IReadOnlyList<BpmnReferenceSite> Usages) Inspect(string id)
    {
        if (State.Document is not { } document)
            return (false, []);


        if (!ReferenceEquals(_inspectedDocument, document))
        {
            _inspectedDocument = document;
            _inspections.Clear();
        }

        if (_inspections.TryGetValue(id, out var cached))
            return cached;

        var inspection = BpmnMessageCatalog.Inspect(document, id);
        return _inspections[id] = inspection is null ? (false, []) : (inspection.CanDelete, inspection.Usages);
    }

    /// <summary>Opens an unassigned new definition.</summary>
    protected void NewMessage() => Session.NewMessage();

    /// <summary>Edits a shared definition.</summary>
    protected void EditMessage(string id) => Session.EditMessage(id);

    /// <summary>Stages deletion for confirmation.</summary>
    protected void ConfirmDelete(string id) => Session.ConfirmMessageDeletion(id);

    /// <summary>Cancels without writing.</summary>
    protected void CancelEdit() => Session.CancelMessageEdit();

    /// <summary>Saves one atomic history step.</summary>
    protected Task SaveAsync() => Session.SaveMessageAsync(DraftName);

    /// <summary>Rechecks references and deletes the confirmed definition.</summary>
    protected Task DeleteAsync() => Session.DeleteMessageAsync();

    /// <summary>Closes and restores focus to the invoking control.</summary>
    protected Task CloseAsync()
    {
        Session.CloseMessages();
        return Task.CompletedTask;
    }
}
