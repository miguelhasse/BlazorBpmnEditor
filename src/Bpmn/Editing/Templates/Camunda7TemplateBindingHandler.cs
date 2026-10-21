using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

internal sealed class Camunda7TemplateBindingHandler : IBpmnTemplateBindingHandler
{
    internal const string Uri = BpmnTemplateMarkers.CamundaNamespaceUri;
    private static readonly Lazy<BpmnSchemaChildOrder> Order = new(() => new(new BpmnExtensionRegistry().Register(BpmnExtensionSchemas.Camunda7)));
    public bool CanHandle(BpmnTemplateBinding binding) => binding.Type.StartsWith("camunda:", StringComparison.Ordinal)
        || binding.Get("__scopeType") is not null;

    public string? Read(BpmnDocument document, string elementId, BpmnTemplateBinding binding)
    {
        if (binding.Get("__scopeType") == "bpmn:Error")
        {
            var error = Error(document, elementId, binding.Get("__scopeId")!);
            return binding.Name switch
            {
                "name" => error?.Name,
                "errorCode" => error?.ErrorCode,
                "camunda:errorMessage" => error?.Extras.ForeignAttributes.GetValueOrDefault("{" + Uri + "}errorMessage"),
                _ => throw new InvalidOperationException("Unsupported error scope property.")
            };
        }
        var extras = BpmnDocumentEditor.TemplateExtras(document, elementId);
        var path = Path(extras, binding);
        var node = BpmnExtensionNavigator.Resolve(extras.Extensions, path.Steps);
        if (node is null) return null;
        return binding.Type switch
        {
            "property" => node.Text,
            "camunda:inputParameter" => binding.Get("scriptFormat") is not null ? node.Children.FirstOrDefault(e => e.Namespace == Uri && e.LocalName == "script")?.Text : node.Text,
            "camunda:outputParameter" => node.Attributes.GetValueOrDefault("name"),
            "camunda:field" => node.Attributes.GetValueOrDefault(Flag(binding, "expression") ? "expression" : "stringValue")
                ?? node.Children.FirstOrDefault(e => e.Namespace == Uri && e.LocalName == (Flag(binding, "expression") ? "expression" : "string"))?.Text,
            "camunda:executionListener" when binding.Get("scriptFormat") is not null || binding.Get("implementationType") == "script" => node.Children.FirstOrDefault(e => e.Namespace == Uri && e.LocalName == "script")?.Text,
            _ => node.Attributes.GetValueOrDefault(path.AttributeName!)
        };
    }

