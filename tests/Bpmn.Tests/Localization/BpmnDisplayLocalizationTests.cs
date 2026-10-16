using System.Globalization;
using System.Resources;

using Hasseware.AspNetCore.Components.Bpmn.Accessibility;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Icons;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Localization;

public sealed class BpmnDisplayLocalizationTests
{
    [Fact]
    public void StandardPropertyCaptionsTranslateWithoutChangingValuesOrReferenceNames()
    {
        var display = Display(("Property.name", "Name translated"), ("Property.calledElementRef", "Calls translated"),
            ("Property.loopCharacteristics", "Loop translated"), ("PropertyChoice.loopCharacteristics.standard", "Loop option"));
        var name = display.Property(new("name", "Name", BpmnPropertyEntryKind.Text, "Name"));
        var calls = display.Property(new("calledElementRef", "Calls", BpmnPropertyEntryKind.Select, "Definition_1")
        {
            Choices = [new("Definition_1", "Name")],
        });
        var loop = display.Property(new("loopCharacteristics", "Loop", BpmnPropertyEntryKind.Select, "standard")
        {
            Choices = [new("standard", "Loop")],
        });

        Assert.Equal("Name translated", name.Label);
        Assert.Equal("Name", name.Value);
        Assert.Equal("Calls translated", calls.Label);
        Assert.Equal("Definition_1", calls.Value);
        Assert.Equal("Name", calls.Choices.Single().Label);
        Assert.Equal("Loop option", loop.Choices.Single().Label);
        Assert.Equal("standard", loop.Choices.Single().Value);
    }

    [Fact]
    public void HostAndTemplateCaptionsRemainUnchangedEvenWhenTheyMatchBuiltInEnglish()
    {
        var display = Display(("Property.name", "Translated name"), ("StencilEntry.Task", "Translated task"),
            ("PropertyGroup.general", "Translated general"));
        var host = new BpmnPropertyGroup("general", "General", [new("name", "Name", BpmnPropertyEntryKind.Text)]);
        var template = new BpmnPropertyEntry("name", "Name", BpmnPropertyEntryKind.Text) { GroupId = "template:mail" };
        var groups = BpmnStencilCuration.Apply(BpmnStencil.GroupsFor(BpmnStencilContext.StandaloneProcess),
            new BpmnStencilOptions(new Dictionary<string, BpmnStencilEntryOptions>
            {
                [new BpmnStencilEntry(BpmnFlowNodeType.Task, "Task").Key] = new(Label: "Task"),
            }));

        Assert.Same(host, display.Group(host));
        Assert.Same(template, display.Property(template));
        Assert.Equal("Task", display.StencilEntry(groups.SelectMany(group => group.Entries)
            .Single(entry => entry.Type == BpmnFlowNodeType.Task && entry.Kind == BpmnStencilKind.FlowNode)));
        Assert.Equal("Host name", display.Property(new("name", "Host name", BpmnPropertyEntryKind.Text)).Label);
    }

    [Fact]
    public void IndexedPropertiesAndSchemaCollectionsUseSemanticKeys()
    {
        var display = Display(("Property.assignment.from.language", "Assignment {0} language translated"),
            ("Schema.camunda:inputs", "Inputs translated"), ("Schema.camunda:name", "Column translated"));
        var indexed = display.Property(new("assignment.2.from.language", "Assignment 3 from language", BpmnPropertyEntryKind.Text, "urn:language"));
        var group = display.Group(new("camunda:inputs-connector", "Inputs",
            [new("inputs-connector", "Inputs", BpmnPropertyEntryKind.List) { Columns = [new("name", "Name")] }])
        {
            Namespace = "http://camunda.org/schema/1.0/bpmn",
            HasBuiltInLabels = true,
        });

        Assert.Equal("Assignment 3 language translated", indexed.Label);
        Assert.Equal("urn:language", indexed.Value);
        Assert.Equal("Inputs translated", group.Label);
        Assert.Equal("Inputs translated", group.Entries.Single().Label);
        Assert.Equal("Column translated", group.Entries.Single().Columns!.Single().Label);
    }

    [Fact]
    public void AccessibleDescriptionsTranslateStructureButNotDocumentNames()
    {
        var display = Display(("AccessibleType.userTask", "User task translated"),
            ("AccessibleTree.Incoming", "{0} incoming translated"), ("AccessibleTree.NoOutgoing", "No outgoing translated"));
        var node = new BpmnGraphNode("Task_1", "userTask", "Name", "Process_1", null, 1, [], ["Start_1"]);

        Assert.Equal("User task translated Name. 1 incoming translated, No outgoing translated.", display.AccessibleName(node));
        Assert.Equal("User task Name. 1 incoming, no outgoing.", node.AccessibleName);
        Assert.Equal("Name", node.Label);
    }

    [Fact]
    public void MissingTranslationsFallBackAndEveryIconAndTypeHasANeutralResource()
    {
        using var provider = new ServiceCollection().AddBpmnComponents().BuildServiceProvider();
        var display = provider.GetRequiredService<BpmnDisplayLocalizer>();
        var resources = new ResourceManager(typeof(BpmnStrings));
        foreach (var type in Enum.GetValues<BpmnFlowNodeType>())
        {
            var key = $"ElementType.{BpmnElementInspector.WireNameOf(type)}";
            Assert.Equal(BpmnElementInspector.TypeLabel(type), resources.GetString(key, CultureInfo.InvariantCulture));
        }
        foreach (var icon in BpmnIconRegistry.Default.Icons)
        {
            Assert.Equal(icon.AccessibilityLabel, resources.GetString($"Icon.{icon.IconId}", CultureInfo.InvariantCulture));
            Assert.Equal(icon.AccessibilityLabel, display.IconLabel(icon));
        }
        Assert.Equal("Message Start Event", Display().TypeOption(new(
            BpmnFlowNodeType.StartEvent, BpmnEventDefinitionType.Message, "Message Start Event", false)));
    }

    private static BpmnDisplayLocalizer Display(params (string Key, string Value)[] entries) =>
        new(new TestStrings(entries.ToDictionary(entry => entry.Key, entry => entry.Value)));

    private sealed class TestStrings(Dictionary<string, string> values) : IStringLocalizer<BpmnStrings>
    {
        public LocalizedString this[string name] => values.TryGetValue(name, out var value)
            ? new(name, value) : new(name, name, true);
        public LocalizedString this[string name, params object[] arguments] => values.TryGetValue(name, out var value)
            ? new(name, string.Format(CultureInfo.CurrentCulture, value, arguments)) : new(name, name, true);
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            values.Select(value => new LocalizedString(value.Key, value.Value));
    }
}
