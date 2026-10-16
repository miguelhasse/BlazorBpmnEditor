using System.Diagnostics.CodeAnalysis;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

/// <summary>Thread-safe namespace registry for opt-in moddle descriptors.</summary>
public sealed class BpmnExtensionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, BpmnExtensionSchema> _schemas = new(StringComparer.Ordinal);

    /// <summary>Registered packages in registration order, as a read-only snapshot.</summary>
    public IReadOnlyList<BpmnExtensionSchema> Schemas
    {
        get { lock (_gate) return Array.AsReadOnly(_schemas.Values.ToArray()); }
    }

    /// <summary>Registers a package snapshot. Identical registrations are idempotent; URI/prefix conflicts fail.</summary>
    public BpmnExtensionRegistry Register(BpmnExtensionSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        if (string.IsNullOrWhiteSpace(schema.Uri) || string.IsNullOrWhiteSpace(schema.Prefix))
            throw new BpmnExtensionSchemaException("Schema URI and prefix must be non-empty.");
        var snapshot = Snapshot(schema);
        lock (_gate)
        {
            if (_schemas.TryGetValue(schema.Uri, out var existing))
            {
                if (!SameContent(existing, snapshot)) throw new BpmnExtensionSchemaException($"Conflicting extension schema for namespace '{schema.Uri}'.");
                return this;
            }
            if (_schemas.Values.Any(s => s.Prefix == schema.Prefix))
                throw new BpmnExtensionSchemaException($"Extension prefix '{schema.Prefix}' is already registered for another namespace.");
            if (snapshot.Types.Select(t => t.Name).Distinct(StringComparer.Ordinal).Count() != snapshot.Types.Count)
                throw new BpmnExtensionSchemaException("Schema contains duplicate type names.");
            _schemas.Add(snapshot.Uri, snapshot);
        }
        return this;
    }

    /// <summary>Finds a package by namespace URI.</summary>
    public BpmnExtensionSchema? TryGetSchema(string uri) { lock (_gate) return _schemas.GetValueOrDefault(uri); }

    /// <summary>Finds a package by namespace URI.</summary>
    public bool TryGetSchema(string uri, [NotNullWhen(true)] out BpmnExtensionSchema? schema) => (schema = TryGetSchema(uri)) is not null;

    /// <summary>Finds a package by preferred prefix.</summary>
    public BpmnExtensionSchema? TryGetSchemaByPrefix(string prefix) { lock (_gate) return _schemas.Values.FirstOrDefault(s => s.Prefix == prefix); }

    /// <summary>Resolves either prefix:Type or {namespaceURI}Type.</summary>
    public bool TryResolveType(string qualifiedOrExpandedName, [NotNullWhen(true)] out BpmnExtensionType? type, [NotNullWhen(true)] out BpmnExtensionSchema? schema)
    {
        lock (_gate)
        {
            type = null;
            schema = null;
            if (string.IsNullOrEmpty(qualifiedOrExpandedName)) return false;
            string local;
            if (qualifiedOrExpandedName[0] == '{')
            {
                var end = qualifiedOrExpandedName.IndexOf('}');
                if (end < 2) return false;
                schema = _schemas.GetValueOrDefault(qualifiedOrExpandedName[1..end]);
                local = qualifiedOrExpandedName[(end + 1)..];
            }
            else
            {
                var colon = qualifiedOrExpandedName.IndexOf(':');
                if (colon <= 0) return false;
                schema = _schemas.Values.FirstOrDefault(s => s.Prefix == qualifiedOrExpandedName[..colon]);
                local = qualifiedOrExpandedName[(colon + 1)..];
            }
            type = schema?.Types.FirstOrDefault(t => t.Name == local);
            if (type is not null && schema is not null) return true;
            schema = null;
            return false;
        }
    }

    /// <summary>Resolves an XML type tag, or an unambiguous property-serialized tag.</summary>
    public BpmnExtensionType? TryResolveElement(string namespaceUri, string localName) =>
        TryResolveElement(namespaceUri, localName, out var type, out _) ? type : null;

    /// <summary>Resolves an XML type tag, respecting package tagAlias and property serialization hints.</summary>
    public bool TryResolveElement(string namespaceUri, string localName, [NotNullWhen(true)] out BpmnExtensionType? type, [NotNullWhen(true)] out BpmnExtensionSchema? schema)
    {
        lock (_gate)
        {
            schema = _schemas.GetValueOrDefault(namespaceUri);
            type = schema?.Types.FirstOrDefault(t => !t.IsAbstract && t.Extends.Count == 0 && t.ElementName == localName);
            if (type is not null && schema is not null) return true;
            if (schema is not null)
            {
                var owner = schema;
                var candidates = owner.Types.SelectMany(t => t.Properties)
                    .Where(p => !p.IsAttr && !p.IsBody && !p.IsReference && p.Serialize is "property" or "xsi:type" && LocalName(p.Name) == localName)
                    .Select(p => Resolve(p.Type, owner)).Where(t => t is not null).Distinct().ToArray();
                if (candidates.Length == 1 && candidates[0] is { } candidate) { type = candidate; return true; }
            }
            schema = null;
            return false;
        }
    }

    /// <summary>Inherited properties followed by declared properties and applicable extension traits; overrides keep their position.</summary>
    public IReadOnlyList<BpmnExtensionProperty> GetAllProperties(BpmnExtensionType type)
    {
        ArgumentNullException.ThrowIfNull(type);
        lock (_gate)
        {
            var schema = SchemaFor(type);
            var result = new List<BpmnExtensionProperty>();
            var path = new HashSet<string>(StringComparer.Ordinal);
            var visited = new HashSet<string>(StringComparer.Ordinal);
            void Collect(BpmnExtensionType current, BpmnExtensionSchema owner)
            {
                if (!path.Add(current.QualifiedName)) throw new BpmnExtensionSchemaException($"Cyclic superclass chain at '{current.QualifiedName}'.");
                if (visited.Add(current.QualifiedName))
                {
                    foreach (var parent in current.SuperClass)
                        if (TryResolveType(Qualify(parent, owner), out var baseType, out var baseSchema)) Collect(baseType, baseSchema);
                    foreach (var property in current.Properties)
                    {
                        var index = result.FindIndex(p => p.Name == property.Name);
                        if (index >= 0) result[index] = property; else result.Add(property);
                    }
                }
                path.Remove(current.QualifiedName);
            }
            if (schema is null) return Array.AsReadOnly(type.Properties.ToArray());
            var registered = Resolve(type.Name, schema) ?? type;
            Collect(registered, schema);
            foreach (var owner in _schemas.Values)
                foreach (var trait in owner.Types.Where(t => t.Extends.Count > 0))
                    if (trait.Extends.Any(target => IsExtensionA(registered, schema, Qualify(target, owner), new HashSet<string>(StringComparer.Ordinal)))) Collect(trait, owner);
            return result.AsReadOnly();
        }
    }

    /// <summary>Concrete Element subtypes allowed in extensionElements; meta.allowedIn takes precedence when supplied.</summary>
    public IReadOnlyList<BpmnExtensionType> ExtensionElementTypesFor(string bpmnType)
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_schemas.Values.SelectMany(s => s.Types.Where(t => !t.IsAbstract && t.Extends.Count == 0 &&
                (t.AllowedIn.Count > 0
                    ? t.AllowedIn.Any(target => Applicable(bpmnType, Qualify(target, s), new HashSet<string>(StringComparer.Ordinal)))
                    : IsExtensionA(t, s, "Element", new HashSet<string>(StringComparer.Ordinal))))).ToArray());
        }
    }

    /// <summary>Attribute properties contributed through applicable extends traits, including inherited traits.</summary>
    public IReadOnlyList<BpmnExtensionProperty> ForeignAttributesFor(string bpmnType)
    {
        lock (_gate)
        {
            return Array.AsReadOnly(_schemas.Values.SelectMany(s => s.Types.Where(t => t.Extends.Count > 0 &&
                Applicable(bpmnType, t.QualifiedName, new HashSet<string>(StringComparer.Ordinal)))
                .SelectMany(GetAllProperties)).Where(p => p.IsAttr).DistinctBy(p => p.QualifiedName).ToArray());
        }
    }

    /// <summary>Declared XML child order, excluding attributes and body properties.</summary>
    public IReadOnlyList<string> GetChildOrder(BpmnExtensionType parentType)
    {
        lock (_gate)
        {
            return Array.AsReadOnly(GetAllProperties(parentType).Where(p => !p.IsAttr && !p.IsBody).Select(p =>
            {
                var owner = _schemas.GetValueOrDefault(p.NamespaceUri) ?? SchemaFor(parentType);
                return p.Serialize is "property" or "xsi:type" || p.IsReference || owner is null
                    ? LocalName(p.Name) : Resolve(p.Type, owner)?.ElementName ?? LocalName(p.Name);
            }).Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    /// <summary>Compares child local names in schema order; unknown names sort after known names.</summary>
    public int CompareChildOrder(BpmnExtensionType parentType, string leftLocalName, string rightLocalName)
    {
        var order = GetChildOrder(parentType);
        int Rank(string name) { for (var i = 0; i < order.Count; i++) if (order[i] == name) return i; return int.MaxValue; }
        return Rank(leftLocalName).CompareTo(Rank(rightLocalName));
    }

    /// <summary>Whether the named property is declared as a reference (including inherited properties).</summary>
    public bool IsReference(BpmnExtensionType type, string propertyName) => GetAllProperties(type).Any(p => (p.Name == propertyName || p.QualifiedName == propertyName) && p.IsReference);

    /// <summary>Whether a property on a qualified or expanded type is a reference.</summary>
    public bool IsReference(string typeName, string propertyName) => TryResolveType(typeName, out var type, out _) && IsReference(type, propertyName);

    private bool Applicable(string bpmnType, string target, HashSet<string> visited)
    {
        if (target == "*" || BpmnTypeNames.QualifiedName(bpmnType) == target) return true;
        if (TryResolveType(bpmnType, out var concrete, out var concreteSchema) && IsExtensionA(concrete, concreteSchema, target, new HashSet<string>(StringComparer.Ordinal))) return true;
        if (target.StartsWith("bpmn:", StringComparison.Ordinal) || target == "Element") return BpmnTypeNames.IsA(bpmnType, target);
        if (!visited.Add(target) || !TryResolveType(target, out var type, out var schema)) return false;
        return type.Extends.Concat(type.SuperClass).Any(parent => Applicable(bpmnType, Qualify(parent, schema), visited));
    }
    private bool IsExtensionA(BpmnExtensionType type, BpmnExtensionSchema owner, string target, HashSet<string> visited)
    {
        if (type.QualifiedName == target) return true;
        if (!visited.Add(type.QualifiedName)) return false;
        return type.SuperClass.Any(parent =>
        {
            var name = Qualify(parent, owner);
            if (name == target) return true;
            if (name.StartsWith("bpmn:", StringComparison.Ordinal)) return BpmnTypeNames.IsA(name, target);
            return TryResolveType(name, out var baseType, out var baseSchema) && IsExtensionA(baseType, baseSchema, target, visited);
        });
    }
    private BpmnExtensionSchema? SchemaFor(BpmnExtensionType type) =>
        _schemas.GetValueOrDefault(type.NamespaceUri) ?? _schemas.Values.FirstOrDefault(s => s.Types.Any(t => ReferenceEquals(t, type) || t.QualifiedName == type.QualifiedName)) ??
        _schemas.Values.SingleOrDefault(s => s.Types.Any(t => t.Name == type.Name));
    private BpmnExtensionType? Resolve(string name, BpmnExtensionSchema owner) => TryResolveType(Qualify(name, owner), out var type, out _) ? type : null;
    private static string Qualify(string name, BpmnExtensionSchema owner) => name.Contains(':') || name is "Element" or "*" ? name : $"{owner.Prefix}:{name}";
    private static string LocalName(string name) => name[(name.IndexOf(':') + 1)..];
    private static BpmnExtensionSchema Snapshot(BpmnExtensionSchema schema) => schema with
    {
        Types = Array.AsReadOnly(schema.Types.Select(t => t with
        {
            NamespaceUri = schema.Uri,
            Prefix = schema.Prefix,
            ElementName = schema.TagAlias == "lowerCase" && t.ElementName == t.Name ? char.ToLowerInvariant(t.Name[0]) + t.Name[1..] : t.ElementName,
            SuperClass = Array.AsReadOnly(t.SuperClass.ToArray()),
            Extends = Array.AsReadOnly(t.Extends.ToArray()),
            AllowedIn = Array.AsReadOnly(t.AllowedIn.ToArray()),
            Properties = Array.AsReadOnly(t.Properties.Select(p => p with
            {
                NamespaceUri = schema.Uri,
                Prefix = schema.Prefix,
                DeclaringType = $"{schema.Prefix}:{t.Name}"
            }).ToArray())
        }).ToArray()),
        Enumerations = Array.AsReadOnly(schema.Enumerations.Select(e => e with { Values = Array.AsReadOnly(e.Values.ToArray()) }).ToArray())
    };
    private static bool SameContent(BpmnExtensionSchema a, BpmnExtensionSchema b) =>
        a.Name == b.Name && a.Prefix == b.Prefix && a.Version == b.Version && a.TagAlias == b.TagAlias && a.TypePrefix == b.TypePrefix &&
        a.Types.Count == b.Types.Count && a.Types.Zip(b.Types).All(pair =>
            pair.First.Name == pair.Second.Name && pair.First.IsAbstract == pair.Second.IsAbstract && pair.First.ElementName == pair.Second.ElementName &&
            pair.First.SuperClass.SequenceEqual(pair.Second.SuperClass) && pair.First.Extends.SequenceEqual(pair.Second.Extends) &&
            pair.First.AllowedIn.SequenceEqual(pair.Second.AllowedIn) && pair.First.Properties.SequenceEqual(pair.Second.Properties)) &&
        a.Enumerations.Count == b.Enumerations.Count && a.Enumerations.Zip(b.Enumerations).All(pair => pair.First.Name == pair.Second.Name && pair.First.Values.SequenceEqual(pair.Second.Values));
}
