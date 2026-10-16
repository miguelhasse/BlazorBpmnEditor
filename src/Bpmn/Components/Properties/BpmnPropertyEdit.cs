namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// One edited property, before it becomes a command.
/// </summary>
/// <remarks>
/// The form reports what changed and leaves the deciding to whoever owns the elements. That is
/// what lets the same markup serve a context menu editing one element and a panel editing a
/// selection: the difference between them is how many commands the value turns into, which is not
/// a rendering question.
/// </remarks>
/// <param name="Name">The property's name, as the descriptor declared it.</param>
/// <param name="Value">The submitted value, or <see langword="null"/> for "absent".</param>
public sealed record BpmnPropertyEdit(string Name, string? Value);