    public BpmnDocument Write(BpmnDocument document, string elementId, BpmnTemplateBinding binding, string? value, BpmnTemplateProperty property, EditingOptions options)
    {
        if (value is null || property.Optional && value.Length == 0) return Remove(document, elementId, binding, options);
        if (binding.Get("__scopeType") == "bpmn:Error") return WriteError(document, elementId, binding, value, options);
        if (binding.Type == "camunda:errorEventDefinition")
        {
            var target = document.FindFlowNode(elementId);
            if (target?.Type != BpmnFlowNodeType.ServiceTask || target.Extras.ForeignAttributes.GetValueOrDefault("{" + Uri + "}type") != "external")
                throw new InvalidOperationException("Camunda error bindings require an external service task.");
            document = EnsureError(document, elementId, Required(binding, "errorRef"), options);
        }
        var extras = BpmnDocumentEditor.TemplateExtras(document, elementId);
        var path = Path(extras, binding);
        var updated = BpmnExtensionNavigator.UpdateElement(extras, path, node =>
        {
            var attributes = new Dictionary<string, string>(node.Attributes, StringComparer.Ordinal);
            var children = node.Children.ToList();
            var text = node.Text;
            void Attr(string name, string? stored) { if (stored is null) attributes.Remove(name); else attributes[name] = stored; }
            void Script(string body)
            {
                var old = children.FirstOrDefault(e => e.Namespace == Uri && e.LocalName == "script");
                var script = old ?? new BpmnExtensionElement(Uri, "script", new Dictionary<string, string>(), []);
                script = script with { Text = body, Attributes = new Dictionary<string, string>(script.Attributes) { ["scriptFormat"] = binding.Get("scriptFormat") ?? "" } };
                if (old is null) children.Add(script); else children[children.IndexOf(old)] = script;
                text = null;
            }
            switch (binding.Type)
            {
                case "property": text = value; break;
                case "camunda:inputParameter":
                    if (children.Any(e => e.Namespace == Uri && e.LocalName is "list" or "map")) throw new InvalidOperationException("Structured parameter remains read-only.");
                    Attr("name", Required(binding, "name"));
                    if (binding.Get("scriptFormat") is not null) Script(value); else { children.RemoveAll(e => e.Namespace == Uri && e.LocalName == "script"); text = value; }
                    break;
                case "camunda:outputParameter":
                    if (children.Any(e => e.Namespace == Uri && e.LocalName is "list" or "map")) throw new InvalidOperationException("Structured parameter remains read-only.");
                    Attr("name", value);
                    if (binding.Get("scriptFormat") is not null) Script(Required(binding, "source"));
                    else { children.RemoveAll(e => e.Namespace == Uri && e.LocalName == "script"); text = Required(binding, "source"); }
                    break;
                case "camunda:in":
                case "camunda:out":
                    var input = binding.Type == "camunda:in";
                    var variables = binding.Get("variables");
                    var all = input ? binding.Get("target") is null : binding.Get("source") is null && binding.Get("sourceExpression") is null;
                    if (all && variables is not ("all" or "local")) throw new InvalidOperationException("Mapping needs target/source or variables.");
                    Attr("local", variables == "local" || Flag(binding, "local") ? "true" : null);
                    Attr("variables", all ? "all" : null);
                    Attr("target", all ? null : input ? binding.Get("target") : value);
                    Attr("source", all ? null : input ? Flag(binding, "expression") ? null : value : binding.Get("source"));
                    Attr("sourceExpression", all ? null : input ? Flag(binding, "expression") ? value : null : binding.Get("sourceExpression"));
                    break;
                case "camunda:executionListener":
                    Attr("event", Required(binding, "event"));
                    if (binding.Get("scriptFormat") is not null || binding.Get("implementationType") == "script") Script(value);
                    else Attr(Implementation(binding), value);
                    break;
                case "camunda:field":
                    Attr("name", Required(binding, "name"));
                    Attr("stringValue", Flag(binding, "expression") ? null : value);
                    Attr("expression", Flag(binding, "expression") ? value : null);
                    children.RemoveAll(e => e.Namespace == Uri && e.LocalName is "string" or "expression");
                    break;
                default: Attr(path.AttributeName!, value); break;
            }
            return node with { Attributes = attributes, Children = children, Text = text };
        }, new(ChildOrder: options.ExtensionChildOrder ?? Order.Value));
        return BpmnDocumentEditor.TemplateReplaceExtras(document, elementId, updated);
    }

    public BpmnDocument Remove(BpmnDocument document, string elementId, BpmnTemplateBinding binding, EditingOptions options)
    {
        if (binding.Get("__scopeType") == "bpmn:Error") return WriteError(document, elementId, binding, null, options);
        var extras = BpmnDocumentEditor.TemplateExtras(document, elementId);
        var path = Path(extras, binding);
        return BpmnDocumentEditor.TemplateReplaceExtras(document, elementId, BpmnExtensionNavigator.Remove(extras,
            path with { AttributeName = null, IsText = binding.Type == "property" }));
    }

