using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.TemplateModel;

public sealed class BpmnElementTemplateTests
{
    private const string ZeebeSchema = "https://unpkg.com/@camunda/zeebe-element-templates-json-schema/resources/schema.json";
    private const string CamundaSchema = "https://unpkg.com/@camunda/element-templates-json-schema/resources/schema.json";

    private const string RestTemplate = """
        {
          "$schema":"https://unpkg.com/@camunda/zeebe-element-templates-json-schema/resources/schema.json",
          "id":"example.rest","name":"REST connector","version":3,"appliesTo":["bpmn:Task"],
          "description":"Calls a remote service","icon":{"contents":"data:image/svg+xml;base64,AA=="},
          "category":{"id":"connectors","name":"Connectors"},"engines":{"camunda":">=8.5 <9.0"},
          "groups":[{"id":"request","label":"Request","openByDefault":true}],
          "properties":[
            {"id":"method","label":"Method","type":"Dropdown","value":"get","choices":[{"name":"GET","value":"get"}],"group":"request","binding":{"type":"zeebe:taskHeader","key":"method"}},
            {"id":"url","label":"URL","type":"String","condition":{"property":"method","oneOf":["get","post"]},"group":"request","constraints":{"notEmpty":true,"minLength":1,"maxLength":2048,"pattern":{"value":"^https?://","message":"Use HTTP"}},"binding":{"type":"zeebe:input","name":"url"}},
            {"id":"token","label":"Token","type":"Hidden","generatedValue":{"type":"uuid"},"binding":{"type":"zeebe:property","name":"token"}},
            {"id":"retries","label":"Retries","type":"Hidden","value":3,"binding":{"type":"zeebe:taskDefinition","property":"retries"}}
          ]
        }
        """;

    private const string Camunda7Template = """
        {
          "$schema":"https://unpkg.com/@camunda/element-templates-json-schema/resources/schema.json",
          "id":"example.c7","name":"C7 connector","appliesTo":["bpmn:ServiceTask"],
          "properties":[{"id":"connector","type":"Hidden","value":"http","binding":{"type":"camunda:property","name":"connectorId"}}],
          "scopes":[{"type":"camunda:Connector","properties":[
            {"id":"url","label":"URL","type":"String","binding":{"type":"camunda:inputParameter","name":"url"}},
            {"id":"field","type":"Hidden","value":"x","binding":{"type":"camunda:field","name":"config"}}
          ]},{"type":"bpmn:Error","id":"service-error","properties":[]}]
        }
        """;

    private const string InvalidTemplate = """
        {"$schema":"https://unpkg.com/@camunda/zeebe-element-templates-json-schema/resources/schema.json",
         "id":"invalid","name":"Invalid","appliesTo":["bpmn:Task"],
         "properties":[{"id":"broken","type":"String","binding":{"type":"not-a-binding"}}]}
        """;

    [Fact]
    public void LoadSingleTemplateParsesMetadataAndProperties()
    {
        var result = BpmnElementTemplateLoader.Load(RestTemplate);
        var template = Assert.Single(result.Templates);
        Assert.Empty(result.Errors);
        Assert.Equal("example.rest", template.Id);
        Assert.Equal(BpmnTemplateFlavor.Zeebe, template.Flavor);
        Assert.Equal("data:image/svg+xml;base64,AA==", template.Icon);
        Assert.Equal("Connectors", template.Category?.Name);
        Assert.Equal("method", template.Properties[0].Binding?.Key);
        Assert.Equal(BpmnTemplatePropertyType.Dropdown, template.Properties[0].Type);
        Assert.Equal("3", template.Properties[3].Value);
        Assert.Equal("uuid", template.Properties[2].GeneratedValue?.Type);
        Assert.Equal(2048, template.Properties[1].Constraints?.MaxLength);
    }

