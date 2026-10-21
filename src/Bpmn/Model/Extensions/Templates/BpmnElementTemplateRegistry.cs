using System.Globalization;
using System.Text.RegularExpressions;

namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

/// <summary>Minimal semantic-version range matching for template engine compatibility.</summary>
public static class BpmnSemverRange
{
    /// <summary>Returns whether a semantic version satisfies a range expression.</summary>
    public static bool Satisfies(string range, string version)
    {
        if (!SemVersion.TryParse(version, out var candidate)) return false;
        foreach (var alternative in range.Split("||", StringSplitOptions.TrimEntries))
        {
            if (alternative.Length == 0) continue;
            var tokens = Regex.Split(alternative, @"\s+").Where(t => t.Length > 0).ToList();
            var checks = new List<Func<SemVersion, bool>>();
            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                if (token is ">=" or "<=" or ">" or "<" or "=" or "^" or "~")
                {
                    if (++i >= tokens.Count) return false;
                    token += tokens[i];
                }
                if (!AddChecks(token, checks)) return false;
            }
            if (checks.All(check => check(candidate))) return true;
        }
        return false;
    }

    private static bool AddChecks(string token, List<Func<SemVersion, bool>> checks)
    {
        var op = token.StartsWith(">=", StringComparison.Ordinal) || token.StartsWith("<=", StringComparison.Ordinal) ? token[..2] :
            token.Length > 0 && "><=^~".Contains(token[0]) ? token[..1] : "=";
        var value = token[(op == "=" && !token.StartsWith('=') ? 0 : op.Length)..];
        if (op == "=" && token.StartsWith('=')) value = token[1..];
        if (value is "*" or "x" or "X") return true;
        if (op is "^" or "~")
        {
            if (!SemVersion.TryParse(value, out var lower)) return false;
            var upper = op == "~" ? new SemVersion(lower.Major, lower.Minor + 1, 0, null) : lower.Major > 0
                ? new SemVersion(lower.Major + 1, 0, 0, null) : lower.Minor > 0
                    ? new SemVersion(0, lower.Minor + 1, 0, null) : new SemVersion(0, 0, lower.Patch + 1, null);
            checks.Add(v => v.CompareTo(lower) >= 0);
            checks.Add(v => v.CompareTo(upper) < 0);
            return true;
        }
        var parts = value.Split('.');
        var wildcardIndex = Array.FindIndex(parts, p => p is "*" or "x" or "X");
        if (wildcardIndex >= 0 || (op == "=" && !token.StartsWith('=') && parts.Length < 3))
        {
            if (op != "=") return false;
            var prefixParts = wildcardIndex >= 0 ? parts.Take(wildcardIndex).ToArray() : parts;
            if (prefixParts.Length == 0) return true;
            if (!SemVersion.TryParse(string.Join('.', prefixParts), out var prefix)) return false;
            checks.Add(v => v.Major == prefix.Major && (prefixParts.Length < 2 || v.Minor == prefix.Minor));
            return true;
        }
        if (!SemVersion.TryParse(value, out var target)) return false;
        checks.Add(op switch
        {
            ">=" => v => v.CompareTo(target) >= 0,
            "<=" => v => v.CompareTo(target) <= 0,
            ">" => v => v.CompareTo(target) > 0,
            "<" => v => v.CompareTo(target) < 0,
            _ => v => v.CompareTo(target) == 0
        });
        return true;
    }

    private readonly record struct SemVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemVersion>
    {
        public static bool TryParse(string value, out SemVersion version)
        {
            version = default;
            var text = value.Trim();
            if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
            var build = text.IndexOf('+');
            if (build >= 0)
            {
                var metadata = text[(build + 1)..];
                if (!ValidIdentifiers(metadata, allowLeadingZeroes: true)) return false;
                text = text[..build];
            }
            var dash = text.IndexOf('-');
            var pre = dash >= 0 ? text[(dash + 1)..] : null;
            if (pre is not null && !ValidIdentifiers(pre, allowLeadingZeroes: false)) return false;
            if (dash >= 0) text = text[..dash];
            var parts = text.Split('.');
            if (parts.Length is < 1 or > 3 || parts.Any(p => p.Length > 1 && p[0] == '0') ||
                !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)) return false;
            var minor = 0;
            var patch = 0;
            if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out minor)) return false;
            if (parts.Length > 2 && !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out patch)) return false;
            version = new(major, minor, patch, pre);
            return true;
        }

        private static bool ValidIdentifiers(string value, bool allowLeadingZeroes)
        {
            if (value.Length == 0) return false;
            foreach (var identifier in value.Split('.'))
            {
                if (identifier.Length == 0 || identifier.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) return false;
                if (!allowLeadingZeroes && identifier.Length > 1 && identifier.All(char.IsAsciiDigit) && identifier[0] == '0') return false;
            }
            return true;
        }

        public int CompareTo(SemVersion other)
        {
            var core = Major.CompareTo(other.Major);
            if (core == 0) core = Minor.CompareTo(other.Minor);
            if (core == 0) core = Patch.CompareTo(other.Patch);
            if (core != 0) return core;
            if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
            if (other.PreRelease is null) return -1;
            var left = PreRelease.Split('.');
            var right = other.PreRelease.Split('.');
            for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
            {
                var leftNumeric = left[i].All(char.IsAsciiDigit);
                var rightNumeric = right[i].All(char.IsAsciiDigit);
                int comparison;
                if (leftNumeric && rightNumeric)
                {
                    comparison = left[i].Length.CompareTo(right[i].Length);
                    if (comparison == 0) comparison = string.CompareOrdinal(left[i], right[i]);
                }
                else if (leftNumeric != rightNumeric)
                    comparison = leftNumeric ? -1 : 1;
                else
                    comparison = string.CompareOrdinal(left[i], right[i]);
                if (comparison != 0) return comparison;
            }
            return left.Length.CompareTo(right.Length);
        }
    }
}