    internal static BpmnExtensionPath Path(BpmnElementExtras extras, BpmnTemplateBinding binding)
    {
        var steps = new List<BpmnExtensionPathStep>();
        if (binding.Get("__scopeType") == "camunda:Connector") steps.Add(new(Uri, "connector"));
        IReadOnlyList<BpmnExtensionElement> Children() => steps.Count == 0 ? extras.Extensions : BpmnExtensionNavigator.Resolve(extras.Extensions, steps)?.Children ?? [];
        void Keyed(string local, string key, string value) => steps.Add(new(Uri, local, new(AttributeName: key, AttributeValue: value)));
        void Find(string local, Func<BpmnExtensionElement, bool> predicate)
        {
            var candidates = Children().Where(e => e.Namespace == Uri && e.LocalName == local).ToArray();
            var index = Array.FindIndex(candidates, e => predicate(e));
            steps.Add(new(Uri, local, new(Ordinal: index < 0 ? candidates.Length : index)));
        }
        string? attribute;
        switch (binding.Type)
        {
            case "property":
                if (binding.Get("__scopeType") != "camunda:Connector" || binding.Name != "connectorId") throw new InvalidOperationException("Unsupported connector property.");
                steps.Add(new(Uri, "connectorId")); attribute = null; break;
            case "camunda:property": steps.Add(new(Uri, "properties")); Keyed("property", "name", Required(binding, "name")); attribute = "value"; break;
            case "camunda:inputParameter": steps.Add(new(Uri, "inputOutput")); Keyed("inputParameter", "name", Required(binding, "name")); attribute = null; break;
            case "camunda:outputParameter":
                steps.Add(new(Uri, "inputOutput"));
                Find("outputParameter", e => (binding.Get("scriptFormat") is null ? e.Text : e.Children.FirstOrDefault(c => c.Namespace == Uri && c.LocalName == "script")?.Text) == binding.Get("source"));
                attribute = "name"; break;
            case "camunda:field": Keyed("field", "name", Required(binding, "name")); attribute = Flag(binding, "expression") ? "expression" : "stringValue"; break;
            case "camunda:in":
                if (binding.Get("target") is { } target) Keyed("in", "target", target);
                else Find("in", e => e.Attributes.GetValueOrDefault("variables") == "all" && e.Attributes.GetValueOrDefault("local") == "true" == (binding.Get("variables") == "local"));
                attribute = binding.Get("target") is null ? "variables" : Flag(binding, "expression") ? "sourceExpression" : "source"; break;
            case "camunda:in:businessKey": Find("in", e => e.Attributes.ContainsKey("businessKey")); attribute = "businessKey"; break;
            case "camunda:out":
                if (binding.Get("source") is { } source) Keyed("out", "source", source);
                else if (binding.Get("sourceExpression") is { } expression) Keyed("out", "sourceExpression", expression);
                else Find("out", e => e.Attributes.GetValueOrDefault("variables") == "all" && e.Attributes.GetValueOrDefault("local") == "true" == (binding.Get("variables") == "local"));
                attribute = binding.Get("source") is null && binding.Get("sourceExpression") is null ? "variables" : "target"; break;
            case "camunda:executionListener":
                var implementation = binding.Get("scriptFormat") is not null ? "script" : Implementation(binding);
                Find("executionListener", e => e.Attributes.GetValueOrDefault("event") == binding.Get("event") && (implementation == "script" ? e.Children.Any(c => c.Namespace == Uri && c.LocalName == "script") : e.Attributes.ContainsKey(implementation)));
                attribute = implementation; break;
            case "camunda:errorEventDefinition":
                var alias = Required(binding, "errorRef");
                Find("errorEventDefinition", e => MatchesError(e.Attributes.GetValueOrDefault("errorRef"), alias)); attribute = "expression"; break;
            default: throw new InvalidOperationException("Unsupported Camunda binding: " + binding.Type);
        }
        return new(steps, attribute, IsText: attribute is null);
    }

    internal static BpmnDocument CloneScopedErrors(BpmnDocument document, string id, BpmnElementTemplate template)
    {
        foreach (var scope in template.Scopes.Where(s => s.Type == "bpmn:Error"))
        {
            if (Error(document, id, scope.Id!) is not { } old) continue;
            var copy = old with { Id = BpmnId.New(ErrorPrefix(scope.Id!)) };
            document = document with { Catalog = document.Catalog with { Errors = [.. document.Catalog.Errors, copy] } };
            var extras = BpmnDocumentEditor.TemplateExtras(document, id);
            document = BpmnDocumentEditor.TemplateReplaceExtras(document, id, extras with
            {
                Extensions = extras.Extensions.Select(e => e.Namespace == Uri && e.LocalName == "errorEventDefinition" && e.Attributes.GetValueOrDefault("errorRef") == old.Id
                    ? e with { Attributes = new Dictionary<string, string>(e.Attributes) { ["errorRef"] = copy.Id } } : e).ToArray()
            });
        }
        return document;
    }

