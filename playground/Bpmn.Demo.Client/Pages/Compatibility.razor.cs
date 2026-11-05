using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Bpmn.Demo.Client.Pages;

public partial class Compatibility
{
    private BpmnPropertyEditorRegistry _registry = default!;
    private string? _value = "Preserved parameter";
    private bool _readOnly;

    [Inject] private IOptions<BpmnOptions> Options { get; set; } = default!;

    private BpmnPropertyEntryContext Context =>
        new(new("compatibility", "Custom value", BpmnPropertyEntryKind.Text)
        {
            EditorKey = "compatibility",
            Value = _value,
        }, EventCallback.Factory.Create<string?>(this, value => _value = value), default, _readOnly, 0);

    protected override void OnInitialized() =>
        _registry = new BpmnPropertyEditorRegistry().AddEditor<CompatibilityEntry>("compatibility")
            .WithKitDefaults(Options.Value.Properties.Editors);

    private void ToggleReadOnly() => _readOnly = !_readOnly;
}