/// <summary>Outcome from a registry add attempt.</summary>
public sealed record BpmnTemplateRegistryAddResult(bool Added, BpmnTemplateLoadError? Error = null);

/// <summary>Mutable in-memory index of validated element templates.</summary>
public sealed class BpmnElementTemplateRegistry
{
    private readonly Dictionary<string, List<BpmnElementTemplate>> _templates = new(StringComparer.Ordinal);
    private readonly Lock _gate = new();

    /// <summary>Adds a template, rejecting an existing id/version pair.</summary>
    public BpmnTemplateRegistryAddResult Add(BpmnElementTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        lock (_gate)
        {
            if (!_templates.TryGetValue(template.Id, out var versions)) _templates[template.Id] = versions = [];
            if (versions.Any(existing => existing.Version == template.Version))
                return new(false, new("EXT-004", $"Template '{template.Id}' version '{template.Version?.ToString(CultureInfo.InvariantCulture) ?? "(unversioned)"}' already exists.", TemplateId: template.Id));
            versions.Add(template);
            versions.Sort((a, b) => Nullable.Compare(a.Version, b.Version));
            return new(true);
        }
    }

    /// <summary>Adds templates independently and returns each add result.</summary>
    public IReadOnlyList<BpmnTemplateRegistryAddResult> AddRange(IEnumerable<BpmnElementTemplate> templates)
    {
        ArgumentNullException.ThrowIfNull(templates);
        return templates.Select(Add).ToArray();
    }

    /// <summary>Looks up an id; null version selects the highest registered version.</summary>
    public bool TryGet(string id, int? version, out BpmnElementTemplate template)
    {
        lock (_gate)
        {
            if (_templates.TryGetValue(id, out var versions))
            {
                var match = version is null ? versions.LastOrDefault() : versions.FirstOrDefault(t => t.Version == version);
                if (match is not null) { template = match; return true; }
            }
        }
        template = null!;
        return false;
    }

    /// <summary>Looks up a template, or returns null when absent.</summary>
    public BpmnElementTemplate? TryGet(string id, int? version = null) => TryGet(id, version, out var template) ? template : null;
    /// <summary>Looks up an exact id/version identity; null version matches only an unversioned template.</summary>
    public bool TryGetExact(string id, int? version, out BpmnElementTemplate template)
    {
        lock (_gate)
        {
            if (_templates.TryGetValue(id, out var versions) && versions.FirstOrDefault(t => t.Version == version) is { } match)
            {
                template = match;
                return true;
            }
        }
        template = null!;
        return false;
    }

