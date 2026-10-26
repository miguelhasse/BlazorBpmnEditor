using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Message selection plus optional document-catalog actions.</summary>
public abstract class BpmnMessageReferenceEntryComponent : BpmnPropertyEntryComponent
{
    /// <summary>The authoring session; absent in standalone property forms.</summary>
    [CascadingParameter] protected BpmnEditorSession? MessageSession { get; set; }
    /// <summary>Focus returns here even when a saved edit rebuilt the property entry.</summary>
    protected ElementReference FocusTarget { get; set; }
    /// <summary>Only a single reference in the authoritative document can create and assign.</summary>
    protected bool CanAuthor => MessageSession is { HasMessageCatalog: true } session && Context.ElementId is { } id
        && session.State.Document?.Find(id) is not null
        && ReferenceEquals(session.State.Document, session.State.DrawnDocument);
    /// <summary>Choice captions include IDs so duplicate names remain distinguishable.</summary>
    protected IReadOnlyList<BpmnPropertyChoice> MessageChoices => Choices.Select(choice =>
    {
        if (choice.Value.Length == 0) return choice;
        var message = MessageSession?.State.Document?.Catalog.Messages.FirstOrDefault(item => item.Id == choice.Value);
        var name = message is null ? choice.Label : string.IsNullOrEmpty(message.Name) ? Text("MessageCatalog.Unnamed") : message.Name;
        return choice with { Label = $"{name} ({choice.Value})" };
    }).ToArray();
    /// <summary>Whether there are definitions to select.</summary>
    protected bool HasMessages => Entry.Choices.Any(choice => choice.Value.Length > 0);
    /// <summary>Whether the current value resolves to a locally editable definition.</summary>
    protected bool CanEditMessage => Entry.Value is { Length: > 0 } id
        && MessageSession?.State.Document?.Find(id) is Model.BpmnMessage;
    /// <summary>Starts create-and-assign without writing until Save.</summary>
    protected void NewMessage() => MessageSession?.NewMessage(Context.ElementId);
    /// <summary>Edits the shared name without changing the selected reference.</summary>
    protected void EditMessage()
    {
        if (Entry.Value is { Length: > 0 } id) MessageSession?.EditMessage(id, Context.ElementId);
    }
    /// <summary>Opens the catalog without implicitly assigning its new definitions.</summary>
    protected void ManageMessages() => MessageSession?.ShowMessages(Context.ElementId ?? "toolbar");
    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (Context.ElementId is { } id && MessageSession?.TakeMessageFocus(id) == true)
            await FocusTarget.FocusAsync(preventScroll: true).ConfigureAwait(true);
    }
}
