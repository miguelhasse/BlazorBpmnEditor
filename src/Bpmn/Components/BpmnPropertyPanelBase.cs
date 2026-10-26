using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>
/// The logic of the property panel (implementation plan M6; specification sections 6.1 and 7); a
/// UI kit supplies the markup.
/// </summary>
/// <remarks>
/// <para>
/// A persistent surface over the same snapshot the context menu binds to, so the two cannot
/// disagree about what an element is or what may be changed about it. Everything shown comes from
/// the snapshot: the panel never sees a JointJS cell and never reads the SVG, so it cannot display
/// a value the model did not put there.
/// </para>
/// <para>
/// With more than one element selected it offers the properties every one of them understands,
/// blanks a value the selection does not agree on, and submits the change as a Only
/// <see cref="CompoundCommand"/> — because "set five things to Executable" is one decision and one
/// undo, not five of each.
/// </para>
/// <para>
/// Read-only needs no separate branch. The snapshot arrives with every property read-only, so the
/// same markup renders an inspector.
/// </para>
/// </remarks>
public abstract class BpmnPropertyPanelBase : BpmnEditorComponentBase
{
    /// <inheritdoc />
    protected override bool ShouldRenderOnStateChange(BpmnEditorState previous, BpmnEditorState current) =>
        _groupsChangedOnParameters || _editorsChangedOnParameters || GroupTemplate is not null || EntryTemplate is not null || EmptyTemplate is not null
            || previous.Selection != current.Selection || previous.IsReadOnly != current.IsReadOnly
            || previous.MessageFocusOrigin != current.MessageFocusOrigin
            || !ReferenceEquals(previous.Document, current.Document)
            || !ReferenceEquals(previous.DrawnDocument, current.DrawnDocument)
            || !ReferenceEquals(previous.Validation, current.Validation)
            || !ReferenceEquals(previous.Configuration, current.Configuration)
            || !ReferenceEquals(previous.Overlays.ContextualActions.Snapshot, current.Overlays.ContextualActions.Snapshot);

    private BpmnDocument? _groupDocument;
    private bool _groupReadOnly;
    private bool _groupsInitialized;
    private bool _groupsChangedOnParameters;
    private bool _editorsChangedOnParameters;
    private BpmnPropertyEditorRegistry? _parameterEditors;
    private int _parameterEditorsRevision;
    private System.Globalization.CultureInfo? _groupCulture;
    private IBpmnPropertiesProvider[] _groupProviders = [];
    private readonly IBpmnPropertiesProvider[] _builtIns = [new BpmnGeneralPropertiesProvider(),
        new BpmnElementTemplatePropertiesProvider(), new BpmnSchemaPropertiesProvider(), new BpmnRawExtensionPropertiesProvider()];
    private readonly Dictionary<BpmnPropertyEntry, (BpmnPropertyEntryContext Context, Dictionary<string, object> Parameters)> _entryContexts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BpmnPropertyGroup, BpmnPropertyGroupContext> _groupContexts = new(ReferenceEqualityComparer.Instance);
    private IReadOnlyList<BpmnFeelVariable> _variables = [];
    private string? _variableElementId;
    private BpmnDocument? _variableDocument;
    private int _variableRequest;

    /// <summary>Provider chain; null uses configured and DI providers plus the built-ins.</summary>
    [Parameter]
    public IEnumerable<IBpmnPropertiesProvider>? Providers { get; set; }

    /// <summary>Replaces group rendering; the context supplies the resolved entry renderer.</summary>
    [Parameter]
    public RenderFragment<BpmnPropertyGroupContext>? GroupTemplate { get; set; }

    /// <summary>Replaces entry rendering while retaining guarded write callbacks.</summary>
    [Parameter]
    public RenderFragment<BpmnPropertyEntryContext>? EntryTemplate { get; set; }

    /// <summary>Replaces the empty-selection or empty-property message.</summary>
    [Parameter]
    public RenderFragment? EmptyTemplate { get; set; }

    /// <summary>Accessible name of the panel; a localized default when not set.</summary>
    [Parameter]
    public string? AriaLabel { get; set; }

    [Inject]
    private IServiceProvider Services { get; set; } = default!;

    /// <summary>The accessible name in force.</summary>
    protected string EffectiveAriaLabel => string.IsNullOrWhiteSpace(AriaLabel) ? Text("PropertyPanel.AriaLabel") : AriaLabel;

    /// <summary>The selected elements, described.</summary>
    protected IReadOnlyList<BpmnElementSnapshot> Snapshots { get; private set; } = [];

