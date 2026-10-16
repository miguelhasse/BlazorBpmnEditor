using System.Text;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Schema;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Extensions;

public sealed class ExtensionSchemaTests
{
    private static readonly string[] IoOrder = ["ioMapping", "input", "output"];
    private static readonly string[] EnumerationValues = ["One", "Two"];
    private static BpmnExtensionRegistry Registry() => new BpmnExtensionRegistry().Register(BpmnExtensionSchemas.Zeebe).Register(BpmnExtensionSchemas.Camunda7);

    [Fact]
    public void EmbeddedDescriptorsLoadAndOptionsRemainOptIn()
    {
        var options = new BpmnOptions();
        Assert.Empty(options.Extensions.Registry.Schemas);
        Assert.Same(options.Extensions, options.Extensions.AddZeebe().AddCamunda7());
        Assert.Equal(2, options.Extensions.Registry.Schemas.Count);
        Assert.Equal(BpmnExtensionSchemas.ZeebeNamespace, BpmnExtensionSchemas.Zeebe.Uri);
        Assert.Equal(BpmnExtensionSchemas.Camunda7Namespace, BpmnExtensionSchemas.Camunda7.Uri);
        Assert.Contains(BpmnExtensionSchemas.Zeebe.Types, t => t.Name == "TaskDefinition");
        Assert.Contains(BpmnExtensionSchemas.Camunda7.Types, t => t.Name == "InputOutput");
    }

    [Theory]
    [InlineData("zeebe:TaskDefinition")]
    [InlineData("{http://camunda.org/schema/zeebe/1.0}TaskDefinition")]
    public void ResolvesQualifiedAndExpandedTypeNames(string name)
    {
        var registry = Registry();
        Assert.True(registry.TryResolveType(name, out var type, out var schema));
        Assert.Equal("TaskDefinition", type.Name);
        Assert.Equal("zeebe:TaskDefinition", type.QualifiedName);
        Assert.Equal(BpmnExtensionSchemas.ZeebeNamespace, schema.Uri);
        Assert.False(registry.TryResolveType("zeebe:Missing", out _, out _));
        Assert.False(registry.TryResolveType("{broken", out _, out _));
        Assert.Same(schema, registry.TryGetSchemaByPrefix("zeebe"));
    }

    [Theory]
    [InlineData("ioMapping", "IoMapping")]
    [InlineData("input", "Input")]
    [InlineData("taskDefinition", "TaskDefinition")]
    [InlineData("taskHeaders", "TaskHeaders")]
    [InlineData("header", "Header")]
    public void ResolvesElementTags(string localName, string name) =>
        Assert.Equal(name, Registry().TryResolveElement(BpmnExtensionSchemas.ZeebeNamespace, localName)?.Name);

    [Fact]
    public void InheritsPropertiesAndOrdersChildren()
    {
        var registry = Registry();
        Assert.True(registry.TryResolveType("zeebe:Input", out var input, out _));
        var properties = registry.GetAllProperties(input);
        Assert.Contains(properties, p => p.Name == "source" && p.IsAttr);
        Assert.Contains(properties, p => p.Name == "target" && p.IsAttr);
        Assert.Contains(properties, p => p.Name == "modelerConfigurationTemplate" && p.IsAttr);
        Assert.True(registry.TryResolveType("zeebe:IoMapping", out var mapping, out _));
        Assert.Equal(IoOrder, registry.GetChildOrder(mapping));
        Assert.True(registry.CompareChildOrder(mapping, "input", "output") < 0);
        Assert.True(registry.CompareChildOrder(mapping, "output", "unknown") < 0);
    }

    [Fact]
    public void ForeignAttributesApplyThroughBpmnHierarchyAndTraits()
    {
        var attributes = Registry().ForeignAttributesFor("serviceTask");
        Assert.Contains(attributes, p => p.QualifiedName == "zeebe:modelerTemplate" && p.NamespaceUri == BpmnExtensionSchemas.ZeebeNamespace);
        Assert.Contains(attributes, p => p.QualifiedName == "camunda:modelerTemplate");
        Assert.Contains(attributes, p => p.QualifiedName == "camunda:taskPriority");
        Assert.DoesNotContain(Registry().ForeignAttributesFor("messageFlow"), p => p.Name == "modelerTemplate");
    }

    [Fact]
    public void AllowedInResolvesExtensionTraits()
    {
        var registry = Registry();
        Assert.Contains(registry.ExtensionElementTypesFor("serviceTask"), t => t.QualifiedName == "zeebe:TaskDefinition");
        Assert.DoesNotContain(registry.ExtensionElementTypesFor("startEvent"), t => t.QualifiedName == "zeebe:TaskDefinition");
        Assert.Contains(registry.ExtensionElementTypesFor("serviceTask"), t => t.QualifiedName == "camunda:Connector");
        Assert.Contains(registry.ExtensionElementTypesFor("startEvent"), t => t.QualifiedName == "camunda:Properties");
    }

