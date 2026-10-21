using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn;

/// <summary>Builds snapshot groups from registered extension schemas.</summary>
public interface IBpmnExtensionGroupBuilder
{
    /// <summary>Builds groups and identifies extension roots consumed by the schema adapter.</summary>
    public BpmnExtensionGroupBuildResult Build(BpmnElementSnapshot snapshot, object element, BpmnElementExtras extras,
        BpmnExtensionRegistry registry, IBpmnPropertyLabelProvider labels);
}

/// <summary>Groups produced by one schema adapter and roots it claims from the raw fallback.</summary>
public sealed record BpmnExtensionGroupBuildResult(IReadOnlyList<BpmnPropertyGroup> Groups,
    IReadOnlyList<(string Namespace, string LocalName)> ClaimedRoots);

/// <summary>Localizes schema group and field labels; implementations can supply host translations.</summary>
public interface IBpmnPropertyLabelProvider
{
    /// <summary>Returns the translated label, or the supplied English fallback.</summary>
    public string GetLabel(string key, string englishFallback);
}

/// <summary>Default English labels for schema-driven property groups.</summary>
public sealed class BpmnEnglishPropertyLabelProvider : IBpmnPropertyLabelProvider
{
    /// <inheritdoc />
    public string GetLabel(string key, string englishFallback) => englishFallback;
}

/// <summary>Renders schema-computed groups in snapshot order between General and raw extensions.</summary>
public sealed partial class BpmnSchemaPropertiesProvider : IBpmnPropertiesProvider, IBpmnExtensionGroupBuilder
{
    private const string ZeebeUri = "http://camunda.org/schema/zeebe/1.0";
    /// <inheritdoc />
    public int Priority => 500;

