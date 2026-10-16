using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the question asking the user to allow an agent's consequential action; a UI kit
/// supplies the markup.
/// </summary>
/// <remarks>
/// <para>
/// A modal question, like the import offer: the action is waiting on the answer, and agreeing to
/// something without having read it is the failure this exists to prevent. Declining is always as easy
/// as allowing, and dismissing the dialog declines.
/// </para>
/// <para>
/// The request's details may quote diagram text an attacker could have written. Kits render them as
/// plain text in a visually separate list, never as markup and never as part of the question itself.
/// </para>
/// </remarks>
public abstract class BpmnAgentConfirmationBase : BpmnEditorComponentBase
{
    /// <summary>The accessible name of the dialog; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("AgentConfirmation.AriaLabel") : AriaLabel;

    /// <summary>The question awaiting an answer, or <see langword="null"/> to show nothing.</summary>
    protected BpmnConfirmationRequest? Request => State.PendingConfirmation;

    /// <summary>The name of the agent asking, for display.</summary>
    protected string AgentName => Request?.Origin.Agent ?? string.Empty;

    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        !ReferenceEquals(previous.PendingConfirmation, current.PendingConfirmation);

    /// <summary>Lets the action go ahead.</summary>
    /// <returns>A completed task.</returns>
    protected Task AllowAsync()
    {
        Session.ResolveConfirmation(allowed: true);
        return Task.CompletedTask;
    }

    /// <summary>Refuses the action.</summary>
    /// <returns>A completed task.</returns>
    protected Task DenyAsync()
    {
        Session.ResolveConfirmation(allowed: false);
        return Task.CompletedTask;
    }
}
