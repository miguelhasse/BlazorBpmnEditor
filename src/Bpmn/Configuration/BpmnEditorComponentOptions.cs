using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Configuration;

/// <summary>Which component renders each <see cref="BpmnEditorPart"/>, app-wide.</summary>
/// <remarks>
/// <para>
/// Two layers are kept apart: components set with <see cref="Use{TComponent}"/> (the host's choice) and
/// those set with <see cref="UseDefault{TComponent}"/> (a UI kit's). The host's always wins, whatever
/// order the two registrations run in, so a kit's <c>AddXxx()</c> never overrides a host's
/// <c>Configure&lt;BpmnOptions&gt;</c>.
/// </para>
/// <para>
/// A composition's own slot (<c>ToolbarContent</c> and so on) outranks both, per editor instance.
/// </para>
/// </remarks>
public sealed class BpmnEditorComponentOptions
{
    private static readonly Dictionary<BpmnEditorPart, Type> RequiredBases = new()
    {
        [BpmnEditorPart.Toolbar] = typeof(BpmnToolbarBase),
        [BpmnEditorPart.Stencil] = typeof(BpmnStencilPanelBase),
        [BpmnEditorPart.PropertyPanel] = typeof(BpmnPropertyPanelBase),
        [BpmnEditorPart.ValidationPanel] = typeof(BpmnValidationPanelBase),
        [BpmnEditorPart.DiagramNavigator] = typeof(BpmnDiagramNavigatorBase),
        [BpmnEditorPart.ContextMenu] = typeof(BpmnContextMenuBase),
        [BpmnEditorPart.ContextualActions] = typeof(BpmnContextualActionsBase),
        [BpmnEditorPart.LabelEditor] = typeof(BpmnLabelEditorBase),
        [BpmnEditorPart.AccessibleTree] = typeof(BpmnAccessibleTreeBase),
        [BpmnEditorPart.ImportOffer] = typeof(BpmnImportOfferBase),
        [BpmnEditorPart.CalledElementPicker] = typeof(BpmnCalledElementPickerBase),
        [BpmnEditorPart.ShortcutHelp] = typeof(BpmnShortcutHelpBase),
    };

    internal int Revision { get; private set; }

    private readonly Dictionary<BpmnEditorPart, Type> _explicit = [];
    private readonly Dictionary<BpmnEditorPart, Type> _defaults = [];
    private readonly HashSet<BpmnEditorPart> _removed = [];

    /// <summary>The base class a component must derive from to render <paramref name="part"/>.</summary>
    /// <param name="part">The part.</param>
    /// <returns>The required base type.</returns>
    public static Type RequiredBase(BpmnEditorPart part) =>
        RequiredBases.TryGetValue(part, out var type)
            ? type
            : throw new ArgumentOutOfRangeException(nameof(part), part, "Unknown editor part.");

    /// <summary>The component configured for a part, or <see langword="null"/> when there is none.</summary>
    /// <param name="part">The part.</param>
    /// <returns>The host's component, else the kit default, else <see langword="null"/>.</returns>
    public Type? Get(BpmnEditorPart part)
    {
        if (_removed.Contains(part)) return null;
        if (_explicit.TryGetValue(part, out var type)) return type;
        return _defaults.GetValueOrDefault(part);
    }

    /// <summary>The component for a part within a composition: the host's, else the composition's kit, else the app-wide default.</summary>
    /// <param name="part">The part.</param>
    /// <param name="kit">The composition's own defaults, or <see langword="null"/> for none.</param>
    /// <returns>The component, or <see langword="null"/> when there is none.</returns>
    public Type? Get(BpmnEditorPart part, BpmnKitDefaults? kit)
    {
        if (_removed.Contains(part)) return null;
        if (_explicit.TryGetValue(part, out var type)) return type;
        return kit?.Components._defaults.GetValueOrDefault(part) ?? _defaults.GetValueOrDefault(part);
    }

    /// <summary>Renders <paramref name="part"/> with <typeparamref name="TComponent"/>, overriding any default.</summary>
    /// <typeparam name="TComponent">A component deriving from the part's base class.</typeparam>
    /// <param name="part">The part.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="TComponent"/> does not derive from the part's base class.</exception>
    public BpmnEditorComponentOptions Use<TComponent>(BpmnEditorPart part) where TComponent : IComponent
    {
        Validate(part, typeof(TComponent));
        Revision++;
        _explicit[part] = typeof(TComponent);
        _removed.Remove(part);
        return this;
    }

    /// <summary>Renders <paramref name="part"/> with <typeparamref name="TComponent"/> unless the host chose otherwise.</summary>
    /// <remarks>For UI kits: it neither overrides <see cref="Use{TComponent}"/> nor revives a part the host removed.</remarks>
    /// <typeparam name="TComponent">A component deriving from the part's base class.</typeparam>
    /// <param name="part">The part.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentException"><typeparamref name="TComponent"/> does not derive from the part's base class.</exception>
    public BpmnEditorComponentOptions UseDefault<TComponent>(BpmnEditorPart part) where TComponent : IComponent
    {
        Validate(part, typeof(TComponent));
        if (!_defaults.ContainsKey(part))
        {
            _defaults[part] = typeof(TComponent);
            Revision++;
        }
        return this;
    }

    /// <summary>Stops <paramref name="part"/> being rendered, whatever a kit registers before or after.</summary>
    /// <param name="part">The part.</param>
    /// <returns>These options, for chaining.</returns>
    public BpmnEditorComponentOptions Remove(BpmnEditorPart part)
    {
        Revision++;
        _explicit.Remove(part);
        _defaults.Remove(part);
        _removed.Add(part);
        return this;
    }

    private static void Validate(BpmnEditorPart part, Type component)
    {
        var required = RequiredBase(part);
        if (component.IsAbstract || !required.IsAssignableFrom(component))
            throw new ArgumentException($"{component.Name} cannot render {part}: it must be a concrete component deriving from {required.Name}.", nameof(component));
    }
}
