using Hasseware.AspNetCore.Components.Bpmn;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.FeelSharp;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;
using Microsoft.AspNetCore.Components;

namespace Bpmn.Demo.Client.Pages;

public partial class FeelAuthoringHarness
{
    private string? _native = "=sum([1, 2, 3])";
    private string? _fluent = "=amount + 1";
    private string? _workspace = "false";
    private string? _notice;
    private int _revision;
    private bool _readOnly;
    private bool _authorize;
    private BpmnFeelSharpHostActions? _hostActions;
    private BpmnEditorScope? _editor;
    private BpmnEditorScope? _initializedEditor;
    private static readonly BpmnDocument Document = BpmnXmlReader.Read("""
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL" id="FeelDemo">
          <process id="Process"><task id="authoring-demo"/></process>
        </definitions>
        """).Document;

    /// <summary>Selects the standard HTML kit with <c>?plain=true</c>; defaults to the Fluent UI kit.</summary>
    [Parameter]
    public bool Plain { get; set; }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_editor is not null && !ReferenceEquals(_editor, _initializedEditor))
        {
            _initializedEditor = _editor;
            await _editor.Session.SetSelectionAsync(["authoring-demo"]);
        }
    }
    private static readonly BpmnFeelVariable[] Variables = [new("amount", "number", "Host-declared sample amount")];
    [Inject] private IBpmnFeelSharpEvaluation Evaluator { get; set; } = default!;
    private BpmnFeelSharpHostActions HostActions => _hostActions ??= new()
    {
        Suggest = (draft, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult($"({draft.Text})");
        },
        Share = async (draft, token) =>
        {
            token.ThrowIfCancellationRequested();
            _notice = Localizer["FeelAuthoring.Shared", draft.Text.Length];
            await InvokeAsync(StateHasChanged);
        },
    };
    private BpmnPropertyEntryContext NativeContext => Context("native", _native, value => _native = value);
    private BpmnPropertyEntryContext FluentContext => Context("fluent", _fluent, value => _fluent = value);
    private BpmnPropertyEntryContext Context(string id, string? value, Action<string?> changed) =>
        new(new(id, id, BpmnPropertyEntryKind.Text) { Feel = BpmnFeelMode.Required, Value = value },
            EventCallback.Factory.Create(this, changed), default, _readOnly, _revision)
        { ElementId = "authoring-demo", Variables = Variables };
    private void Reset()
    {
        _revision++;
        _native = "=sum([1, 2, 3])";
        _fluent = "=amount + 1";
        _workspace = "false";
        _notice = null;
    }
    private void ToggleReadOnly() => _readOnly = !_readOnly;
    private void ApplyWorkspace(string? value) => _workspace = value;
    private void CancelWorkspace() => _notice = Localizer["FeelAuthoring.Cancelled"];
}
