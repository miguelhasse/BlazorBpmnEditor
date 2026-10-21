using Bunit;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Interop;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class ContextMenuDropdownTests : BunitContext
{
    private const string Xml = """
        <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                     id="Definitions_1" targetNamespace="urn:test">
          <process id="Process_1"><task id="Task_1" name="Work" /></process>
        </definitions>
        """;

    public ContextMenuDropdownTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
    }

    [Fact]
    public async Task DropdownDistinguishesEventDefinitionsAndSelectsCurrentSnapshotType()
    {
        var snapshot = Snapshot() with
        {
            SwitchableTypes =
            [
                new(BpmnFlowNodeType.StartEvent, BpmnEventDefinitionType.None, "Start", false),
                new(BpmnFlowNodeType.StartEvent, BpmnEventDefinitionType.Message, "Message", true),
                new(BpmnFlowNodeType.StartEvent, BpmnEventDefinitionType.Timer, "Timer", false),
            ],
        };
        var (menu, _) = await OpenAsync(snapshot);

        Assert.Equal("StartEvent:Message", KitFixture.MenuTypeValue(menu));
        var values = menu.FindAll("option, fluent-option").Select(option => option.GetAttribute("value")).ToArray();
        Assert.Equal(3, values.Distinct().Count());
        Assert.Contains("StartEvent:Timer", values);
    }

    [Theory]
    [InlineData(false, 2)]
    [InlineData(true, 1)]
    public async Task DropdownIsAbsentWithoutSwitchCapabilityOrAlternatives(bool canSwitch, int count)
    {
        var snapshot = Snapshot();
        snapshot = snapshot with
        {
            Capabilities = snapshot.Capabilities with { CanSwitchType = canSwitch },
            SwitchableTypes = snapshot.SwitchableTypes.Take(count).ToArray(),
        };
        var (menu, _) = await OpenAsync(snapshot);
        Assert.Empty(menu.FindAll(".bpmn-menu__type-select"));
    }

    [Fact]
    public async Task AcceptedSwitchUpdatesDropdownAndKeepsMenuOpen()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        var (menu, session) = await OpenAsync(Snapshot(), document, liveSnapshot: true);

        await KitFixture.PickMenuTypeAsync(menu, "ServiceTask:None");

        Assert.Equal("ServiceTask:None", KitFixture.MenuTypeValue(menu));
        Assert.Equal("serviceTask", session.ContextMenuSnapshot!.ElementType);
        Assert.True(session.IsContextMenuOpen);
        Assert.Equal("Task_1", session.ContextMenuSnapshot.ElementId);
        Assert.Equal("Work", session.ContextMenuSnapshot.Label);
    }

    [Fact]
    public async Task RefusedSwitchRestoresCurrentSelectionAndKeepsMenuOpen()
    {
        var snapshot = Snapshot() with
        {
            SwitchableTypes =
            [
                new(BpmnFlowNodeType.Task, BpmnEventDefinitionType.None, "Task", true),
                new(BpmnFlowNodeType.StartEvent, BpmnEventDefinitionType.None, "Start", false),
            ],
        };
        var document = BpmnXmlReader.Read(Xml).Document;
        var (menu, session) = await OpenAsync(snapshot, document);

        await KitFixture.PickMenuTypeAsync(menu, "StartEvent:None");

        Assert.Equal("Task:None", KitFixture.MenuTypeValue(menu));
        Assert.Equal("task", BpmnElementInspector.Describe(session.EditedDocument!, "Task_1")!.ElementType);
        Assert.True(session.IsContextMenuOpen);
    }

    [Fact]
    public async Task SelectingCurrentOptionDoesNotChangeDocument()
    {
        var document = BpmnXmlReader.Read(Xml).Document;
        var (menu, session) = await OpenAsync(Snapshot(), document);
        var before = session.EditedDocument;

        await KitFixture.PickMenuTypeAsync(menu, "Task:None");

        Assert.Same(before, session.EditedDocument);
        Assert.True(session.IsContextMenuOpen);
    }

    private async Task<(IRenderedComponent<BpmnPart> Menu, BpmnEditorSession Session)> OpenAsync(
        BpmnElementSnapshot snapshot, BpmnDocument? document = null, bool liveSnapshot = false)
    {
        BpmnEditorSession? session = null;
        var menu = this.RenderSeeded<BpmnPart>(
            seed =>
            {
                seed.Snapshots([snapshot]).Session(value => session = value);
                if (document is not null) seed.Document(document);
            },
            parameters => parameters.Add(component => component.Part, BpmnEditorPart.ContextMenu),
            canvas: true);
        if (liveSnapshot) session!.DescribeOverride = null;
        await menu.InvokeAsync(() => session!.HandleContextMenuRequestedAsync(
            new ContextMenuRequested("element", "Task_1",
                new Anchor(new Bounds(0, 0, 100, 80), new Bounds(10, 10, 100, 80)), "pointer", ["Task_1"])));
        return (menu, session!);
    }

    private static BpmnElementSnapshot Snapshot() => new()
    {
        ElementId = "Task_1",
        ElementType = "task",
        Category = BpmnElementCategory.Activity,
        Label = "Work",
        Capabilities = BpmnElementCapabilities.None with { CanSwitchType = true },
        SwitchableTypes =
        [
            new(BpmnFlowNodeType.Task, BpmnEventDefinitionType.None, "Task", true),
            new(BpmnFlowNodeType.ServiceTask, BpmnEventDefinitionType.None, "Service", false),
        ],
    };
}
