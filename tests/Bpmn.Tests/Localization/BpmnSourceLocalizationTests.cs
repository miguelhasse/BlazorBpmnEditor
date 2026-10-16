using System.Globalization;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Localization;

public sealed class BpmnSourceLocalizationTests
{
    private const string Xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                     xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                     id="Definitions_1" targetNamespace="urn:test">
          <process id="Process_1">
            <startEvent id="Start_1" name="Begin" />
            <task id="Task_1" name="Review" />
            <sequenceFlow id="Flow_1" sourceRef="Start_1" targetRef="Task_1" />
          </process>
          <bpmndi:BPMNDiagram id="Diagram_1">
            <bpmndi:BPMNPlane id="Plane_1" bpmnElement="Process_1">
              <bpmndi:BPMNShape id="Start_1_di" bpmnElement="Start_1"><dc:Bounds x="0" y="0" width="36" height="36" /></bpmndi:BPMNShape>
              <bpmndi:BPMNShape id="Task_1_di" bpmnElement="Task_1"><dc:Bounds x="100" y="0" width="100" height="80" /></bpmndi:BPMNShape>
            </bpmndi:BPMNPlane>
          </bpmndi:BPMNDiagram>
        </definitions>
        """;

    private static readonly (string Key, string Value)[] French =
    [
        ("Property.name", "Nom"),
        ("Property.loopCharacteristics", "Boucle"),
        ("PropertyChoice.loopCharacteristics.standard", "Standard (fr)"),
        ("Property.eventDefinition", "Declencheur"),
        ("EventTrigger.Message", "Message (fr)"),
        ("ElementType.userTask", "Tache utilisateur"),
        ("TypeOption.Event", "{1} / {0}"),
        ("ContextMenu.Command.Copy", "Copier"),
        ("ContextMenu.Command.SelectAll", "Tout selectionner"),
        ("ContextualActions.Append.Task", "Ajouter une tache"),
        ("ContextualActions.EditLabel", "Renommer"),
    ];

    [Fact]
    public void DirectInspectorLocalizesDescriptorsChoicesAndSwitchOptionsButNotIdentifiers()
    {
        var display = new BpmnDisplayLocalizer(new TestStrings(French));
        var document = BpmnXmlReader.Read(Xml).Document;

        var task = BpmnElementInspector.Describe(document, "Task_1", display: display)!;
        var start = BpmnElementInspector.Describe(document, "Start_1", display: display)!;

        var name = task.Properties.Single(property => property.Name == "name");
        Assert.Equal("Nom", name.Label);
        Assert.Equal("Review", name.Value);
        Assert.Equal("Review", task.Label);
        Assert.Equal("task", task.ElementType);
        var loop = task.Properties.Single(property => property.Name == "loopCharacteristics");
        Assert.Equal("Boucle", loop.Label);
        Assert.Equal("Standard (fr)", loop.Choices.Single(choice => choice.Value == "standard").Label);
        Assert.Equal("None", loop.Choices.Single(choice => choice.Value == "none").Label);
        var userTask = task.SwitchableTypes.Single(option => option.Type == BpmnFlowNodeType.UserTask);
        Assert.Equal("Tache utilisateur", userTask.Label);

        var trigger = start.Properties.Single(property => property.Name == "eventDefinition");
        Assert.Equal("Declencheur", trigger.Label);
        Assert.Equal("Message (fr)", trigger.Choices.Single(choice => choice.Value == "message").Label);
        Assert.Equal("Start Event / Message (fr)", start.SwitchableTypes
            .Single(option => option.EventDefinition == BpmnEventDefinitionType.Message).Label);
        Assert.Equal("Begin", start.Properties.Single(property => property.Name == "name").Value);
    }

    [Fact]
    public void RenderTimeHelpersKeepSourceLocalizedCaptions()
    {
        var display = new BpmnDisplayLocalizer(new TestStrings(French));
        var snapshot = BpmnElementInspector.Describe(BpmnXmlReader.Read(Xml).Document, "Start_1", display: display)!;
        var option = snapshot.SwitchableTypes.Single(candidate => candidate.EventDefinition == BpmnEventDefinitionType.Message);
        var localizedEntry = new BpmnMenuEntry(BpmnMenuCommand.Copy, "Copier");
        var localizedAction = new BpmnContextualAction(BpmnActionKind.EditLabel, "Renommer", "e");

        Assert.Equal(snapshot.Properties, display.Snapshot(snapshot).Properties, new DescriptorComparer());
        Assert.Equal(option.Label, display.TypeOption(option));
        Assert.Equal("Copier", display.MenuCommand(localizedEntry));
        Assert.Equal("Renommer", display.ContextualAction(localizedAction));
        Assert.Equal("Host copy", display.MenuCommand(localizedEntry with { Label = "Host copy" }));
        Assert.Equal("Copier", display.MenuCommand(localizedEntry with { Label = "Copy" }));
    }

    [Fact]
    public void DirectConsumersReadNeutralResourcesForTheInvariantUiCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            var snapshot = BpmnElementInspector.Describe(BpmnXmlReader.Read(Xml).Document, "Task_1")!;
            var actions = BpmnContextualActionsBase.DefaultsFor(snapshot);

            Assert.Equal("Name", snapshot.Properties.Single(property => property.Name == "name").Label);
            Assert.Equal("Loop", snapshot.Properties.Single(property => property.Name == "loopCharacteristics")
                .Choices.Single(choice => choice.Value == "standard").Label);
            Assert.Equal("User Task", snapshot.SwitchableTypes.Single(option => option.Type == BpmnFlowNodeType.UserTask).Label);
            Assert.Equal(["Append Task", "Append Exclusive Gateway", "Append End Event", "Rename", "Delete", "More actions"],
                actions.Select(action => action.Label));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task SessionLocalizesCommandsActionsAndSnapshotsWithTheHostLocalizer()
    {
        using var services = new ServiceCollection()
            .AddBpmnComponents()
            .AddSingleton<IStringLocalizer<BpmnStrings>>(new TestStrings(French))
            .BuildServiceProvider();
        var document = BpmnXmlReader.Read(Xml).Document;
        var session = new BpmnEditorSession(new BpmnOptions(), services)
        {
            Document = document,
            DiagramId = "Diagram_1",
        };
        session.UpdateParameters(EditorConfig.CreateDefault());

        await session.SetSelectionAsync(["Task_1"]);
        var bounds = new Bounds(0, 0, 1, 1);
        await session.HandleContextMenuRequestedAsync(
            new ContextMenuRequested("element", "Task_1", new Anchor(bounds, bounds), "pointer", ["Task_1"]));

        var menu = session.State.Overlays.ContextMenu;
        Assert.Equal("Copier", menu.Commands.Single(entry => entry.Command == BpmnMenuCommand.Copy).Label);
        Assert.Equal("Cut", menu.Commands.Single(entry => entry.Command == BpmnMenuCommand.Cut).Label);
        Assert.Equal("Nom", menu.Snapshot!.Properties.Single(property => property.Name == "name").Label);
        Assert.Equal("Review", menu.Snapshot.Properties.Single(property => property.Name == "name").Value);
        Assert.Equal("Tache utilisateur", menu.Snapshot.SwitchableTypes.Single(option => option.Type == BpmnFlowNodeType.UserTask).Label);
        var actions = session.State.Overlays.ContextualActions.Actions;
        Assert.Equal("Ajouter une tache", actions.Single(action => action.Kind == BpmnActionKind.Append && action.Type == BpmnFlowNodeType.Task).Label);
        Assert.Equal("Renommer", actions.Single(action => action.Kind == BpmnActionKind.EditLabel).Label);
        Assert.Equal("More actions", actions.Single(action => action.Kind == BpmnActionKind.OpenMenu).Label);
    }

    [Theory]
    [InlineData("PropertyChoice.loopCharacteristics.none", "None")]
    [InlineData("PropertyChoice.loopCharacteristics.standard", "Loop")]
    [InlineData("PropertyChoice.loopCharacteristics.multiInstanceSequential", "Multi-instance (sequential)")]
    [InlineData("PropertyChoice.loopCharacteristics.multiInstanceParallel", "Multi-instance (parallel)")]
    [InlineData("PropertyChoice.multiInstance.behavior.All", "All")]
    [InlineData("PropertyChoice.multiInstance.behavior.One", "One")]
    [InlineData("PropertyChoice.multiInstance.behavior.None", "None")]
    [InlineData("PropertyChoice.multiInstance.behavior.Complex", "Complex")]
    [InlineData("PropertyChoice.associationDirection.none", "None")]
    [InlineData("PropertyChoice.associationDirection.one", "One")]
    [InlineData("PropertyChoice.associationDirection.both", "Both")]
    [InlineData("PropertyChoice.processType.None", "None")]
    [InlineData("PropertyChoice.processType.Public", "Public")]
    [InlineData("PropertyChoice.processType.Private", "Private")]
    [InlineData("PropertyChoice.Empty", "(none)")]
    [InlineData("EventTrigger.Message", "Message")]
    public void ResourceKeyLocalizesChoicesIndependentlyOfPropertyId(string key, string english)
    {
        var display = new BpmnDisplayLocalizer(new TestStrings([(key, "Localized choice")]));
        var entry = new BpmnPropertyEntry("arbitrary-id", "Host label", BpmnPropertyEntryKind.Select)
        {
            Choices = [new("unchanged-value", english) { LabelResourceKey = key }, new("document-id", "Document name")],
        };

        var localized = display.Property(entry);

        Assert.Equal("Localized choice", localized.Choices[0].Label);
        Assert.Equal("unchanged-value", localized.Choices[0].Value);
        Assert.Equal("Document name", localized.Choices[1].Label);
        Assert.Equal(english + " host override", display.Property(entry with
        {
            Choices = [entry.Choices[0] with { Label = english + " host override" }],
        }).Choices[0].Label);
    }

    [Fact]
    public void InspectorBuiltInChoicesCarryResourceKeys()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        var task = BpmnElementInspector.Describe(document, "Task_1")!;
        var start = BpmnElementInspector.Describe(document, "Start_1")!;

        Assert.All(task.Properties.Single(property => property.Name == "loopCharacteristics").Choices,
            choice => Assert.NotNull(choice.LabelResourceKey));
        Assert.All(start.Properties.Single(property => property.Name == "eventDefinition").Choices,
            choice => Assert.NotNull(choice.LabelResourceKey));
    }

    [Fact]
    public void BuiltInCaptionsResolveByResourceKeyNotByEnglishLiteral()
    {
        var display = new BpmnDisplayLocalizer(new EchoStrings());
        var snapshot = BpmnElementInspector.Describe(BpmnXmlReader.Read(Xml).Document, "Task_1")!;

        Assert.All(Enum.GetValues<BpmnMenuCommand>(), command =>
            Assert.Equal($"[ContextMenu.Command.{command}]", display.MenuCommand(command)));
        Assert.All(BpmnContextualActionsBase.DefaultsFor(snapshot, display), action =>
            Assert.StartsWith("[ContextualActions.", action.Label, StringComparison.Ordinal));
        var groups = Enum.GetValues<BpmnStencilContext>().SelectMany(BpmnStencil.GroupsFor).ToArray();
        Assert.All(groups, group =>
        {
            Assert.Equal($"[StencilGroup.{group.Title}]", display.StencilGroup(group));
            Assert.All(group.Entries, entry =>
                Assert.StartsWith("[StencilEntry.", display.StencilEntry(entry), StringComparison.Ordinal));
        });

        var document = BpmnXmlReader.Read(Xml).Document;
        foreach (var id in new[] { "Task_1", "Start_1" })
        {
            var described = BpmnElementInspector.Describe(document, id, display: display)!;
            Assert.All(described.Properties.SelectMany(property => property.Choices)
                .Where(choice => choice.LabelResourceKey is not null),
                choice => Assert.Equal($"[{choice.LabelResourceKey}]", choice.Label));
            Assert.All(described.SwitchableTypes, option =>
                Assert.StartsWith("[", option.Label, StringComparison.Ordinal));
        }

        var custom = snapshot.SwitchableTypes.First(option => option.Type == BpmnFlowNodeType.UserTask) with { Label = "Host type" };
        Assert.Equal("Host type", display.TypeOption(custom));
    }

    private sealed class DescriptorComparer : IEqualityComparer<BpmnPropertyDescriptor>
    {
        public bool Equals(BpmnPropertyDescriptor? x, BpmnPropertyDescriptor? y) =>
            x is not null && y is not null && x.Name == y.Name && x.Label == y.Label && x.Value == y.Value
            && x.Choices.Select(choice => (choice.Value, choice.Label)).SequenceEqual(y.Choices.Select(choice => (choice.Value, choice.Label)));

        public int GetHashCode(BpmnPropertyDescriptor obj) => obj.Name.GetHashCode(StringComparison.Ordinal);
    }

    private sealed class TestStrings((string Key, string Value)[] entries) : IStringLocalizer<BpmnStrings>
    {
        private readonly Dictionary<string, string> _values = entries.ToDictionary(entry => entry.Key, entry => entry.Value);

        public LocalizedString this[string name] => _values.TryGetValue(name, out var value)
            ? new(name, value) : new(name, name, true);

        public LocalizedString this[string name, params object[] arguments] => _values.TryGetValue(name, out var value)
            ? new(name, string.Format(CultureInfo.CurrentCulture, value, arguments)) : new(name, name, true);

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) =>
            _values.Select(value => new LocalizedString(value.Key, value.Value));
    }

    private sealed class EchoStrings : IStringLocalizer<BpmnStrings>
    {
        public LocalizedString this[string name] => new(name, $"[{name}]");

        public LocalizedString this[string name, params object[] arguments] => new(name, $"[{name}]");

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