    [Fact]
    public void LoadArrayKeepsValidTemplatesAndReportsInvalidTemplate()
    {
        var result = BpmnElementTemplateLoader.Load($"[{RestTemplate},{Camunda7Template},{InvalidTemplate}]");
        Assert.Equal(2, result.Templates.Count);
        Assert.Single(result.Errors);
        Assert.Equal("EXT-004", result.Errors[0].Code);
        Assert.Equal(2, result.Errors[0].TemplateIndex);
        Assert.Equal("invalid", result.Errors[0].TemplateId);
    }

    [Fact]
    public void DuplicateDictionaryKeysRejectOnlyTheMalformedTemplate()
    {
        var malformed = RestTemplate.Replace("\"engines\":{\"camunda\":\">=8.5 <9.0\"}",
            "\"engines\":{\"camunda\":\">=8.5\",\"camunda\":\"<9.0\"}", StringComparison.Ordinal);
        var result = BpmnElementTemplateLoader.Load($"[{Camunda7Template},{malformed},{RestTemplate}]");
        Assert.Equal(2, result.Templates.Count);
        var error = Assert.Single(result.Errors);
        Assert.Equal("EXT-004", error.Code);
        Assert.Equal(1, error.TemplateIndex);
        Assert.Equal("example.rest", error.TemplateId);
    }

    [Fact]
    public void RegistryExactLookupDistinguishesUnversionedFromLatest()
    {
        var versioned = Assert.Single(BpmnElementTemplateLoader.Load(RestTemplate).Templates);
        var registry = new BpmnElementTemplateRegistry();
        registry.Add(versioned);
        Assert.Null(registry.TryGetExact(versioned.Id, null));
        Assert.False(registry.TryGetExact(versioned.Id, null, out _));
        Assert.Same(versioned, registry.TryGetExact(versioned.Id, versioned.Version));
        var unversioned = versioned with { Version = null };
        registry.Add(unversioned);
        Assert.Same(unversioned, registry.TryGetExact(versioned.Id, null));
        Assert.True(registry.TryGetExact(versioned.Id, null, out var found));
        Assert.Same(unversioned, found);
        Assert.Same(versioned, registry.GetLatest(versioned.Id));
        Assert.Same(versioned, registry.TryGet(versioned.Id));
    }

    [Fact]
    public void Camunda7ScopesFollowSchemaShape()
    {
        var result = BpmnElementTemplateLoader.Load(Camunda7Template);
        var template = Assert.Single(result.Templates);
        Assert.Equal(BpmnTemplateFlavor.Camunda7, template.Flavor);
        Assert.Equal("camunda:Connector", template.Scopes[0].Type);
        Assert.Equal("url", template.Scopes[0].Properties[0].Binding?.Get("name"));
        Assert.Equal("bpmn:Error", template.Scopes[1].Type);
        Assert.Equal("service-error", template.Scopes[1].Id);
    }

    [Fact]
    public void ConditionEvaluatorHandlesChainsAllMatchAndCycles()
    {
        var props = new BpmnTemplateProperty[]
        {
            new() { Id = "mode" },
            new() { Id = "endpoint", Condition = new PropertyCondition("mode", BpmnTemplateConditionOperator.OneOf, Values: ["post", "put"]) },
            new() { Id = "secret", Condition = new AllMatchCondition([
                new("mode", BpmnTemplateConditionOperator.Equals, "post"),
                new("endpoint", BpmnTemplateConditionOperator.IsActive)]) },
            new() { Id = "nonempty", Condition = new PropertyCondition("mode", BpmnTemplateConditionOperator.IsEmpty, BooleanValue: false) },
            new() { Id = "empty", Condition = new PropertyCondition("blank", BpmnTemplateConditionOperator.IsEmpty) },
            new() { Id = "blank" },
            new() { Id = "cycle-a", Condition = new PropertyCondition("cycle-b", BpmnTemplateConditionOperator.IsActive) },
            new() { Id = "cycle-b", Condition = new PropertyCondition("cycle-a", BpmnTemplateConditionOperator.IsActive) }
        };
        var template = new BpmnElementTemplate { Properties = props };
        var values = new Dictionary<string, string?> { ["mode"] = "post", ["blank"] = "" };
        var active = BpmnTemplateConditionEvaluator.ActiveProperties(template, property => values.GetValueOrDefault(property.Id!));
        Assert.Contains("endpoint", active);
        Assert.Contains("secret", active);
        Assert.Contains("nonempty", active);
        Assert.Contains("empty", active);
        Assert.DoesNotContain("cycle-a", active);
        Assert.DoesNotContain("cycle-b", active);
    }

