using Hasseware.AspNetCore.Components.Bpmn.Storage;

namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnEditorSession
{
    /// <summary>
    /// The binding the document comes from, when the host supplied one through the scope's
    /// <c>DocumentBinding</c> parameter.
    /// </summary>
    /// <remarks>
    /// Parts use it to reload or save a copy after an external change. The session itself never loads or
    /// saves; it only reports each published edit to the binding so its dirty state stays current.
    /// </remarks>
    public BpmnDocumentBinding? DocumentBinding { get; internal set; }

    /// <summary>Re-reads the binding's dirty flag and external change into <see cref="State"/>.</summary>
    internal void BindingChanged() => NotifyStateChanged();
}
