namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

/// <summary>Comparison operator for a property condition.</summary>
public enum BpmnTemplateConditionOperator
{
    /// <summary>Compare a value for equality.</summary>
    Equals,
    /// <summary>Check membership in an allowed value set.</summary>
    OneOf,
    /// <summary>Check referenced property activation.</summary>
    IsActive,
    /// <summary>Check whether referenced value is empty.</summary>
    IsEmpty
}

/// <summary>Base condition for conditional template properties.</summary>
public abstract record BpmnTemplateCondition;

/// <summary>Condition evaluated against one property id.</summary>
public sealed record PropertyCondition(
    string Property,
    BpmnTemplateConditionOperator Operator,
    string? Value = null,
    IReadOnlyList<string>? Values = null,
    bool BooleanValue = true) : BpmnTemplateCondition;

/// <summary>Condition requiring every child condition to match.</summary>
public sealed record AllMatchCondition(IReadOnlyList<PropertyCondition> Conditions) : BpmnTemplateCondition;

/// <summary>Evaluates template property activation rules.</summary>
public static class BpmnTemplateConditionEvaluator
{
    /// <summary>Returns ids of active properties using supplied current values.</summary>
    public static IReadOnlySet<string> ActiveProperties(BpmnElementTemplate template, Func<BpmnTemplateProperty, string?> currentValue)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(currentValue);
        var properties = template.Properties.Where(p => p.Id is not null).ToDictionary(p => p.Id!, StringComparer.Ordinal);
        var cyclic = FindIsActiveCycles(properties);
        var active = new HashSet<string>(properties.Keys.Except(cyclic), StringComparer.Ordinal);
        var limit = Math.Max(1, properties.Count * properties.Count + 1);
        for (var iteration = 0; iteration < limit; iteration++)
        {
            var next = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (id, property) in properties)
                if (!cyclic.Contains(id) && (property.Condition is null || Evaluate(property.Condition, active, properties, currentValue)))
                    next.Add(id);
            if (next.SetEquals(active)) return active;
            active = next;
        }
        return active;
    }

    private static HashSet<string> FindIsActiveCycles(Dictionary<string, BpmnTemplateProperty> properties)
    {
        var state = new Dictionary<string, int>(StringComparer.Ordinal);
        var path = new List<string>();
        var cyclic = new HashSet<string>(StringComparer.Ordinal);
        void Visit(string id)
        {
            if (state.TryGetValue(id, out var currentState))
            {
                if (currentState == 1)
                {
                    var start = path.LastIndexOf(id);
                    for (var i = start; i < path.Count; i++) cyclic.Add(path[i]);
                }
                return;
            }
            state[id] = 1;
            path.Add(id);
            if (properties[id].Condition is { } condition)
                foreach (var dependency in Conditions(condition).Where(c => c.Operator == BpmnTemplateConditionOperator.IsActive).Select(c => c.Property))
                    if (properties.ContainsKey(dependency)) Visit(dependency);
            path.RemoveAt(path.Count - 1);
            state[id] = 2;
        }
        foreach (var id in properties.Keys) Visit(id);
        return cyclic;
    }

    private static IEnumerable<PropertyCondition> Conditions(BpmnTemplateCondition condition) => condition switch
    {
        PropertyCondition single => [single],
        AllMatchCondition all => all.Conditions,
        _ => []
    };

    private static bool Evaluate(BpmnTemplateCondition condition, HashSet<string> active,
        Dictionary<string, BpmnTemplateProperty> properties, Func<BpmnTemplateProperty, string?> currentValue) => condition switch
        {
            AllMatchCondition all => all.Conditions.All(c => EvaluatePropertyCondition(c, active, properties, currentValue)),
            PropertyCondition single => EvaluatePropertyCondition(single, active, properties, currentValue),
            _ => false
        };

    private static bool EvaluatePropertyCondition(PropertyCondition condition, HashSet<string> active,
        Dictionary<string, BpmnTemplateProperty> properties, Func<BpmnTemplateProperty, string?> currentValue)
    {
        if (!properties.TryGetValue(condition.Property, out var referenced)) return false;
        var value = currentValue(referenced);
        return condition.Operator switch
        {
            BpmnTemplateConditionOperator.Equals => string.Equals(value, condition.Value, StringComparison.Ordinal),
            BpmnTemplateConditionOperator.OneOf => condition.Values?.Contains(value ?? string.Empty, StringComparer.Ordinal) == true,
            BpmnTemplateConditionOperator.IsActive => active.Contains(condition.Property) == condition.BooleanValue,
            BpmnTemplateConditionOperator.IsEmpty => string.IsNullOrEmpty(value) == condition.BooleanValue,
            _ => false
        };
    }
}


