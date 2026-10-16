using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn;

public sealed partial class BpmnSchemaPropertiesProvider
{
    private static void BuildCamunda7(BpmnElementSnapshot snapshot, object element, BpmnElementExtras extras,
        BpmnExtensionRegistry registry, IBpmnPropertyLabelProvider labels, List<BpmnPropertyGroup> groups,
        HashSet<(string Namespace, string LocalName)> claimed)
    {
        const string uri = Camunda7TemplateBindingHandler.Uri;
        if (registry.TryGetSchema(uri) is null) return;
        var wireType = snapshot.ElementType;
        string Label(string name, string fallback) => labels.GetLabel($"camunda:{name}", fallback);
        string Path(string local) => ElementPath(uri, local);
        void Add(string id, string label, IReadOnlyList<BpmnPropertyEntry> entries) => groups.Add(Group($"camunda:{id}", Label(id, label), uri, entries));
        void Claim(string local) => claimed.Add((uri, local));
        BpmnPropertyEntry Attribute(string name, string fallback, bool boolean = false) => new("camunda:" + name, Label(name, fallback),
            boolean ? BpmnPropertyEntryKind.Checkbox : BpmnPropertyEntryKind.Text,
            extras.ForeignAttributes.GetValueOrDefault("{" + uri + "}" + name) ?? (boolean ? name == "exclusive" ? "true" : "false" : null))
        {
            ExpressionLanguage = !boolean && name is "delegateExpression" or "expression" or "topic" or "assignee" or "candidateUsers" or "candidateGroups" or "dueDate" or "followUpDate" or "priority" or "formKey" or "calledElementVersion" or "calledElementVersionTag" or "calledElementTenantId" ? BpmnExpressionLanguage.Juel : BpmnExpressionLanguage.None,
            Binding = new("foreign-attribute", new Dictionary<string, string> { ["namespace"] = uri, ["name"] = name }),
        };
        void Attributes(string id, string label, params (string Name, string Label)[] names) => Add(id, label, names.Select(n => Attribute(n.Name, n.Label)).ToArray());
        bool Has(string local) => extras.Extensions.Any(e => e.Namespace == uri && e.LocalName == local);
        BpmnPropertyColumn Column(string name, string fallback) => new(name, Label(name, fallback))
        { ExpressionLanguage = name is "#text" or "expression" or "delegateExpression" or "sourceExpression" or "businessKey" ? BpmnExpressionLanguage.Juel : BpmnExpressionLanguage.None };
        void List(string id, string label, string? container, string item, params BpmnPropertyColumn[] columns)
        {
            var children = container is null ? extras.Extensions : BpmnExtensionNavigator.Resolve(extras.Extensions, BpmnExtensionPath.Parse(container).Steps)?.Children ?? [];
            var items = children.Where(e => e.Namespace == uri && e.LocalName == item).ToArray();
            var parameters = new Dictionary<string, string> { ["itemLocalName"] = item };
            if (container is not null) parameters["container"] = container;
            var entries = new List<BpmnPropertyEntry>
            {
                new(id, Label(id, label), BpmnPropertyEntryKind.List)
                {
                    Columns = columns,
                    Rows = items.Select((node, index) => (node, index)).Where(x => x.node.Children.Count == 0 || item == "executionListener").Select(x =>
                    {
                        var row = columns.ToDictionary(c => c.Name,
                            c => c.Name == "#text" ? x.node.Text : x.node.Attributes.GetValueOrDefault(c.Name), StringComparer.Ordinal);
                        row[BpmnExtensionNavigator.CollectionRowIndexKey] = x.index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        return (IReadOnlyDictionary<string, string?>)row;
                    }).ToArray(),
                    Binding = new("camunda-collection", parameters)
                }
            };
            foreach (var (node, index) in items.Select((e, i) => (e, i)).Where(x => x.e.Children.Count > 0))
                entries.Add(new(id + ":structured:" + index, Label("structured", "Structured value (preserved)") + ": " + (node.Attributes.GetValueOrDefault("name") ?? (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    BpmnPropertyEntryKind.ReadOnly, JsonSerializer.Serialize(node))
                { IsReadOnly = true });
            Add(id, label, entries);
        }
        void Io(string? parent, string suffix = "")
        {
            var container = parent is null ? Path("inputOutput") : parent + "/" + Path("inputOutput");
            List("inputs" + suffix, "Inputs", container, "inputParameter", Column("name", "Name"), Column("#text", "Value"));
            List("outputs" + suffix, "Outputs", container, "outputParameter", Column("name", "Name"), Column("#text", "Value"));
        }
        var implementation = wireType is "serviceTask" or "sendTask" or "businessRuleTask" || TaskDefinitionApplies(element, wireType) && wireType is "endEvent" or "intermediateThrowEvent";
        if (implementation)
            Attributes("implementation", "Implementation", ("class", "Java class"), ("delegateExpression", "Delegate expression"), ("expression", "Expression"),
                ("resultVariable", "Result variable"), ("type", "Type"), ("topic", "Topic"));
        if (BpmnTypeNames.IsA(wireType, "bpmn:Activity") || BpmnTypeNames.IsA(wireType, "bpmn:Event") || BpmnTypeNames.IsA(wireType, "bpmn:Gateway"))
            Add("async", "Asynchronous continuations", [Attribute("asyncBefore", "Before", true), Attribute("asyncAfter", "After", true), Attribute("exclusive", "Exclusive", true)]);
        if (BpmnTypeNames.IsA(wireType, "bpmn:Activity") || Has("inputOutput")) { Claim("inputOutput"); Io(null); }
        if (BpmnTypeNames.IsA(wireType, "bpmn:BaseElement"))
        {
            Claim("properties");
            List("properties", "Extension properties", Path("properties"), "property", Column("name", "Name"), Column("value", "Value"));
        }
        if (implementation || Has("field"))
        {
            Claim("field");
            List("fields", "Field injections", null, "field", Column("name", "Name"), Column("stringValue", "String value"), Column("expression", "Expression"));
        }
        if (BpmnTypeNames.IsA(wireType, "bpmn:FlowNode") || wireType is "sequenceFlow" or "process" || Has("executionListener"))
        {
            Claim("executionListener");
            List("execution-listeners", "Execution listeners", null, "executionListener", Column("event", "Event"), Column("class", "Java class"),
                Column("expression", "Expression"), Column("delegateExpression", "Delegate expression"));
        }
        foreach (var listenerType in new[] { "executionListener", "taskListener" })
            foreach (var (listener, index) in extras.Extensions.Where(e => e.Namespace == uri && e.LocalName == listenerType).Select((e, i) => (e, i)))
                if (listener.Children.Any(e => e.Namespace == uri && e.LocalName == "field"))
                    List(listenerType + "-fields-" + index, "Field injections", Path(listenerType) + "[" + index + "]", "field", Column("name", "Name"), Column("stringValue", "String value"), Column("expression", "Expression"));
        if (wireType == "callActivity")
        {
            Claim("in"); Claim("out");
            List("in", "In mappings", null, "in", Column("source", "Source"), Column("sourceExpression", "Source expression"), Column("target", "Target"), Column("variables", "Variables"), Column("local", "Local"), Column("businessKey", "Business key"));
            List("out", "Out mappings", null, "out", Column("source", "Source"), Column("sourceExpression", "Source expression"), Column("target", "Target"), Column("variables", "Variables"), Column("local", "Local"));
            Attributes("called-element", "Called element", ("calledElementBinding", "Binding"), ("calledElementVersion", "Version"), ("calledElementVersionTag", "Version tag"), ("calledElementTenantId", "Tenant ID"));
        }
        if (implementation || Has("connector"))
        {
            Claim("connector");
            var path = Path("connector") + "/" + Path("connectorId") + "#text";
            Add("connector", "Connector", [Scalar("connectorId", Label("connectorId", "Connector ID"), BpmnPropertyEntryKind.Text,
                BpmnExtensionNavigator.Read(extras, BpmnExtensionPath.Parse(path)), path)]);
            Io(Path("connector"), "-connector");
        }
        if (wireType == "userTask")
        {
            Attributes("assignment", "User assignment", ("assignee", "Assignee"), ("candidateUsers", "Candidate users"), ("candidateGroups", "Candidate groups"),
                ("dueDate", "Due date"), ("followUpDate", "Follow-up date"), ("priority", "Priority"));
            Attributes("forms", "Forms", ("formKey", "Form key"));
        }
    }
}
