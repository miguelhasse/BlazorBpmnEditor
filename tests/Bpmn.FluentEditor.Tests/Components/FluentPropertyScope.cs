using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

internal static class FluentPropertyScope
{
    internal static BpmnPropertyScope Default { get; } = new(BpmnPropertyScope.DefaultBlock,
        BpmnPropertyScope.DefaultMultilineRows, FluentBpmnDefaults.Register(new BpmnPropertyEditorRegistry()), false, null, 0);
}