    /// <summary>The diagnostics raised against the selection.</summary>
    protected IReadOnlyList<BpmnDiagnostic> Diagnostics => Session.SelectionDiagnostics;

    /// <summary>Every place the document names the selected element.</summary>
    protected IReadOnlyList<BpmnReferenceSite> Usages => Session.SelectionUsages;

    /// <summary>The panel's options.</summary>
    protected BpmnPropertyPanelOptions PanelOptions { get => Services.GetService<IOptions<BpmnOptions>>()?.Value.Properties ?? field; } = new();

    /// <summary>Whether the panel is inspecting rather than editing.</summary>
    /// <remarks>
    /// Only the id needs asking. Every property arrives already marked read-only in the snapshot,
    /// but the id is not a property — it is rendered directly, so it has nothing to read it from.
    /// </remarks>
    protected bool Inspecting => State.IsReadOnly;

    /// <summary>The document being edited, which variable hints and expression checks resolve against.</summary>
    protected BpmnDocument? Document => State.DrawnDocument;

    /// <summary>The edit revision, which keys every control.</summary>
    protected int Revision { get; private set; }

    /// <summary>The groups of the current selection.</summary>
    protected IReadOnlyList<BpmnPropertyGroup> Groups { get; private set; } = [];

    /// <summary>The one selected element, or <see langword="null"/> when several are.</summary>
    protected BpmnElementSnapshot? Only => Snapshots.Count == 1 ? Snapshots[0] : null;

    /// <summary>The panel heading for the selection.</summary>
    protected string Title => Only is { } one
        ? one.Label is { Length: > 0 } label ? label : Display.ElementType(one.ElementType)
        : Text("PropertyPanel.Elements", Snapshots.Count);

    /// <summary>The element type of a Only selection.</summary>
    protected string? Subtitle => Only is { } one ? Display.ElementType(one.ElementType) : null;

    /// <summary>The message shown when the selection has nothing to edit.</summary>
    protected string NothingToEdit => Only is null
        ? Text("PropertyPanel.NoCommonProperties")
        : Text("PropertyPanel.NoProperties");

    /// <summary>The vendor extensions of a Only selection.</summary>
    protected IReadOnlyList<BpmnExtensionSummary> Extensions => Only?.Extensions ?? [];

    /// <summary>Localized text of a diagnostic.</summary>
    /// <param name="diagnostic">The diagnostic.</param>
    /// <returns>Its message in the current culture.</returns>
    protected string MessageOf(BpmnDiagnostic diagnostic) => DiagnosticLocalizer.Localize(diagnostic);

    /// <summary>Selects the element a usage belongs to.</summary>
    /// <param name="usage">The usage.</param>
    /// <returns>A task that completes once the element is selected.</returns>
    protected Task SelectUsageAsync(BpmnReferenceSite usage) => Session.GoToUsageAsync(usage);

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        var elementId = Only?.ElementId;
        var document = Document;

        if (_variableElementId == elementId && ReferenceEquals(_variableDocument, document))
            return;

        _variableElementId = elementId;
        _variableDocument = document;
        _variables = [];
        var request = ++_variableRequest;

        if (elementId is null || document is null || Services.GetService<IBpmnFeelVariableProvider>() is not { } provider)
            return;

        var variables = await provider.GetVariablesAsync(document, elementId);

        if (request == _variableRequest)
            _variables = variables;
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        base.OnParametersSet();

        _groupsChangedOnParameters = false;
        var editors = Session.Editors;
        _editorsChangedOnParameters = !ReferenceEquals(_parameterEditors, editors) || _parameterEditorsRevision != editors.Revision;
        _parameterEditors = editors;
        _parameterEditorsRevision = editors.Revision;
        var snapshots = Session.SelectionSnapshots;
        IEnumerable<IBpmnPropertiesProvider> providers = Providers ??
            _builtIns.Concat(PanelOptions.Providers).Concat(Services.GetServices<IBpmnPropertiesProvider>());
        var culture = System.Globalization.CultureInfo.CurrentUICulture;

        if (_groupsInitialized && ReferenceEquals(_groupDocument, Document) && _groupReadOnly == Inspecting
            && ReferenceEquals(_groupCulture, culture) && Snapshots.SequenceEqual(snapshots)
            && _groupProviders.SequenceEqual(providers))
            return;