    [Fact]
    public void RegistryIndexesVersionsAndFiltersByTypeEngineAndDeprecation()
    {
        var loaded = Assert.Single(BpmnElementTemplateLoader.Load(RestTemplate).Templates);
        var registry = new BpmnElementTemplateRegistry();
        Assert.True(registry.Add(loaded with { Version = 1 }).Added);
        Assert.True(registry.Add(loaded with { Version = 3 }).Added);
        Assert.False(registry.Add(loaded with { Version = 3 }).Added);
        Assert.Equal([1, 3], registry.Versions(loaded.Id));
        Assert.Equal(3, registry.GetLatest(loaded.Id)?.Version);
        Assert.Single(registry.ApplicableTo("bpmn:ServiceTask", engines: new Dictionary<string, string> { ["camunda"] = "8.6.1" }));
        Assert.Empty(registry.ApplicableTo("bpmn:ServiceTask", engines: new Dictionary<string, string> { ["camunda"] = "9.1.0" }));
        registry.Add(loaded with { Id = "deprecated", Version = 1, Deprecated = new("Do not use") });
        Assert.DoesNotContain(registry.ApplicableTo("bpmn:ServiceTask"), template => template.Id == "deprecated");
        Assert.Contains(registry.ApplicableTo("bpmn:ServiceTask", includeDeprecated: true), template => template.Id == "deprecated");
    }

    [Theory]
    [InlineData("^1.2.3", "1.9.0", true)]
    [InlineData("^1.2.3", "2.0.0", false)]
    [InlineData("~1.2.3", "1.2.8", true)]
    [InlineData(">=1.2.0 <2.0.0", "1.5.0", true)]
    [InlineData(">=1.2.0 <2.0.0", "2.0.0", false)]
    [InlineData("^8.5 || 9.x", "9.2.0", true)]
    [InlineData("1.4.x", "1.4.9", true)]
    [InlineData("1.2", "1.2.9", true)]
    [InlineData("1.2", "1.3.0", false)]
    [InlineData(">=1.0.0-alpha.2 <1.0.0-alpha.11", "1.0.0-alpha.10", true)]
    [InlineData("*", "1.0.0-01", false)]
    public void SemverRangeSupportsCommonOperators(string range, string version, bool expected) =>
        Assert.Equal(expected, BpmnSemverRange.Satisfies(range, version));

    [Fact]
    public void MarkerReaderUsesExpandedNameAndFlavorNamespace()
    {
        var extras = new BpmnElementExtras
        {
            ForeignAttributes = new Dictionary<string, string>
            {
                [$"{{{BpmnTemplateMarkers.CamundaNamespaceUri}}}modelerTemplate"] = "example.c7",
                [$"{{{BpmnTemplateMarkers.CamundaNamespaceUri}}}modelerTemplateVersion"] = "7"
            }
        };
        Assert.True(BpmnTemplateMarkers.TryReadApplied(extras, out var id, out var version, out var flavor));
        Assert.Equal("example.c7", id);
        Assert.Equal(7, version);
        Assert.Equal(BpmnTemplateFlavor.Camunda7, flavor);
    }

    [Fact]
    public void OptionsAccumulateLoaderAndDuplicateErrors()
    {
        var options = new BpmnTemplateOptions();
        Assert.Single(options.AddTemplates(RestTemplate).Templates);
        Assert.Empty(options.AddTemplates(RestTemplate).Templates);
        Assert.Single(options.LoadErrors);
        Assert.NotNull(options.Registry.GetLatest("example.rest"));
    }
}


