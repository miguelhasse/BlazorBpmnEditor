namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>A question put to the user before an agent's consequential action goes ahead.</summary>
/// <remarks>
/// <para>
/// An agent acting through the editor (for example a WebMCP tool) asks before anything that cannot be
/// casually undone: deleting, replacing the document, writing to storage. The editor shows the question
/// with the <see cref="Configuration.BpmnEditorPart.AgentConfirmation"/> part, and the action proceeds
/// only on the user's explicit yes.
/// </para>
/// <para>
/// <see cref="Title"/> and <see cref="Summary"/> are written by the requesting module, localized, and
/// describe the action in the user's terms. <see cref="Details"/> may quote diagram content such as
/// element names, which is user-controlled text: kits render it as plain text, never as markup, and keep
/// it visually apart from the module's own words.
/// </para>
/// </remarks>
/// <param name="Title">What is being asked, such as "Delete 3 elements?".</param>
/// <param name="Summary">What will happen if the user allows it.</param>
/// <param name="Origin">The agent asking; never <see cref="BpmnEditOrigin.User"/>.</param>
/// <param name="Details">Specifics, such as the names of the elements affected; quoted, untrusted text.</param>
/// <param name="IsDestructive">Whether the action removes or replaces content, so kits can style it as such.</param>
public sealed record BpmnConfirmationRequest(
    string Title,
    string Summary,
    BpmnEditOrigin Origin,
    IReadOnlyList<string> Details,
    bool IsDestructive = false);
