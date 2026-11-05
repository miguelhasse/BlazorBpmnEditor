using System.Diagnostics.CodeAnalysis;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Resolves components accepting a BpmnPropertyEntryContext parameter named Context.</summary>
public sealed class BpmnPropertyEditorRegistry
{
    internal int Revision { get; private set; }

    private BpmnComponentType? _feelEditor;
    private BpmnComponentType? _defaultFeelEditor;
    private readonly Dictionary<BpmnPropertyEntryKind, BpmnComponentType> _defaultKinds = [];
    private readonly Dictionary<BpmnPropertyEntryKind, BpmnComponentType> _kinds = [];
    private readonly Dictionary<string, BpmnComponentType> _keys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, BpmnComponentType> _defaultKeys = new(StringComparer.Ordinal);
    private readonly List<(Func<BpmnPropertyEntry, bool> Predicate, BpmnComponentType Component)> _predicates = [];

    /// <summary>Registers an editor for a kind.</summary>
    public BpmnPropertyEditorRegistry AddEditor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(BpmnPropertyEntryKind kind) where TComponent : IComponent
    {
        Revision++;
        _kinds[kind] = new(typeof(TComponent));
        return this;
    }

    /// <summary>Registers an explicitly keyed editor.</summary>
    public BpmnPropertyEditorRegistry AddEditor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(string editorKey) where TComponent : IComponent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(editorKey);
        Revision++;
        _keys[editorKey] = new(typeof(TComponent));
        return this;
    }

    /// <summary>Registers a matching editor; the last matching registration wins.</summary>
    public BpmnPropertyEditorRegistry AddEditor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(Func<BpmnPropertyEntry, bool> predicate) where TComponent : IComponent
    {
        ArgumentNullException.ThrowIfNull(predicate);
        Revision++;
        _predicates.Add((predicate, new(typeof(TComponent))));
        return this;
    }

    /// <summary>Replaces the default editor for required and optional FEEL expressions.</summary>
    public BpmnPropertyEditorRegistry AddFeelEditor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>() where TComponent : IComponent
    {
        Revision++;
        _feelEditor = new(typeof(TComponent));
        return this;
    }

    /// <summary>Registers the editor for a kind unless the host has registered one; for UI kits.</summary>
    public BpmnPropertyEditorRegistry UseDefaultEditor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(BpmnPropertyEntryKind kind) where TComponent : IComponent
    {
        if (_defaultKinds.TryAdd(kind, new(typeof(TComponent)))) Revision++;
        return this;
    }

    /// <summary>Registers a keyed kit default without replacing host editors.</summary>
    public BpmnPropertyEditorRegistry UseDefaultEditor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>(string key) where TComponent : IComponent
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (_defaultKeys.TryAdd(key, new(typeof(TComponent)))) Revision++;
        return this;
    }

    /// <summary>Registers the FEEL editor unless the host has replaced it; for UI kits.</summary>
    public BpmnPropertyEditorRegistry UseDefaultFeelEditor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TComponent>() where TComponent : IComponent
    {
        if (_defaultFeelEditor is null)
        {
            _defaultFeelEditor = new(typeof(TComponent));
            Revision++;
        }
        return this;
    }

    /// <summary>A registry resolving like this one, but preferring <paramref name="kit"/>'s defaults over this registry's own.</summary>
    /// <param name="kit">The composition's kit defaults.</param>
    /// <returns>A snapshot; host registrations still outrank every default.</returns>
    public BpmnPropertyEditorRegistry WithKitDefaults(BpmnPropertyEditorRegistry kit)
    {
        ArgumentNullException.ThrowIfNull(kit);
        var merged = new BpmnPropertyEditorRegistry { _feelEditor = _feelEditor, _defaultFeelEditor = kit._defaultFeelEditor ?? _defaultFeelEditor };
        foreach (var pair in _kinds) merged._kinds[pair.Key] = pair.Value;
        foreach (var pair in _keys) merged._keys[pair.Key] = pair.Value;
        foreach (var pair in kit._defaultKeys) merged._defaultKeys[pair.Key] = pair.Value;
        foreach (var pair in _defaultKeys) merged._defaultKeys.TryAdd(pair.Key, pair.Value);
        merged._predicates.AddRange(_predicates);
        foreach (var pair in kit._defaultKinds) merged._defaultKinds[pair.Key] = pair.Value;
        foreach (var pair in _defaultKinds) merged._defaultKinds.TryAdd(pair.Key, pair.Value);
        return merged;
    }

    /// <summary>Resolves key, then predicates, then FEEL, then kind, then a kit default, then the read-only view.</summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public Type Resolve(BpmnPropertyEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.EditorKey is { } key && _keys.TryGetValue(key, out var keyed)) return keyed.Type;
        for (var index = _predicates.Count - 1; index >= 0; index--)
        {
            var registration = _predicates[index];
            if (registration.Predicate(entry)) return registration.Component.Type;
        }
        if (entry.Feel is BpmnFeelMode.Required or BpmnFeelMode.Optional && (_feelEditor ?? _defaultFeelEditor) is { } feel) return feel.Type;
        if (_kinds.TryGetValue(entry.Kind, out var component)) return component.Type;
        if (entry.EditorKey is { } defaultKey && _defaultKeys.TryGetValue(defaultKey, out var keyedDefault)) return keyedDefault.Type;
        return _defaultKinds.GetValueOrDefault(entry.Kind)?.Type ?? typeof(BpmnReadOnlyEntry);
    }
}