    private static bool Flag(BpmnTemplateBinding binding, string name) => binding.Get(name) is "true" or "True";
    private static string Required(BpmnTemplateBinding binding, string name) => binding.Get(name) ?? throw new InvalidOperationException("Binding needs " + name);
    private static string Implementation(BpmnTemplateBinding binding) => binding.Get("implementationType") is "class" or "expression" or "delegateExpression" or "script" ? binding.Get("implementationType")! : throw new InvalidOperationException("Listener needs implementationType.");
    private static string ErrorPrefix(string alias) => "Error_" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(alias));
    private static bool MatchesError(string? actual, string alias) => actual == alias || actual?.StartsWith(ErrorPrefix(alias) + "_", StringComparison.Ordinal) == true
        || actual?.StartsWith("Error_" + alias + "_", StringComparison.Ordinal) == true;
    private static BpmnError? Error(BpmnDocument document, string id, string alias)
    {
        var reference = BpmnDocumentEditor.TemplateExtras(document, id).Extensions.FirstOrDefault(e => e.Namespace == Uri && e.LocalName == "errorEventDefinition" && MatchesError(e.Attributes.GetValueOrDefault("errorRef"), alias))?.Attributes.GetValueOrDefault("errorRef");
        return document.Catalog.Errors.FirstOrDefault(e => e.Id == reference);
    }
    private static BpmnDocument EnsureError(BpmnDocument document, string id, string alias, EditingOptions options)
    {
        var target = document.FindFlowNode(id);
        if (target?.Type != BpmnFlowNodeType.ServiceTask || target.Extras.ForeignAttributes.GetValueOrDefault("{" + Uri + "}type") != "external")
            throw new InvalidOperationException("Camunda error bindings require an external service task.");
        if (Error(document, id, alias) is not null) return document;
        var errorId = BpmnId.New(ErrorPrefix(alias));
        var error = new BpmnError(errorId);
        document = document with { Catalog = document.Catalog with { Errors = [.. document.Catalog.Errors, error] } };
        var extras = BpmnDocumentEditor.TemplateExtras(document, id);
        var binding = new BpmnTemplateBinding("camunda:errorEventDefinition", new Dictionary<string, string> { ["errorRef"] = alias });
        extras = BpmnExtensionNavigator.UpdateElement(extras, Path(extras, binding), node => node with
        { Attributes = new Dictionary<string, string>(node.Attributes) { ["errorRef"] = errorId } }, new(ChildOrder: options.ExtensionChildOrder ?? Order.Value));
        return BpmnDocumentEditor.TemplateReplaceExtras(document, id, extras);
    }
    private static BpmnDocument WriteError(BpmnDocument document, string id, BpmnTemplateBinding binding, string? value, EditingOptions options)
    {
        var alias = Required(binding, "__scopeId");
        if (Error(document, id, alias) is null)
        {
            if (value is null) return document;
            document = EnsureError(document, id, alias, options);
        }
        var error = Error(document, id, alias)!;
        switch (binding.Name)
        {
            case "name": error = error with { Name = value }; break;
            case "errorCode": error = error with { ErrorCode = value }; break;
            case "camunda:errorMessage":
                var attributes = new Dictionary<string, string>(error.Extras.ForeignAttributes);
                if (value is null) attributes.Remove("{" + Uri + "}errorMessage"); else attributes["{" + Uri + "}errorMessage"] = value;
                document = BpmnDocumentEditor.TemplateReplaceExtras(document, error.Id, error.Extras with { ForeignAttributes = attributes });
                error = document.Catalog.Errors.First(e => e.Id == error.Id); break;
            default: throw new InvalidOperationException("Unsupported error scope property.");
        }
        return document with { Catalog = document.Catalog with { Errors = document.Catalog.Errors.Select(e => e.Id == error.Id ? error : e).ToArray() } };
    }
}
