using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>A pending edit of a document-level Message definition.</summary>
public sealed record BpmnMessageDraft(string Id, string? Name, bool IsNew, string? AssignTo);

/// <summary>The document Message catalog and its pending user action.</summary>
public sealed record BpmnMessageCatalogState
{
    /// <summary>Whether the catalog is visible.</summary>
    public bool IsOpen { get; init; }
    /// <summary>The form being edited, if any.</summary>
    public BpmnMessageDraft? Draft { get; init; }
    /// <summary>The unused definition awaiting deletion confirmation.</summary>
    public string? DeleteId { get; init; }
    /// <summary>The last refused operation's explanation.</summary>
    public string? Error { get; init; }
    /// <summary>Whether saving should return to the invoking property.</summary>
    public bool CloseAfterSave { get; init; }
    /// <summary>Whether a save or delete is awaiting publication.</summary>
    public bool IsBusy { get; init; }
}

public sealed partial class BpmnEditorSession
{
    private BpmnMessageCatalogState _messageCatalog = new();
    private string _messageCatalogOrigin = "toolbar";
    private string? _messageFocusReturn;
    private string? _messageFocusPending;

    /// <summary>Whether component configuration provides a Message catalog renderer.</summary>
    public bool HasMessageCatalog => _options.Value.Components.Get(BpmnEditorPart.MessageCatalog, KitDefaults) is not null;

    /// <summary>Opens the document catalog without changing the canvas selection.</summary>
    public void ShowMessages(string origin = "toolbar")
    {
        if (EditedDocument is null) return;
        _messageCatalogOrigin = origin;
        _messageCatalog = new() { IsOpen = true };
        NotifyStateChanged();
    }

    /// <summary>Starts a Message form, optionally assigning its new definition to a task or Message Flow.</summary>
    public void NewMessage(string? assignTo = null)
    {
        if (IsReadOnly || EditedDocument is not { } document) return;
        var inline = !_messageCatalog.IsOpen;
        if (inline) _messageCatalogOrigin = assignTo ?? "toolbar";
        string id;
        do { id = "Message_" + Guid.NewGuid().ToString("N"); } while (document.Find(id) is not null);
        _messageCatalog = new()
        {
            IsOpen = true,
            CloseAfterSave = inline,
            Draft = new(id, null, true, assignTo),
        };
        NotifyStateChanged();
    }

    /// <summary>Opens the referenced definition's shared name for editing.</summary>
    public void EditMessage(string id, string? origin = null)
    {
        if (EditedDocument?.Find(id) is not BpmnMessage message) return;
        var inline = !_messageCatalog.IsOpen;
        if (inline) _messageCatalogOrigin = origin ?? "toolbar";
        _messageCatalog = new()
        {
            IsOpen = true,
            CloseAfterSave = inline,
            Draft = new(id, message.Name, false, null),
        };
        NotifyStateChanged();
    }

    /// <summary>Cancels the draft or confirmation without modifying the document.</summary>
    public void CancelMessageEdit()
    {
        if (_messageCatalog.CloseAfterSave) CloseMessages();
        else
        {
            _messageCatalog = _messageCatalog with { Draft = null, DeleteId = null, Error = null, IsBusy = false };
            NotifyStateChanged();
        }
    }

    /// <summary>Closes the catalog, discarding unsubmitted values.</summary>
    public void CloseMessages()
    {
        _messageFocusPending = _messageCatalogOrigin;
        _messageCatalog = new();
        NotifyStateChanged();
    }

    /// <summary>Restores invocation focus after the renderer has released its modal surface.</summary>
    public void CompleteMessageCatalogClose()
    {
        if (_messageCatalog.IsOpen || _messageFocusPending is null) return;
        _messageFocusReturn = _messageFocusPending;
        _messageFocusPending = null;
        NotifyStateChanged();
    }

    /// <summary>Consumes a focus-restoration request for the invoking toolbar or property control.</summary>
    public bool TakeMessageFocus(string origin)
    {
        if (_messageFocusReturn != origin) return false;
        _messageFocusReturn = null;
        return true;
    }

    /// <summary>Requests explicit confirmation before attempting to delete a definition.</summary>
    public void ConfirmMessageDeletion(string id)
    {
        if (IsReadOnly) return;
        _messageCatalog = _messageCatalog with { Draft = null, DeleteId = id, Error = null };
        NotifyStateChanged();
    }

    /// <summary>Saves against the current document; creation and assignment share one undo step.</summary>
    public async Task SaveMessageAsync(string? name)
    {
        if (_messageCatalog.Draft is not { } draft) return;
        BpmnEditCommand command = draft.IsNew
            ? new CreateMessageCommand(draft.Id, name)
            : new UpdateMessageNameCommand(draft.Id, name);
        if (draft.IsNew && draft.AssignTo is { } owner)
        {
            command = new CompoundCommand(
                [command, new UpdatePropertiesCommand(owner, [new("messageRef", draft.Id)])],
                "Create and assign Message");
        }
        await ApplyMessageCommandAsync(command).ConfigureAwait(true);
    }

    /// <summary>Deletes only after the command rechecks current references.</summary>
    public Task DeleteMessageAsync() => _messageCatalog.DeleteId is { } id
        ? ApplyMessageCommandAsync(new DeleteMessageCommand(id)) : Task.CompletedTask;

    private async Task ApplyMessageCommandAsync(BpmnEditCommand command)
    {
        if (_messageCatalog.IsBusy) return;
        var pending = _messageCatalog with { IsBusy = true, Error = null };
        var completed = pending with { IsBusy = false };
        _messageCatalog = pending;
        NotifyStateChanged();
        BpmnEditResult? result;
        try
        {
            result = await ApplyPropertyCommandAsync(command).ConfigureAwait(true);
        }
        finally
        {
            if (ReferenceEquals(_messageCatalog, pending))
            {
                _messageCatalog = completed;
                NotifyStateChanged();
            }
        }
        if (!ReferenceEquals(_messageCatalog, completed)) return;
        if (result is null)
        {
            _messageCatalog = _messageCatalog with { Error = DisplayText.BuiltIn("MessageCatalog.Unavailable", "This document cannot currently be edited.") };
        }
        else if (!result.IsApplied)
        {
            _messageCatalog = _messageCatalog with { Error = string.Join(" ", result.Rejections.Select(item => item.Message)) };
        }
        else if (_messageCatalog.CloseAfterSave)
        {
            CloseMessages();
            return;
        }
        else _messageCatalog = _messageCatalog with { Draft = null, DeleteId = null, Error = null };
        NotifyStateChanged();
    }
}
