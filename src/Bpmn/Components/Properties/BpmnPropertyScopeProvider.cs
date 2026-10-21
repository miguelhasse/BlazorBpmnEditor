using Hasseware.AspNetCore.Components.Bpmn.Model;

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// Creates a <see cref="BpmnPropertyScope"/> and cascades it to the property entries rendered in
/// its child content. Renders no markup of its own.
/// </summary>
/// <remarks>
/// <para>
/// This is how a property surface — the built-in form and panel, or one in a UI-kit package —
/// tells its entry editors how to look and behave. Every parameter is optional: a value left unset
/// is inherited from an enclosing scope, and from the defaults when there is none. A nested
/// provider can therefore override a single value, such as the editor registry, without
/// restating the rest.
/// </para>
/// <para>
/// Read-only only accumulates: a nested provider can make its content read-only but cannot make
/// editable what an enclosing surface is inspecting.
/// </para>
/// </remarks>
public sealed class BpmnPropertyScopeProvider : ComponentBase
{
    [Inject]
    private Microsoft.Extensions.Options.IOptions<Configuration.BpmnOptions> Options { get; set; } = default!;

    [CascadingParameter]
    private BpmnEditorSession? Session { get; set; }

    [CascadingParameter]
    private BpmnPropertyScope? Parent { get; set; }

    /// <summary>The CSS block the controls are styled with, such as <c>bpmn-menu</c>.</summary>
    [Parameter]
    public string? Block { get; set; }

    /// <summary>Rows a multiline text box is given.</summary>
    [Parameter]
    public int? MultilineRows { get; set; }

    /// <summary>The editor component each property entry resolves to.</summary>
    [Parameter]
    public BpmnPropertyEditorRegistry? Editors { get; set; }

    /// <summary>Whether the content is inspected rather than edited.</summary>
    [Parameter]
    public bool? IsReadOnly { get; set; }

    /// <summary>The document the surface edits.</summary>
    [Parameter]
    public BpmnDocument? Document { get; set; }

    /// <summary>How many edits the surface has submitted.</summary>
    [Parameter]
    public int? Revision { get; set; }

    /// <summary>The property entries that receive the scope.</summary>
    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    /// <summary>The scope currently cascaded.</summary>
    internal BpmnPropertyScope Scope { get; private set; } = BpmnPropertyScope.Default;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var parent = Parent ?? BpmnPropertyScope.Default;
        var block = Block is { Length: > 0 } own ? own : parent.Block;
        var rows = MultilineRows ?? parent.MultilineRows;
        var editors = Editors ?? Parent?.Editors ?? Session?.Editors ?? Options.Value.Properties.Editors;
        var readOnly = parent.IsReadOnly || IsReadOnly == true;
        var document = Document ?? parent.Document;
        var revision = Revision ?? parent.Revision;

        if (Scope.Block == block && Scope.MultilineRows == rows && ReferenceEquals(Scope.Editors, editors)
            && Scope.IsReadOnly == readOnly && ReferenceEquals(Scope.Document, document) && Scope.Revision == revision)
        {
            return;
        }

        Scope = new BpmnPropertyScope(block, rows, editors, readOnly, document, revision);
    }

    /// <inheritdoc />
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<CascadingValue<BpmnPropertyScope>>(0);
        builder.AddComponentParameter(1, nameof(CascadingValue<>.Value), Scope);
        builder.AddComponentParameter(2, nameof(CascadingValue<>.ChildContent), ChildContent);
        builder.CloseComponent();
    }
}