    /// <summary>Looks up an exact template identity, or returns null when absent.</summary>
    public BpmnElementTemplate? TryGetExact(string id, int? version) => TryGetExact(id, version, out var template) ? template : null;

    /// <summary>Gets registered versions in ascending order.</summary>
    public IReadOnlyList<int?> Versions(string id)
    {
        lock (_gate) return _templates.TryGetValue(id, out var versions) ? versions.Select(t => t.Version).ToArray() : [];
    }
    /// <summary>Gets latest template for an id, or null when absent.</summary>
    public BpmnElementTemplate? GetLatest(string id) => TryGet(id);

    /// <summary>Returns the latest nondeprecated version per id compatible with supplied engine versions.</summary>
    public IReadOnlyList<BpmnElementTemplate> LatestCompatible(IReadOnlyDictionary<string, string>? engines = null)
    {
        lock (_gate)
            return _templates.Values.Select(versions => versions.LastOrDefault(template =>
                template.Deprecated is null && EnginesMatch(template.Engines, engines)))
                .OfType<BpmnElementTemplate>().ToArray();
    }

    /// <summary>Returns applicable templates, filtering deprecation and supplied engine versions.</summary>
    public IReadOnlyList<BpmnElementTemplate> ApplicableTo(string bpmnType, string? eventDefinitionType = null,
        IReadOnlyDictionary<string, string>? engines = null, bool includeDeprecated = false,
        Func<string, string, bool>? isA = null)
    {
        isA ??= IsA;
        lock (_gate)
        {
            return _templates.Values.Select(versions => versions.LastOrDefault(template =>
                (includeDeprecated || template.Deprecated is null) &&
                template.AppliesTo.Any(applies => isA(bpmnType, applies)) &&
                (template.ElementType?.EventDefinition is null || template.ElementType.EventDefinition == eventDefinitionType) &&
                EnginesMatch(template.Engines, engines))).OfType<BpmnElementTemplate>().ToArray();
        }
    }

    private static bool EnginesMatch(IReadOnlyDictionary<string, string> requirements, IReadOnlyDictionary<string, string>? versions)
    {
        if (versions is null) return true;
        foreach (var (engine, range) in requirements)
            if (versions.TryGetValue(engine, out var version) && !BpmnSemverRange.Satisfies(range, version)) return false;
        return true;
    }

    private static bool IsA(string candidate, string expected)
    {
        if (candidate == expected || expected == "bpmn:BaseElement") return true;
        var parents = candidate switch
        {
            "bpmn:Task" => ["bpmn:Activity", "bpmn:FlowNode"],
            "bpmn:ServiceTask" or "bpmn:UserTask" or "bpmn:ScriptTask" or "bpmn:BusinessRuleTask" or "bpmn:SendTask" or "bpmn:ReceiveTask" => ["bpmn:Task", "bpmn:Activity", "bpmn:FlowNode"],
            "bpmn:CallActivity" or "bpmn:SubProcess" or "bpmn:Transaction" or "bpmn:AdHocSubProcess" => ["bpmn:Activity", "bpmn:FlowNode"],
            "bpmn:StartEvent" or "bpmn:EndEvent" or "bpmn:IntermediateCatchEvent" or "bpmn:IntermediateThrowEvent" or "bpmn:BoundaryEvent" => ["bpmn:Event", "bpmn:FlowNode"],
            "bpmn:Event" => ["bpmn:FlowNode"],
            "bpmn:SequenceFlow" => ["bpmn:FlowElement"],
            "bpmn:FlowNode" => ["bpmn:FlowElement"],
            "bpmn:Activity" => ["bpmn:FlowNode"],
            "bpmn:FlowElement" => ["bpmn:BaseElement"],
            "bpmn:Process" => ["bpmn:CallableElement"],
            _ => Array.Empty<string>()
        };
        return parents.Contains(expected, StringComparer.Ordinal) || parents.Any(parent => parent != candidate && IsA(parent, expected));
    }
}


