namespace Hasseware.AspNetCore.Components.Bpmn.Tools;

/// <summary>A tool host whose document has a selection a user made, such as an open editor.</summary>
/// <remarks>
/// Optional: a host that loads a stored document has nothing selected, and <c>bpmn_get_selection</c>
/// then reports an empty selection.
/// </remarks>
public interface IBpmnSelectionHost
{
    /// <summary>The ids of the selected elements and connectors, in selection order.</summary>
    public IReadOnlyList<string> SelectedIds { get; }
}
