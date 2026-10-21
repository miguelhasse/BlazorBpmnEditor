using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

public sealed class PickerNavigationPathTests
{
    private static readonly string[] DiagramIds = ["Root", "Middle", "Leaf", "Detached"];

    [Fact]
    public async Task PickerMatchesDirectDrillDownPath()
    {
        var session = Session(Document(("Root", "Middle", "Call_Middle")));
        await session.GoToDiagramAsync("Root");
        await session.DrillIntoAsync("Call_Middle");
        var drilled = session.NavigationPath.ToArray();

        await session.GoToDiagramAsync("Middle");

        Assert.Equal(drilled, session.NavigationPath);
        Assert.Equal(
            [new BpmnDiagramStep("Root", "Root"), new BpmnDiagramStep("Middle", "Middle", "Call_Middle")],
            session.NavigationPath);
    }

    [Fact]
    public async Task PickerMatchesMultihopCallAndSubProcessDrillDownPath()
    {
        var document = Document(("Root", "Middle", "Call_Middle"), ("Middle", "Leaf", "Sub_Leaf"));
        document = document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "Sub_Leaf"
                ? node with { Type = BpmnFlowNodeType.SubProcess, CalledElementRef = null, FlowElementsOwned = true }
                : node)],
            Diagrams = [.. document.Diagrams.Select(diagram => diagram.Id == "Leaf"
                ? diagram with { Plane = diagram.Plane with { BpmnElement = "Sub_Leaf" } }
                : diagram)],
        };
        var session = Session(document);
        await session.GoToDiagramAsync("Root");
        await session.DrillIntoAsync("Call_Middle");
        await session.DrillIntoAsync("Sub_Leaf");
        var drilled = session.NavigationPath.ToArray();

        await session.GoToDiagramAsync("Leaf");

        Assert.Equal(drilled, session.NavigationPath);
        Assert.Equal(
            [new BpmnDiagramStep("Root", "Root"),
             new BpmnDiagramStep("Middle", "Middle", "Call_Middle"),
             new BpmnDiagramStep("Leaf", "Leaf", "Sub_Leaf")],
            session.NavigationPath);
    }

    [Fact]
    public async Task PickerUsesShortestRouteEvenWhenLongerRouteIsDeclaredFirst()
    {
        var session = Session(Document(
            ("Root", "Middle", "Call_Middle"),
            ("Middle", "Leaf", "Call_Leaf"),
            ("Root", "Leaf", "Direct_Leaf")));

        await session.GoToDiagramAsync("Leaf");

        Assert.Equal(
            [new BpmnDiagramStep("Root", "Root"), new BpmnDiagramStep("Leaf", "Leaf", "Direct_Leaf")],
            session.NavigationPath);
    }

    [Fact]
    public async Task PickerBreaksEqualLengthRouteTiesInDocumentOrder()
    {
        var session = Session(Document(
            ("Root", "Middle", "First"),
            ("Root", "Detached", "Second"),
            ("Middle", "Leaf", "First_Leaf"),
            ("Detached", "Leaf", "Second_Leaf")));

        await session.GoToDiagramAsync("Leaf");

        Assert.Equal(
            [new BpmnDiagramStep("Root", "Root"),
             new BpmnDiagramStep("Middle", "Middle", "First"),
             new BpmnDiagramStep("Leaf", "Leaf", "First_Leaf")],
            session.NavigationPath);
    }

    [Fact]
    public async Task PickerTerminatesCyclesAndKeepsUnreachableDiagramStandalone()
    {
        var session = Session(Document(
            ("Root", "Root", "Self"),
            ("Root", "Middle", "Call_Middle"),
            ("Middle", "Root", "Back")));

        await session.GoToDiagramAsync("Detached");

        Assert.Equal(new BpmnDiagramStep("Detached", "Detached"), Assert.Single(session.NavigationPath));
    }

    [Fact]
    public async Task PickerIgnoresCallsNotDrawnOnSourcePlane()
    {
        var document = Document(("Root", "Leaf", "Hidden_Call"));
        document = document with
        {
            Diagrams = [.. document.Diagrams.Select(diagram => diagram.Id == "Root"
                ? diagram with { Plane = diagram.Plane with { Shapes = [] } }
                : diagram)],
        };
        var session = Session(document);

        await session.GoToDiagramAsync("Leaf");

        Assert.Equal(new BpmnDiagramStep("Leaf", "Leaf"), Assert.Single(session.NavigationPath));
    }

    [Fact]
    public async Task PickerReturningToRootDiscardsPreviousRoute()
    {
        var session = Session(Document(("Root", "Middle", "Call_Middle")));
        await session.GoToDiagramAsync("Middle");
        var previous = session.NavigationPath;

        await session.GoToDiagramAsync("Root");

        Assert.Equal(new BpmnDiagramStep("Root", "Root"), Assert.Single(session.NavigationPath));
        Assert.Equal(2, previous.Count);
    }

    [Fact]
    public async Task PickerPublishesMatchingStateBeforeInvokingCallback()
    {
        var document = Document(("Root", "Middle", "Call_Middle"));
        var session = Session(document);
        var notifications = new List<BpmnDiagramNavigated>();
        session.OnDiagramNavigated = EventCallback.Factory.Create<BpmnDiagramNavigated>(new object(), notification =>
        {
            Assert.Equal(notification.Path, session.State.NavigationPath);
            Assert.Equal(notification.DiagramId, session.State.ActiveDiagramId);
            notifications.Add(notification);
        });

        await session.GoToDiagramAsync("Middle");

        var notification = Assert.Single(notifications);
        Assert.Equal(BpmnNavigationOrigin.Picker, notification.Origin);
        Assert.Equal(2, notification.Path.Count);
        Assert.Same(document, session.EditedDocument);
        Assert.False(session.CanUndo);

        await session.AscendToAsync("Root");

        Assert.Equal("Root", session.ActiveDiagramId);
        Assert.Single(session.NavigationPath);
    }

    [Fact]
    public async Task PickerWithoutDocumentKeepsStandalonePath()
    {
        var session = Session(null);

        await session.GoToDiagramAsync("Unknown");

        Assert.Equal(new BpmnDiagramStep("Unknown", "Unknown"), Assert.Single(session.NavigationPath));
    }

    private static BpmnEditorSession Session(BpmnDocument? document)
    {
        var session = new BpmnEditorSession(new BpmnOptions(), new ServiceCollection().BuildServiceProvider())
        {
            Document = document,
        };
        session.UpdateParameters(EditorConfig.CreateDefault());
        return session;
    }

    private static BpmnDocument Document(params (string Source, string Target, string ElementId)[] links)
    {
        var builder = new BpmnDocumentBuilder("Process_Root")
            .Process("Process_Middle")
            .Process("Process_Leaf")
            .Process("Process_Detached");
        foreach (var (Source, Target, ElementId) in links)
        {
            builder.Node(ElementId, BpmnFlowNodeType.CallActivity, new Bounds(100, 100, 100, 80),
                containerId: $"Process_{Source}");
        }

        var document = builder.Build();
        return document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node with
            {
                CalledElementRef = $"Process_{links.Single(link => link.ElementId == node.Id).Target}",
            })],
            Diagrams = [.. DiagramIds.Select(id =>
                new BpmnDiagram(id, new BpmnPlane($"Plane_{id}", $"Process_{id}",
                    [.. document.Diagrams[0].Plane.Shapes.Where(shape =>
                        links.Any(link => link.Source == id && link.ElementId == shape.BpmnElement))], []), id))],
        };
    }
}
