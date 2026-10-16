using Hasseware.AspNetCore.Components.Bpmn.Model;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// What a property surface tells the entry editors rendered inside it, cascaded by
/// <see cref="BpmnPropertyScopeProvider"/>.
/// </summary>
/// <remarks>
/// <para>
/// One typed context instead of loose named cascades: the CSS block the surface styles its
/// controls with, how tall a multiline box is, which editor component each entry kind resolves
/// to, whether the surface is inspecting rather than editing, the document being edited and the
/// surface's edit revision.
/// </para>
/// <para>
/// The constructor is internal so a scope can only come from a provider: an entry editor can rely
/// on it but cannot forge one. Property surfaces in other assemblies create one by rendering
/// <see cref="BpmnPropertyScopeProvider"/>.
/// </para>
/// </remarks>
public sealed class BpmnPropertyScope
{
    /// <summary>The CSS block used when no surface says otherwise.</summary>
    public const string DefaultBlock = "bpmn-properties";

    /// <summary>The multiline height used when no surface says otherwise.</summary>
    public const int DefaultMultilineRows = 3;

    /// <summary>The scope an entry editor sees when it is rendered outside any property surface.</summary>
    internal static BpmnPropertyScope Default { get; } = new(
        DefaultBlock, DefaultMultilineRows, new BpmnPropertyEditorRegistry(), false, null, 0);

    internal BpmnPropertyScope(
        string block,
        int multilineRows,
        BpmnPropertyEditorRegistry editors,
        bool isReadOnly,
        BpmnDocument? document,
        int revision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(block);
        ArgumentNullException.ThrowIfNull(editors);
        ArgumentOutOfRangeException.ThrowIfLessThan(multilineRows, 1);

        Block = block;
        MultilineRows = multilineRows;
        Editors = editors;
        IsReadOnly = isReadOnly;
        Document = document;
        Revision = revision;
    }

    /// <summary>The CSS block the surface styles its controls with, such as <c>bpmn-menu</c>.</summary>
    public string Block { get; }

    /// <summary>Rows a multiline text box is given.</summary>
    public int MultilineRows { get; }

    /// <summary>The editor component each property entry resolves to.</summary>
    public BpmnPropertyEditorRegistry Editors { get; }

    /// <summary>Whether the surface is inspecting rather than editing.</summary>
    public bool IsReadOnly { get; }

    /// <summary>The document the surface edits, when it knows it.</summary>
    public BpmnDocument? Document { get; }

    /// <summary>How many edits the surface has submitted.</summary>
    public int Revision { get; }
}
