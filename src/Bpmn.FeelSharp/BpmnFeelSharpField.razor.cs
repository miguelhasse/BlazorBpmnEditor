using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Standalone FEEL field over the same guarded property editor behavior.</summary>
public partial class BpmnFeelSharpField
{
    private DynamicComponent? _editor;
    private string? _source;
    private string? _current;
    // Echoing a local commit as a new source would invalidate an in-flight native capture.
    private string? _editorSource;
    private bool _wasReadOnly;
    private Type? _editorType;
    private bool _initialized;
    private int _revision;
    private int _externalRevision;
    private bool _prefix;

    /// <summary>Expression value; prefix-free unless PersistencePrefix is enabled.</summary>
    [Parameter] public string? Value { get; set; }
    /// <summary>Committed value callback; native drafts do not commit through this callback.</summary>
    [Parameter] public EventCallback<string?> ValueChanged { get; set; }
    /// <summary>Finalized draft callback, independent of commits.</summary>
    [Parameter] public EventCallback<BpmnFeelSharpDraft> DraftChanged { get; set; }
    /// <summary>Advisory diagnostic callback.</summary>
    [Parameter] public EventCallback<BpmnFeelSharpDiagnosticsUpdate> DiagnosticsChanged { get; set; }
    /// <summary>Variable metadata supplied explicitly by the host.</summary>
    [Parameter] public IReadOnlyList<BpmnFeelVariable> Variables { get; set; } = [];
    /// <summary>Accessible field label.</summary>
    [Parameter] public string Label { get; set; } = "FEEL";
    /// <summary>Disable editing and commits.</summary>
    [Parameter] public bool ReadOnly { get; set; }
    /// <summary>Use BPMN persistence values with one optional leading '='. Default is prefix-free expression text.</summary>
    [Parameter] public bool PersistencePrefix { get; set; }
    /// <summary>Explicit reset revision for hosts that replace a draft with the same source value.</summary>
    [Parameter] public int Revision { get; set; }
    /// <summary>Textarea row count.</summary>
    [Parameter] public int Rows { get; set; } = 6;
    /// <summary>Alternate renderer inheriting BpmnFeelSharpEditorBase, such as the Fluent renderer.</summary>
    [Parameter] public Type EditorType { get; set; } = typeof(BpmnFeelSharpCodeEditor);
    /// <summary>Permit expanded editing when registration options enable it.</summary>
    [Parameter] public bool EnableExpansion { get; set; } = true;
    /// <summary>Hide editor chrome already supplied by an enclosing manager.</summary>
    [Parameter] public bool Minimal { get; set; }

    private BpmnPropertyEditorRegistry Registry { get; } = new();
    private IDictionary<string, object> Parameters
    {
        get
        {
            var revision = _revision;
            var value = PersistencePrefix ? _editorSource : _editorSource is null ? null : "=" + _editorSource;
            var context = new BpmnPropertyEntryContext(
                new("standalone", Label, BpmnPropertyEntryKind.Text) { Feel = BpmnFeelMode.Required, Value = value },
                EventCallback.Factory.Create<string?>(this, changed => CommitAsync(changed, revision)), default, ReadOnly, revision)
            { Variables = Variables };
            return new Dictionary<string, object>
            {
                ["Context"] = context,
                ["DiagnosticsChanged"] = DiagnosticsChanged,
                ["DraftChanged"] = DraftChanged,
                ["EnableExpansion"] = EnableExpansion,
                ["PropertyInput"] = false,
                ["Minimal"] = Minimal,
            };
        }
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!typeof(BpmnFeelSharpEditorBase).IsAssignableFrom(EditorType))
        {
            throw new ArgumentException("EditorType must inherit BpmnFeelSharpEditorBase.", nameof(EditorType));
        }
        if (!_initialized || _source != Value || _externalRevision != Revision || _prefix != PersistencePrefix)
        {
            _source = Value;
            _current = Value;
            _editorSource = Value;
            _externalRevision = Revision;
            _prefix = PersistencePrefix;
            _initialized = true;
            _revision++;
        }
        if (_wasReadOnly != ReadOnly || _editorType != EditorType)
        {
            _editorSource = _current;
            _wasReadOnly = ReadOnly;
            _editorType = EditorType;
        }
    }

    private async Task CommitAsync(string? value, int revision)
    {
        if (ReadOnly || revision != _revision)
        {
            return;
        }
        _current = PersistencePrefix || value is null ? value : FeelSharpSyntax.Normalize(value);
        await ValueChanged.InvokeAsync(_current);
    }

    /// <summary>Reads the real native selection; fails explicitly if rendering has not finished.</summary>
    public Task<BpmnFeelSharpDraft> CaptureAsync() => Editor.CaptureAsync();

    /// <summary>Applies a host-accepted edit through native source/generation/undo guards.</summary>
    public Task ApplyDraftAsync(BpmnFeelSharpDraft expected, string replacement) => Editor.ApplyDraftAsync(expected, replacement);

    private BpmnFeelSharpEditorBase Editor => _editor?.Instance as BpmnFeelSharpEditorBase ??
        throw new InvalidOperationException("The FEEL field has not rendered.");
}