    [Theory]
    [InlineData("serviceTask", "bpmn:Task", true)]
    [InlineData("serviceTask", "Activity", true)]
    [InlineData("eventSubProcess", "SubProcess", true)]
    [InlineData("startEvent", "CatchEvent", true)]
    [InlineData("endEvent", "ThrowEvent", true)]
    [InlineData("boundaryEvent", "FlowNode", true)]
    [InlineData("sequenceFlow", "FlowElement", true)]
    [InlineData("process", "RootElement", true)]
    [InlineData("process", "FlowElementsContainer", true)]
    [InlineData("collaboration", "BaseElement", true)]
    [InlineData("messageFlow", "FlowElement", false)]
    [InlineData("dataObjectReference", "FlowElement", true)]
    [InlineData("timerEventDefinition", "RootElement", true)]
    [InlineData("userTask", "bpmn:ServiceTask", false)]
    public void BpmnHierarchyMatches(string type, string parent, bool expected) => Assert.Equal(expected, BpmnTypeNames.IsA(type, parent));

    [Theory]
    [InlineData("{}")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"name\":\"x\",\"prefix\":\"x\",\"uri\":\"urn:x\",\"types\":{}}")]
    [InlineData("{\"name\":\"x\",\"prefix\":\"x\",\"uri\":\"urn:x\",\"types\":[{\"name\":\"A\",\"isAbstract\":\"true\"}]}")]
    public void RejectsMalformedDescriptors(string json) => Assert.Throws<BpmnExtensionSchemaException>(() => BpmnExtensionSchemaLoader.Load(json));

    [Fact]
    public void RejectsOversizedStringAndStreamWithoutDisposingStream()
    {
        var json = new string(' ', BpmnExtensionSchemaLoader.MaximumInputBytes + 1);
        Assert.Throws<BpmnExtensionSchemaException>(() => BpmnExtensionSchemaLoader.Load(json));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        Assert.Throws<BpmnExtensionSchemaException>(() => BpmnExtensionSchemaLoader.Load(stream));
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void RejectsConflictsButAcceptsEquivalentRegistrations()
    {
        var registry = Registry();
        registry.Register(BpmnExtensionSchemaLoader.Load(File.ReadAllText(Path.Combine(FindRoot(), "src", "Bpmn", "Model", "Extensions", "Schema", "Resources", "zeebe.json"))));
        Assert.Throws<BpmnExtensionSchemaException>(() => registry.Register(BpmnExtensionSchemas.Zeebe with { Version = "different" }));
        Assert.Throws<BpmnExtensionSchemaException>(() => registry.Register(BpmnExtensionSchemas.Zeebe with { Uri = "urn:other" }));
    }

    [Fact]
    public void SupportsCrossSchemaInheritanceReferencesAndSerializationHints()
    {
        var a = BpmnExtensionSchemaLoader.Load("""{"name":"a","uri":"urn:a","prefix":"a","types":[{"name":"Base","isAbstract":true,"superClass":["Element"],"properties":[{"name":"ref","type":"bpmn:BaseElement","isAttr":true,"isReference":true}]}]}""");
        var b = BpmnExtensionSchemaLoader.Load("""{"name":"b","uri":"urn:b","prefix":"b","types":[{"name":"Derived","superClass":["a:Base","bpmn:BaseElement"],"properties":[{"name":"number","type":"Integer","default":3},{"name":"flag","type":"Boolean","default":false},{"name":"child","type":"a:Base","xml":{"serialize":"xsi:type"}}]}],"enumerations":[{"name":"Mode","literalValues":[{"name":"One"},{"name":"Two"}]}]}""");
        var registry = new BpmnExtensionRegistry().Register(a).Register(b);
        Assert.True(registry.TryResolveType("b:Derived", out var type, out _));
        Assert.True(registry.IsReference(type, "ref"));
        Assert.True(registry.IsReference("b:Derived", "a:ref"));
        Assert.Equal("3", registry.GetAllProperties(type).Single(p => p.Name == "number").Default);
        Assert.Equal("false", registry.GetAllProperties(type).Single(p => p.Name == "flag").Default);
        Assert.Equal("xsi:type", registry.GetAllProperties(type).Single(p => p.Name == "child").Serialize);
        Assert.Contains("child", registry.GetChildOrder(type));
        Assert.Equal("Derived", type.ElementName); // No tagAlias means case is retained.
        Assert.Equal(EnumerationValues, b.Enumerations.Single().Values);
        Assert.Contains(registry.ExtensionElementTypesFor("task"), t => t.Name == "Derived");
        Assert.DoesNotContain(registry.ExtensionElementTypesFor("task"), t => t.IsAbstract);
    }

    [Fact]
    public void RegistrationSnapshotsCallerOwnedLists()
    {
        var types = new List<BpmnExtensionType> { new("Item", ["Element"], [], []) };
        var registry = new BpmnExtensionRegistry().Register(new("x", "urn:x", "x", types));
        types.Clear();
        Assert.True(registry.TryResolveType("x:Item", out _, out _));
    }

    [Fact]
    public void CyclicSuperclassChainFailsClearly()
    {
        var registry = new BpmnExtensionRegistry().Register(BpmnExtensionSchemaLoader.Load("""{"name":"x","uri":"urn:x","prefix":"x","types":[{"name":"A","superClass":["B"]},{"name":"B","superClass":["A"]}]}"""));
        Assert.True(registry.TryResolveType("x:A", out var type, out _));
        Assert.Throws<BpmnExtensionSchemaException>(() => registry.GetAllProperties(type));
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, "src"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Repository root not found.");
    }
}