        _groupsChangedOnParameters = true;
        _groupsInitialized = true;
        Snapshots = snapshots;
        _groupDocument = Document;
        _groupReadOnly = Inspecting;
        _groupCulture = culture;
        _groupProviders = providers.ToArray();
        _entryContexts.Clear();
        _groupContexts.Clear();

        if (snapshots.Count == 0)
        {
            Groups = [];
            return;
        }

        var element = Only ?? snapshots[0] with
        {
            Properties = Editable,
            ExtensionProperties = [],
            Extensions = [],
            ExtensionGroups = [],
            TemplateId = null,
            TemplateVersion = null
        };

        IReadOnlyList<BpmnPropertyGroup> groups = [];
        // Snapshot-computed extension groups can be appended by a later provider at this seam.
        foreach (var provider in _groupProviders.OrderByDescending(provider => provider.Priority))
        {
            groups = provider.GetGroups(element, groups);
        }

        Groups = groups.Select(DisplayableGroup).Where(group => group is not null)
            .Select(group => Display.Group(group!)).ToList();

        BpmnPropertyGroup? DisplayableGroup(BpmnPropertyGroup group)
        {
            if (Only is null && group.Namespace is not null)
            {
                return null;
            }

            var entries = group.Entries.Where(entry => entry.Kind != BpmnPropertyEntryKind.Hidden
                && (Only is not null || entry.Binding is null))
                .Select(entry => entry with { GroupId = group.Id }).ToList();
            var children = group.Children.Select(DisplayableGroup).Where(child => child is not null)
                .Select(child => child!).ToList();

            return entries.Count == 0 && children.Count == 0
                ? null
                : group with { Entries = entries, Children = children };
        }
    }

    /// <summary>Builds the context an entry editor is rendered with.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The context.</returns>
    protected BpmnPropertyEntryContext BuildEntryContext(BpmnPropertyEntry entry)
    {
        var readOnly = Inspecting || entry.IsReadOnly || entry.Kind == BpmnPropertyEntryKind.ReadOnly;
        var elementId = Only?.ElementId;
        if (_entryContexts.TryGetValue(entry, out var cached) && cached.Context.Revision == Revision
            && cached.Context.IsReadOnly == readOnly && cached.Context.ElementId == elementId
            && ReferenceEquals(cached.Context.Variables, _variables))
            return cached.Context;

        var context = new BpmnPropertyEntryContext(entry,
            cached.Context?.OnValueChanged ?? EventCallback.Factory.Create<string?>(this, value => ApplyEntryAsync(entry, value, null)),
            cached.Context?.OnRowsChanged ?? EventCallback.Factory.Create<IReadOnlyList<IReadOnlyDictionary<string, string?>>>(this,
                rows => ApplyEntryAsync(entry, null, rows)), readOnly, Revision)
        {
            ElementId = elementId,
            Variables = _variables,
        };
        var parameters = cached.Parameters ?? [];
        parameters["Context"] = context;
        _entryContexts[entry] = (context, parameters);
        return context;
    }

    /// <summary>Builds the context a group template is rendered with.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The context.</returns>
    protected BpmnPropertyGroupContext BuildGroupContext(BpmnPropertyGroup group)
    {
        if (_groupContexts.TryGetValue(group, out var cached))
            return cached;

        var context = new BpmnPropertyGroupContext(group, group.Entries,
            entryContext => RenderEntry(BuildEntryContext(entryContext.Entry)), Inspecting)
        {
            Children = group.Children.Select(BuildGroupContext).ToArray(),
            RenderEntry = entry => RenderEntry(BuildEntryContext(entry)),
        };
        _groupContexts[group] = context;
        return context;
    }

    /// <summary>Renders an entry through the host's template or the built-in editors.</summary>
    /// <param name="context">The entry context.</param>
    /// <returns>The rendering.</returns>
    protected RenderFragment RenderEntry(BpmnPropertyEntryContext context) => EntryTemplate is { } template
        ? template(context)
        : builder =>
        {
            if (context.Entry.Kind == BpmnPropertyEntryKind.Hidden)
                return;

            builder.OpenComponent<DynamicComponent>(0);
            builder.SetKey($"{context.Entry.Id}:{context.Revision}");
            builder.AddComponentParameter(1, nameof(DynamicComponent.Type), Session.Editors.Resolve(context.Entry));
            builder.AddComponentParameter(2, nameof(DynamicComponent.Parameters),
                _entryContexts.TryGetValue(context.Entry, out var cached) && ReferenceEquals(cached.Context, context)
                    ? cached.Parameters : new Dictionary<string, object> { ["Context"] = context });
            builder.CloseComponent();
        };

    private async Task ApplyEntryAsync(BpmnPropertyEntry entry, string? value, IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows)
    {
        if (Inspecting || entry.IsReadOnly || entry.Kind is BpmnPropertyEntryKind.ReadOnly or BpmnPropertyEntryKind.Hidden || Snapshots.Count == 0)
            return;

        if (entry.Binding is not null)
        {
            if (Only is not { } element)
                return;

            Revision++;
            var command = BpmnPropertyEntryCommands.ToCommand(
                element.ElementId, entry, value, rows, PanelOptions.BindingCommandFactories);

            if (command is not null)
                await Session.ApplyPropertyCommandAsync(command);
            return;
        }

        if (rows is not null)
            return;

        var submitted = entry.Kind is BpmnPropertyEntryKind.Text or BpmnPropertyEntryKind.TextArea && value is { Length: 0 }
            ? null : value;
        await ApplyAsync(new BpmnPropertyEdit(entry.Id, submitted));
    }

    /// <summary>
    /// The properties on offer: one element's, or what a whole selection agrees it has.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Common means same name and same kind. Name alone is not enough — two families can both call
    /// something <c>name</c> while one offers a choice list the other has never heard of, and
    /// rendering a select built from the first element's choices would offer values the rest cannot
    /// take.
    /// </para>
    /// <para>
    /// A value the selection disagrees on is blanked rather than shown from the first element,
    /// because showing one element's value for all of them is the stale standard field section 7
    /// forbids. A read-only property in any element is read-only for the selection: a bulk edit
    /// that silently skipped the locked ones would report success for work it did not do.
    /// </para>
    /// </remarks>
    private IReadOnlyList<BpmnPropertyDescriptor> Editable
    {
        get
        {
            var snapshots = Snapshots;

            if (Only is { } one)
                return one.Properties;

            if (snapshots.Count == 0)
                return [];

            var common = new List<BpmnPropertyDescriptor>();

            foreach (var candidate in snapshots[0].Properties)
            {
                var others = snapshots
                    .Skip(1)
                    .Select(snapshot => snapshot.Properties
                        .FirstOrDefault(property => property.Name == candidate.Name && property.Kind == candidate.Kind))
                    .ToList();

                if (others.Exists(property => property is null))
                    continue;

                var agreed = others.TrueForAll(property => property!.Value == candidate.Value);
                var locked = candidate.IsReadOnly || others.Exists(property => property!.IsReadOnly);

                common.Add(candidate with
                {
                    Value = agreed ? candidate.Value : null,
                    IsReadOnly = locked,
                    Group = others.TrueForAll(property => property!.Group == candidate.Group)
                        ? candidate.Group : BpmnPropertyGroupKind.General,
                });
            }

            return common;
        }
    }

    /// <summary>
    /// Submits an id change as a rename.
    /// </summary>
    /// <remarks>
    /// A blank box is not a request to delete the id, so it is ignored rather than sent: an element
    /// without an id is not something the editor should offer to make by accident.
    /// </remarks>
    /// <param name="elementId">The current id.</param>
    /// <param name="proposed">The id typed.</param>
    /// <returns>A task that completes once the rename is applied or refused.</returns>
    protected async Task RenameAsync(string elementId, string? proposed)
    {
        // Bumped whatever happens, so the box is rebuilt from the document on the re-render that
        // follows. A blank or unchanged entry submits nothing, and without this it would sit there
        // showing an id the element does not have.
        Revision++;

        if (Inspecting || string.IsNullOrWhiteSpace(proposed) || proposed == elementId)
        {
            return;
        }

        await Session.ApplyPropertyCommandAsync(new RenameElementCommand(elementId, proposed));
    }

    private async Task ApplyAsync(BpmnPropertyEdit edit)
    {
        if (Inspecting || Snapshots.Count == 0) return;

        Revision++;

        var commands = Snapshots
            .Select(snapshot => new UpdatePropertiesCommand(snapshot.ElementId, edit.Name, edit.Value))
            .ToList();

        // One command for one element; otherwise one compound, so the whole bulk edit is a Only
        // step to undo and is refused as a whole if any part of it is.
        BpmnEditCommand command = commands.Count == 1
            ? commands[0]
            : new CompoundCommand(commands, Text("PropertyPanel.BulkEdit", edit.Name, commands.Count));

        await Session.ApplyPropertyCommandAsync(command);
    }
}
