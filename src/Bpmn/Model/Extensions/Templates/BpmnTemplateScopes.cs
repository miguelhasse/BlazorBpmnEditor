namespace Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

/// <summary>Scoped property command keys are scope:&lt;type&gt;[:&lt;id&gt;]/&lt;property-id or #index&gt;. Conditions within a scope resolve local ids first, then top-level ids.</summary>
public static class BpmnTemplateScopes
{
    /// <summary>Gets the stable prefix for scoped property keys.</summary>
    public static string Prefix(BpmnTemplateScope scope) => "scope:" + scope.Type + (scope.Id is null ? "" : ":" + scope.Id) + "/";

    internal static BpmnElementTemplate Flatten(BpmnElementTemplate template)
    {
        if (template.Scopes.Count == 0) return template;
        var properties = template.Properties.ToList();
        var groups = template.Groups.ToList();
        foreach (var scope in template.Scopes)
        {
            var prefix = Prefix(scope);
            var keys = scope.Properties.Select((p, i) => p.Id ?? "#" + i).ToHashSet(StringComparer.Ordinal);
            PropertyCondition Remap(PropertyCondition condition) => keys.Contains(condition.Property) ? condition with { Property = prefix + condition.Property } : condition;
            var groupId = prefix.TrimEnd('/');
            groups.Add(new(groupId, scope.Type == "camunda:Connector" ? "Connector" : "Error: " + scope.Id));
            foreach (var (property, index) in scope.Properties.Select((p, i) => (p, i)))
            {
                var parameters = property.Binding is null ? null : new Dictionary<string, string>(property.Binding.Parameters, StringComparer.Ordinal)
                { ["__scopeType"] = scope.Type, ["__scopeId"] = scope.Id ?? "" };
                properties.Add(property with
                {
                    Id = prefix + (property.Id ?? "#" + index),
                    Group = property.Group ?? groupId,
                    Binding = property.Binding is null ? null : property.Binding with { Parameters = parameters! },
                    Condition = property.Condition switch
                    {
                        PropertyCondition single => Remap(single),
                        AllMatchCondition all => new AllMatchCondition(all.Conditions.Select(Remap).ToArray()),
                        _ => property.Condition
                    }
                });
            }
        }
        return template with { Properties = properties, Groups = groups, Scopes = [] };
    }
}
