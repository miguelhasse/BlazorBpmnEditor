using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Hasseware.FluentUI.AspNetCore.Components.Bpmn.FeelSharp;

/// <summary>Fluent UI rendering of the managed FEEL editor.</summary>
public partial class FluentBpmnFeelSharpCodeEditor
{
    private FluentFeelSharpInput? _textArea;

    internal override ElementReference InputElement => _textArea?.Element ?? default;
    internal override string AttachMethod => "attachFluent";

    private void DraftChanged(string? value, int generation) => Input(new ChangeEventArgs { Value = value }, generation);

    private Task CommitFallbackAsync(FocusEventArgs _, int generation) =>
        ChangeAsync(new ChangeEventArgs { Value = Expression }, generation);
}