    /// <inheritdoc />
    public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups) =>
        element.ExtensionGroups.Count == 0 ? groups : [.. groups, .. element.ExtensionGroups.Where(group => !group.Id.StartsWith("template:", StringComparison.Ordinal))];

    /// <inheritdoc />
    public BpmnExtensionGroupBuildResult Build(BpmnElementSnapshot snapshot, object element, BpmnElementExtras extras,
        BpmnExtensionRegistry registry, IBpmnPropertyLabelProvider labels)
    {
        var groups = new List<BpmnPropertyGroup>();
        var claimed = new HashSet<(string Namespace, string LocalName)>();
        var wireType = snapshot.ElementType;
        var zeebe = registry.TryGetSchema(ZeebeUri);

        if (zeebe is not null)
        {
            BpmnExtensionType? zeebeType(string name) => zeebe.Types.FirstOrDefault(type => type.Name == name);
            bool Allows(string typeName) => registry.ExtensionElementTypesFor(wireType)
                .Any(type => type.NamespaceUri == ZeebeUri && type.Name == typeName);
            string XmlName(string typeName) => zeebeType(typeName)?.ElementName ?? char.ToLowerInvariant(typeName[0]) + typeName[1..];
            void Claim(string typeName) => claimed.Add((ZeebeUri, XmlName(typeName)));
            string Label(string key, string fallback) => labels.GetLabel(key, fallback);

            if (Allows("TaskDefinition") && TaskDefinitionApplies(element, wireType))
            {
                Claim("TaskDefinition");
                var path = ElementPath(ZeebeUri, XmlName("TaskDefinition"));
                groups.Add(Group("task-definition", Label("task-definition", "Task definition"), ZeebeUri,
                [
                    Scalar("type", Label("type", "Type"), BpmnPropertyEntryKind.Text, Value(extras, ZeebeUri, XmlName("TaskDefinition"), "type"), path + "@type", feel: BpmnFeelMode.Optional),
                    Scalar("retries", Label("retries", "Retries"), BpmnPropertyEntryKind.Text, Value(extras, ZeebeUri, XmlName("TaskDefinition"), "retries"), path + "@retries", feel: BpmnFeelMode.Optional),
                ]));
            }

            if (Allows("IoMapping"))
            {
                Claim("IoMapping");
                var container = ElementPath(ZeebeUri, XmlName("IoMapping"));
                if (Allows("Input"))
                    groups.Add(Group("inputs", Label("inputs", "Inputs"), ZeebeUri,
                        [Collection("inputs", Label("inputs", "Inputs"), container, ZeebeUri, XmlName("Input"),
                            [new("target", Label("target", "Target")), new("source", Label("source", "Source")) { Feel = BpmnFeelMode.Optional, ExpressionLanguage = BpmnExpressionLanguage.Feel }], extras)]));
                if (Allows("Output"))
                    groups.Add(Group("outputs", Label("outputs", "Outputs"), ZeebeUri,
                        [Collection("outputs", Label("outputs", "Outputs"), container, ZeebeUri, XmlName("Output"),
                            [new("target", Label("target", "Target")), new("source", Label("source", "Source")) { Feel = BpmnFeelMode.Optional, ExpressionLanguage = BpmnExpressionLanguage.Feel }], extras)]));
            }

            if (Allows("TaskHeaders") && (wireType == "userTask" || TaskDefinitionApplies(element, wireType)))
            {
                Claim("TaskHeaders");
                var container = ElementPath(ZeebeUri, XmlName("TaskHeaders"));
                groups.Add(Group("headers", Label("headers", "Headers"), ZeebeUri,
                    [Collection("headers", Label("headers", "Headers"), container, ZeebeUri, XmlName("Header"),
                        [new("key", Label("key", "Key")), new("value", Label("value", "Value"))], extras)]));
            }

            if (wireType == "callActivity" && Allows("CalledElement"))
            {
                Claim("CalledElement");
                var typeName = XmlName("CalledElement");
                var path = ElementPath(ZeebeUri, typeName);
                groups.Add(Group("called-element", Label("called-element", "Called element"), ZeebeUri,
                [
                    Scalar("processId", Label("process-id", "Process ID"), BpmnPropertyEntryKind.Text, Value(extras, ZeebeUri, typeName, "processId"), path + "@processId", feel: BpmnFeelMode.Optional),
                    Scalar("propagateAllChildVariables", Label("propagate-child", "Propagate all child variables"), BpmnPropertyEntryKind.Checkbox,
                        Value(extras, ZeebeUri, typeName, "propagateAllChildVariables") ?? "false", path + "@propagateAllChildVariables"),
                    Scalar("propagateAllParentVariables", Label("propagate-parent", "Propagate all parent variables"), BpmnPropertyEntryKind.Checkbox,
                        Value(extras, ZeebeUri, typeName, "propagateAllParentVariables") ?? "true", path + "@propagateAllParentVariables"),
                ]));
            }

            if (wireType is "process" or "serviceTask" or "sendTask" or "scriptTask" or "businessRuleTask" or "userTask")
            {
                Claim("Properties");
                var typeName = XmlName("Properties");
                groups.Add(Group("properties", Label("properties", "Extension properties"), ZeebeUri,
                    [Collection("properties", Label("properties", "Extension properties"), ElementPath(ZeebeUri, typeName), ZeebeUri,
                        XmlName("Property"), [new("name", Label("name", "Name")), new("value", Label("value", "Value"))], extras)]));
            }

            if (element is Hasseware.AspNetCore.Components.Bpmn.Model.BpmnFlowNode { MultiInstanceLoop: { } loop })
            {
                var typeName = XmlName("LoopCharacteristics");
                var path = ElementPath(ZeebeUri, typeName);
                groups.Add(Group("multi-instance", Label("multi-instance", "Multi-instance"), ZeebeUri,
                [
                    Scalar("inputCollection", "Input collection", BpmnPropertyEntryKind.Text, Value(loop.Extras, ZeebeUri, typeName, "inputCollection"), path + "@inputCollection", "extension-loop", BpmnFeelMode.Optional),
                    Scalar("inputElement", "Input element", BpmnPropertyEntryKind.Text, Value(loop.Extras, ZeebeUri, typeName, "inputElement"), path + "@inputElement", "extension-loop"),
                    Scalar("outputCollection", "Output collection", BpmnPropertyEntryKind.Text, Value(loop.Extras, ZeebeUri, typeName, "outputCollection"), path + "@outputCollection", "extension-loop"),
                    Scalar("outputElement", "Output element", BpmnPropertyEntryKind.Text, Value(loop.Extras, ZeebeUri, typeName, "outputElement"), path + "@outputElement", "extension-loop", BpmnFeelMode.Optional),
                ]));
            }
        }

        BuildCamunda7(snapshot, element, extras, registry, labels, groups, claimed);

        foreach (var root in extras.Extensions)
        {
            if (claimed.Contains((root.Namespace, root.LocalName))
                || !registry.TryResolveElement(root.Namespace, root.LocalName, out var type, out var schema)) continue;
            var attrs = registry.GetAllProperties(type).Where(property => property.IsAttr).ToArray();
            var entries = new List<BpmnPropertyEntry>();
            foreach (var property in attrs)
            {
                if (!root.Attributes.TryGetValue(property.Name, out var value)) continue;
                var kind = property.Type switch
                {
                    "Boolean" => BpmnPropertyEntryKind.Checkbox,
                    "Integer" or "Real" or "Float" or "Double" => BpmnPropertyEntryKind.Number,
                    _ => BpmnPropertyEntryKind.Text,
                };
                var path = ElementPath(root.Namespace, root.LocalName) + "@" + property.Name;
                entries.Add(Scalar(property.Name, labels.GetLabel(property.QualifiedName, Humanise(property.Name)), kind,
                    value, path, feel: root.Namespace == ZeebeUri ? ZeebeFeel(property.Name) : BpmnFeelMode.None) with
                {
                    ExpressionLanguage = root.Namespace == Camunda7TemplateBindingHandler.Uri && property.Name is "expression" or "delegateExpression"
                        ? BpmnExpressionLanguage.Juel : root.Namespace == ZeebeUri && ZeebeFeel(property.Name) != BpmnFeelMode.None
                        ? BpmnExpressionLanguage.Feel : BpmnExpressionLanguage.None,
                });
            }
            if (entries.Count == 0) continue;
            claimed.Add((root.Namespace, root.LocalName));
            groups.Add(Group($"schema:{schema.Prefix}:{type.Name}", labels.GetLabel(type.QualifiedName, Humanise(type.Name)), schema.Uri, entries));
        }

        return new BpmnExtensionGroupBuildResult(
            groups.Select(group => group with { HasBuiltInLabels = labels is BpmnEnglishPropertyLabelProvider }).ToArray(),
            claimed.ToArray());
    }

    private static BpmnFeelMode ZeebeFeel(string name) => name switch
    {
        "expression" or "correlationKey" or "activeElementsCollection" => BpmnFeelMode.Required,
        "assignee" or "candidateGroups" or "candidateUsers" or "priority" or "dueDate" or "followUpDate"
            or "decisionId" or "formKey" or "formId" or "externalReference" or "businessId" or "processIdExpression"
            or "inputCollection" or "outputElement" or "completionCondition" => BpmnFeelMode.Optional,
        _ => BpmnFeelMode.None,
    };

    private static bool TaskDefinitionApplies(object element, string type) => type is "serviceTask" or "sendTask" or "scriptTask" or "businessRuleTask"
        || type is "endEvent" or "intermediateThrowEvent" && element is Hasseware.AspNetCore.Components.Bpmn.Model.BpmnFlowNode node
            && (node.EventDefinition == Hasseware.AspNetCore.Components.Bpmn.Model.BpmnEventDefinitionType.Message
                || node.EventDefinitions.Any(definition => definition.Type == Hasseware.AspNetCore.Components.Bpmn.Model.BpmnEventDefinitionType.Message));

    private static string? Value(BpmnElementExtras extras, string uri, string localName, string attribute) =>
        extras.Extensions.FirstOrDefault(root => root.Namespace == uri && root.LocalName == localName)?.Attributes.GetValueOrDefault(attribute);

    private static string ElementPath(string uri, string localName) => $"{{{uri}}}{localName}";

    private static BpmnPropertyGroup Group(string id, string label, string ns, IReadOnlyList<BpmnPropertyEntry> entries) =>
        new(id, label, entries.Select(entry => entry with { GroupId = id }).ToArray()) { Namespace = ns };

    private static BpmnPropertyEntry Scalar(string id, string label, BpmnPropertyEntryKind kind, string? value,
        string path, string bindingType = "extension", BpmnFeelMode feel = BpmnFeelMode.None) =>
        new(id, label, kind, value)
        {
            Feel = feel,
            ExpressionLanguage = feel is BpmnFeelMode.Optional or BpmnFeelMode.Required ? BpmnExpressionLanguage.Feel : BpmnExpressionLanguage.None,
            Binding = new(bindingType, new Dictionary<string, string>(StringComparer.Ordinal) { ["path"] = path }),
        };

    private static BpmnPropertyEntry Collection(string id, string label, string container, string uri, string item,
        IReadOnlyList<BpmnPropertyColumn> columns, BpmnElementExtras extras)
    {
        var rows = BpmnExtensionNavigator.ReadCollection(extras, BpmnExtensionPath.Parse(container), uri, item)
            .Select((child, index) =>
            {
                var row = columns.ToDictionary(column => column.Name,
                    column => child.Attributes.GetValueOrDefault(column.Name), StringComparer.Ordinal);
                row[BpmnExtensionNavigator.CollectionRowIndexKey] = index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                return (IReadOnlyDictionary<string, string?>)row;
            }).ToArray();
        return new BpmnPropertyEntry(id, label, BpmnPropertyEntryKind.List)
        {
            Columns = columns,
            Rows = rows,
            Binding = new("extension-collection", new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["container"] = container,
                ["itemNamespace"] = uri,
                ["itemLocalName"] = item,
            }),
        };
    }

    private static string Humanise(string value) => string.Concat(value.Select((character, index) =>
        index > 0 && char.IsUpper(character) ? " " + character : character.ToString()));
}
