namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

internal static class StandardPropertyScope
{
    internal static BpmnPropertyScope Default { get; } = new(BpmnPropertyScope.DefaultBlock,
        BpmnPropertyScope.DefaultMultilineRows, BpmnStandardDefaults.Register(new BpmnPropertyEditorRegistry()), false, null, 0);
}
