namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>An optional toolbar action owned by the last-focused property control.</summary>
/// <param name="Owner">Opaque lifetime identity of the control.</param>
/// <param name="ElementId">Selected semantic element.</param>
/// <param name="Label">Localized action caption.</param>
/// <param name="Enabled">Whether the action is currently available.</param>
/// <param name="ExecuteAsync">Opens the property's transactional manager.</param>
public sealed record BpmnPropertyAction(object Owner, string ElementId, string Label, bool Enabled, Func<Task> ExecuteAsync);
