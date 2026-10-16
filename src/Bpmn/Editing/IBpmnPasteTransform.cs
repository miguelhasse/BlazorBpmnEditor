using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>Transforms newly pasted elements after id remapping, inside the paste undo step.</summary>
public interface IBpmnPasteTransform
{
    /// <summary>Returns the transformed document. Throw InvalidOperationException to refuse the entire paste.</summary>
    public BpmnDocument Transform(BpmnDocument document, IReadOnlyList<string> pastedIds, EditingOptions options);
}
