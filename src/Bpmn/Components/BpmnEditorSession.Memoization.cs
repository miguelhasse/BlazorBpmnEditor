using System.Collections.Immutable;
using System.Globalization;
using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.AspNetCore.Components.Bpmn.Resources;

using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnEditorSession
{
    // Session-owned edits and read-only inspection replace documents, never mutate their collections.
    // Keep only the current document's derived values, so old history is not retained by these caches.
    private readonly DerivedList<BpmnGraphNode> _graphCache = new();
    private readonly DerivedList<BpmnDiagramTarget> _diagramsCache = new();
    private readonly DerivedList<(string Id, string? Name)> _callablesCache = new();
    private readonly DerivedList<BpmnMenuEntry> _menuCache = new();
    private readonly DerivedList<BpmnContextualAction> _actionsCache = new();
    private readonly Dictionary<string, (BpmnElementSnapshot? Snapshot, BpmnElementTemplate? Template)> _descriptions = new(StringComparer.Ordinal);
    private IReadOnlyList<BpmnExtensionSchema> _descriptionSchemas = [];
    private IBpmnExtensionGroupBuilder[] _descriptionBuilders = [];
    private IBpmnPropertyLabelProvider? _descriptionLabels;
    private bool _descriptionOfferSchemas;
    private bool _descriptionHonorEntries;
    private int _descriptionRevision;
    private int _stateDescriptionRevision;
    private BpmnDocument? _describedDocument;
    private string? _describedDiagramId;
    private Func<string, BpmnElementSnapshot?>? _describeFunction;
    private BpmnExtensionOptions? _describedExtensions;
    private BpmnTemplateOptions? _describedTemplates;
    private bool _describedReadOnly;
    private string? _describedCulture;
    private BpmnDisplayLocalizer? _displayText;
    private bool _expansionsDirty;
    private ImmutableDictionary<string, BpmnExpansionState> _expansionSnapshot = ImmutableDictionary<string, BpmnExpansionState>.Empty;
    private BpmnDocument? _parameterDocument;
    private bool _parameterValidation;
    private bool _parametersInitialized;

    private IReadOnlyList<BpmnGraphNode> CachedAccessibleGraph()
    {
        if (!_graphCache.Matches(DrawnDocument, AccessibleGraphOverride, text: ActiveDiagramId)
            || (AccessibleGraphOverride is { } graph && !_graphCache.Items.SequenceEqual(graph)))
        {
            var rebuilt = AccessibleGraphOverride ?? (DrawnDocument is { } document
                ? BpmnAccessibleGraph.For(document, ActiveDiagramId) : []);

            // A move, a resize or a style change leaves the graph as it was. Keeping the old list lets the tree see "nothing changed"
            // by reference and skip rebuilding a thousand items for an edit that did not touch one.
            if (_graphCache.IsEquivalentTo(rebuilt)) _graphCache.Keep(DrawnDocument, AccessibleGraphOverride, text: ActiveDiagramId);
            else _graphCache.Store(rebuilt, DrawnDocument, AccessibleGraphOverride, text: ActiveDiagramId);
        }
        return _graphCache.Value;
    }

    private IReadOnlyList<BpmnDiagramTarget> CachedDiagrams()
    {
        if (!_diagramsCache.Matches(EditedDocument, DiagramsOverride)
            || (DiagramsOverride is { } diagrams && !_diagramsCache.Items.SequenceEqual(diagrams)))
        {
            _diagramsCache.Store(DiagramsOverride ?? (EditedDocument is { } document
                ? BpmnDiagramNavigation.Diagrams(document) : []), EditedDocument, DiagramsOverride);
        }
        return _diagramsCache.Value;
    }

    private IReadOnlyList<(string Id, string? Name)> CachedCallableDefinitions()
    {
        if (!_callablesCache.Matches(EditedDocument))
            _callablesCache.Store(EditedDocument is { } document ? BpmnDocumentEditor.CallableDefinitions(document) : [], EditedDocument);
        return _callablesCache.Value;
    }

    /// <summary>
    /// Localizes built-in command and snapshot captions with the host's <see cref="IStringLocalizer{T}"/> when
    /// one is registered, falling back to the built-in resources for the current UI culture.
    /// </summary>
    private BpmnDisplayLocalizer DisplayText => _displayText ??=
        _profileServices.GetService(typeof(IStringLocalizer<BpmnStrings>)) is IStringLocalizer<BpmnStrings> strings
            ? new BpmnDisplayLocalizer(strings)
            : _profileServices.GetService(typeof(BpmnDisplayLocalizer)) as BpmnDisplayLocalizer ?? BpmnDisplayLocalizer.CurrentCulture;

    private void EnsureDescriptionInputs()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        var extensions = _options.Value.Extensions;
        var templates = _options.Value.Templates;
        var schemas = extensions.Registry.Schemas;
        if (!ReferenceEquals(_describedDocument, EditedDocument) || _describedDiagramId != ActiveDiagramId
            || _describedReadOnly != IsReadOnly
            || _describedCulture != culture
            || !ReferenceEquals(_describeFunction, DescribeOverride)
            || !ReferenceEquals(_describedExtensions, extensions) || !ReferenceEquals(_describedTemplates, templates)
            || !_descriptionSchemas.SequenceEqual(schemas) || !_descriptionBuilders.SequenceEqual(extensions.GroupBuilders)
            || !ReferenceEquals(_descriptionLabels, extensions.LabelProvider)
            || _descriptionOfferSchemas != extensions.AlwaysOfferRegisteredSchemas
            || _descriptionHonorEntries != templates.HonorEntriesVisible)
        {
            _descriptions.Clear();
            _descriptionRevision++;
            _describedDocument = EditedDocument;
            _describedDiagramId = ActiveDiagramId;
            _describedReadOnly = IsReadOnly;
            _describedCulture = culture;
            _describeFunction = DescribeOverride;
            _describedExtensions = extensions;
            _describedTemplates = templates;
            _descriptionSchemas = schemas;
            _descriptionBuilders = [.. extensions.GroupBuilders];
            _descriptionLabels = extensions.LabelProvider;
            _descriptionOfferSchemas = extensions.AlwaysOfferRegisteredSchemas;
            _descriptionHonorEntries = templates.HonorEntriesVisible;
        }
        // Registries are mutable even though documents are not. Notice a formerly missing template.
        foreach (var entry in _descriptions)
        {
            if (entry.Value.Snapshot?.TemplateId is null || EditedDocument is not { } document || DescribeOverride is not null) continue;
            if (ReferenceEquals(entry.Value.Template, BpmnAppliedTemplate.Resolve(document, entry.Key, templates.Registry).Template)) continue;
            _descriptions.Clear();
            _descriptionRevision++;
            break;
        }
    }

    private BpmnElementSnapshot? CachedDescribe(string elementId)
    {
        EnsureDescriptionInputs();
        if (_descriptions.TryGetValue(elementId, out var cached)) return cached.Snapshot;
        var snapshot = DescribeOverride is { } describe ? describe(elementId) : EditedDocument is { } document
            ? BpmnElementInspector.Describe(document, elementId, IsReadOnly, _options.Value.Extensions, _options.Value.Templates, DisplayText) : null;
        if (snapshot is not null && DescribeOverride is null && ActiveDiagramId is { } diagramId)
        {
            var plane = EditedDocument?.Diagrams.FirstOrDefault(diagram => diagram.Id == diagramId)?.Plane;
            var shape = plane?.Shapes.FirstOrDefault(candidate => candidate.BpmnElement == elementId);
            var edge = plane?.Edges.FirstOrDefault(candidate => candidate.BpmnElement == elementId);
            // Elements the open diagram does not draw keep the inspector's geometry rather than losing it.
            if (shape is not null)
                snapshot = snapshot with { Bounds = shape.Bounds, LabelBounds = shape.Label?.Bounds, Waypoints = [] };
            else if (edge is not null)
                snapshot = snapshot with { Bounds = null, LabelBounds = edge.Label?.Bounds, Waypoints = edge.Waypoints };
        }
        var template = snapshot?.TemplateId is not null && EditedDocument is { } source && DescribeOverride is null
            ? BpmnAppliedTemplate.Resolve(source, elementId, _options.Value.Templates.Registry).Template : null;
        _descriptions.Add(elementId, (snapshot, template));
        return snapshot;
    }

    private IReadOnlyList<BpmnMenuEntry> MenuEntries
    {
        get
        {
            var flags = (IsReadOnly ? 1 : 0) | (CanPaste ? 2 : 0) | (CanUndo ? 4 : 0) | (CanRedo ? 8 : 0);
            var culture = CultureInfo.CurrentUICulture.Name;
            if (!_menuCache.Matches(_menu, _menuSnapshot, EditedDocument, culture, flags))
                _menuCache.Store(BuildMenuEntries, _menu, _menuSnapshot, EditedDocument, culture, flags);
            return _menuCache.Value;
        }
    }

    private IReadOnlyList<BpmnContextualAction> CachedContextualActions()
    {
        var culture = CultureInfo.CurrentUICulture.Name;
        if (!_actionsCache.Matches(_selectionSnapshot, text: culture))
            _actionsCache.Store(BpmnContextualActionsBase.DefaultsFor(_selectionSnapshot, DisplayText), _selectionSnapshot, text: culture);
        return _actionsCache.Value;
    }

    private ImmutableDictionary<string, BpmnExpansionState> ExpansionSnapshot()
    {
        if (_expansionsDirty)
        {
            _expansionSnapshot = _expansions.ToImmutableDictionary(pair => pair.Key,
                pair => new BpmnExpansionState(pair.Value.Expanded, pair.Value.Collapsed), StringComparer.Ordinal);
            _expansionsDirty = false;
        }
        return _expansionSnapshot;
    }

    private bool ParametersUnchanged(EditorConfig snapshot)
    {
        EnsureDescriptionInputs();
        var unchanged = _parametersInitialized && ReferenceEquals(_parameterDocument, Document)
            && (ReferenceEquals(snapshot, _snapshot) || snapshot == _snapshot)
            && ReferenceEquals(State.Document, EditedDocument) && ReferenceEquals(State.DrawnDocument, DrawnDocument)
            && State.IsReadOnly == IsReadOnly && State.ActiveDiagramId == ActiveDiagramId
            && State.Overlays.AccessibleTree.IsOpen == ShowAccessibleTree
            && State.Overlays.AccessibleTree.AriaLabel == AccessibleTreeLabel
            && _parameterValidation == ValidateOnChange
            && _graphCache.Matches(DrawnDocument, AccessibleGraphOverride, text: ActiveDiagramId)
            && _diagramsCache.Matches(EditedDocument, DiagramsOverride)
            && _stateDescriptionRevision == _descriptionRevision
            && State.Overlays.AccessibleTree.Nodes == _graphCache.Items && State.Diagrams == _diagramsCache.Items
            && (AccessibleGraphOverride is not { } graph || _graphCache.Items.SequenceEqual(graph))
            && (DiagramsOverride is not { } diagrams || _diagramsCache.Items.SequenceEqual(diagrams));
        _parameterDocument = Document;
        _parameterValidation = ValidateOnChange;
        _parametersInitialized = true;
        return unchanged;
    }

    private static ImmutableArray<T> PreserveArray<T>(ImmutableArray<T> previous, IReadOnlyList<T> current)
    {
        if (previous.Length == current.Count)
        {
            var comparer = EqualityComparer<T>.Default;
            var same = true;
            for (var i = 0; i < previous.Length; i++)
                if (!comparer.Equals(previous[i], current[i])) { same = false; break; }
            if (same) return previous;
        }
        return [.. current];
    }

    private static T PreserveRecord<T>(T previous, T current) where T : class =>
        EqualityComparer<T>.Default.Equals(previous, current) ? previous : current;

    private sealed class DerivedList<T>
    {
        private object? _first;
        private object? _second;
        private object? _third;
        private string? _text;
        private int _flags;
        private bool _initialized;
        internal ImmutableArray<T> Items { get; private set; } = [];
        internal IReadOnlyList<T> Value { get; private set; } = [];

        internal bool Matches(object? first, object? second = null, object? third = null, string? text = null, int flags = 0) =>
            _initialized && ReferenceEquals(_first, first) && ReferenceEquals(_second, second)
            && ReferenceEquals(_third, third) && _text == text && _flags == flags;

        /// <summary>Whether a rebuilt list holds the same content as the stored one, item by item (only meaningful for graph nodes).</summary>
        internal bool IsEquivalentTo(IReadOnlyList<BpmnGraphNode> rebuilt) =>
            _initialized && Items is ImmutableArray<BpmnGraphNode> current && current.Length == rebuilt.Count
            && Enumerable.Range(0, current.Length).All(index => BpmnGraphNode.SameContent(current[index], rebuilt[index]));

        /// <summary>Records new inputs for the stored list without replacing it.</summary>
        internal void Keep(object? first, object? second = null, object? third = null, string? text = null, int flags = 0)
        {
            _first = first;
            _second = second;
            _third = third;
            _text = text;
            _flags = flags;
        }

        internal void Store(IEnumerable<T> values, object? first, object? second = null, object? third = null, string? text = null, int flags = 0)
        {
            Items = [.. values];
            Value = Items;
            _first = first;
            _second = second;
            _third = third;
            _text = text;
            _flags = flags;
            _initialized = true;
        }
    }
}
