using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// Creating elements and connectors, in a real browser.
/// </summary>
/// <remarks>
/// <para>
/// The commands are unit-tested against documents and the rules against endpoint facts; neither
/// sees a pointer. What only a browser can show is that the gesture reaches the command at all —
/// and every bug this file was written alongside was exactly that: a handle that never rendered, a
/// pan that swallowed the drag, a provisional link that cancelled the selection it was started
/// from. Each of those left the unit tests green.
/// </para>
/// <para>
/// A drag here is a real mouse. Synthetic pointer events reach the listeners but not JointJS's
/// move machinery, which only runs for trusted events.
/// </para>
/// </remarks>
public class StencilTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task SubProcessStencilShowsACollapsedMarkerInsteadOfAnExpandedBorder()
    {
        await GoToEditorAsync();
        var tool = Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool",
            new() { HasTextString = "Sub-Process" });
        await Expect(tool).ToBeVisibleAsync();

        var detail = tool.Locator(".bpmn-stencil__silhouette").Nth(1);
        await Expect(detail).ToHaveAttributeAsync("d", "M 10 12 h 4 v 4 h -4 Z M 12 13 v 2 M 11 14 h 2");
    }

    [Fact]
    public async Task EveryToolInAProcessIsUsable()
    {
        await GoToEditorAsync();

        await Expect(Page.Locator(".bpmn-stencil")).ToHaveAttributeAsync("role", "listbox");
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool")).ToHaveCountAsync(12);
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__label")).ToHaveTextAsync(
        [
            "Start Event",
            "Intermediate / Boundary Event",
            "End Event",
            "Task",
            "Sub-Process",
            "Call Activity",
            "Gateway",
            "Data Object Reference",
            "Data Store Reference",
            "Pool / Lane",
            "Text Annotation",
            "Group",
        ]);

        var disabled = Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool--disabled");
        await Expect(disabled).ToHaveCountAsync(0);

        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool[draggable='false']")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool", new() { HasTextString = "User Task" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ReadOnlyOffersNoStencil()
    {
        await Page.GotoAsync($"{app.BaseUrl}/gallery");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync(new() { Timeout = 30_000 });

        // Section 7: in read-only every mutation surface is gone, not merely inert.
        await Expect(Page.Locator(".bpmn-stencil")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DraggingAToolPreviewsTheIconAndNotTheWholeRow()
    {
        /*
         * Left alone the browser drags a snapshot of whatever carries `draggable`, which here is
         * the whole tool row — so a label, its padding and its background travelled across the
         * canvas with the shape. What is being placed is the element, so that is the preview.
         *
         * Asserted by intercepting `setDragImage`, because the drag image itself is drawn by the
         * browser's compositor and is not in the DOM to be read.
         */
        await GoToEditorAsync();

        var captured = await Page.EvaluateAsync<string>(
            """
            () => {
              const tool = [...document.querySelectorAll('.bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool')]
                .find((t) => (t.textContent ?? '').includes('Task') && !t.classList.contains('bpmn-stencil__tool--disabled'));
              if (tool == null) return 'no tool';

              let seen = 'never called';
              const original = DataTransfer.prototype.setDragImage;
              DataTransfer.prototype.setDragImage = function (el, x, y) {
                seen = `${el.tagName}.${el.getAttribute('class')} at ${Math.round(x)},${Math.round(y)}`;
                return original.call(this, el, x, y);
              };

              try {
                tool.dispatchEvent(new DragEvent('dragstart', {
                  bubbles: true,
                  cancelable: true,
                  dataTransfer: new DataTransfer(),
                }));
              } finally {
                DataTransfer.prototype.setDragImage = original;
              }

              return seen;
            }
            """);

        Assert.Equal("svg.bpmn-stencil__icon at 14,14", captured);
    }

    [Fact]
    public async Task TheLabelStaysInTheRowItIsJustNotInThePreview()
    {
        // The label is how a user reads the palette and how every test in this file finds a tool.
        // Only the drag image changed.
        await GoToEditorAsync();

        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool", new() { HasTextString = "Task" })).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__label").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DroppingAToolCreatesAnElementWhereItWasReleased()
    {
        await GoToEditorAsync();
        var before = await CellCountAsync();

        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Task", x, y);

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");

        // The pointer marks the centre: offering the top-left would put a 100x80 Task almost
        // entirely below and right of where the user aimed.
        // The aim is interpolated rather than passed as an argument: an anonymous object arrives
        // with no readable members and every comparison against it comes back NaN.
        var offset = await Page.EvaluateAsync<double>(
            $$"""
            () => {
              const cells = [...document.querySelectorAll('.bpmn-canvas .joint-cell[model-id]')];
              return Math.min(...cells.map((c) => {
                const b = c.getBoundingClientRect();
                return Math.abs((b.x + b.width / 2) - {{Invariant(x)}}) + Math.abs((b.y + b.height / 2) - {{Invariant(y)}});
              }));
            }
            """);

        Assert.True(offset < 12, $"the nearest new shape was centred {offset}px from the release point");
    }

    [Fact]
    public async Task ACollidingDropIsRefusedAndLeavesNothingBehind()
    {
        await GoToEditorAsync();
        var before = await CellCountAsync();

        var victim = await Page.Locator(".joint-cell[model-id^='Task_']").First.BoundingBoxAsync();
        await DropToolAsync("Task", victim!.X + (victim.Width / 2), victim.Y + (victim.Height / 2));

        // Section 9.3: creation is one transaction, so a refusal leaves no semantic object and no
        // DI behind — not a shape with nothing to draw, and not a drawing with nothing behind it.
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("DI-011");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
    }

    [Fact]
    public async Task CreationIsUndoableAndRedoable()
    {
        await GoToEditorAsync();
        var before = await CellCountAsync();

        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Task", x, y);
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo" }).ClickAsync();
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);
    }

    [Fact]
    public async Task PressingEnterOnAToolCreatesItWithoutAPointer()
    {
        await GoToEditorAsync();
        var before = await CellCountAsync();

        // A palette that can only be dragged is unusable without a pointer, and pressing twice
        // must not stack the second element on the first and be refused by DI-011.
        await Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool", new() { HasText = "Gateway" }).First.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);

        await Page.Keyboard.PressAsync("Enter");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationOnNonRootDiagramPersistsInCorrectProcessAndPlane(bool keyboard)
    {
        await GoToEditorAsync();
        await SelectSampleAsync("linked-processes.bpmn");
        var rootCount = await CellCountAsync();
        await Page.PickAsync(Page.DiagramSelect(), "Handle Shipment Diagram");
        await Expect(Page.SelectedDiagram()).ToHaveAttributeAsync("value", "Diagram_HandleShipment");
        await Expect(Page.Locator(".joint-cell[model-id='Task_PackItems']")).ToBeVisibleAsync();
        var before = await CellCountAsync();
        var existingIds = await Page.Locator(".bpmn-canvas .joint-element").EvaluateAllAsync<string[]>(
            "elements => elements.map(element => element.getAttribute('model-id'))");

        if (keyboard)
        {
            await ActivateAsync("Task");
        }
        else
        {
            var (x, y) = await FreeSpotAsync();
            await DropToolAsync("Task", x, y);
        }

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);
        var currentIds = await Page.Locator(".bpmn-canvas .joint-element").EvaluateAllAsync<string[]>(
            "elements => elements.map(element => element.getAttribute('model-id'))");
        var createdId = Assert.Single(currentIds.Except(existingIds));

        await Page.PickAsync(Page.DiagramSelect(), "Main Process Diagram");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(rootCount);
        await Expect(Page.Locator($".joint-cell[model-id='{createdId}']")).ToHaveCountAsync(0);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Page.PickAsync(Page.DiagramSelect(), "Handle Shipment Diagram");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo", Exact = true }).ClickAsync();
        await Expect(Page.Locator($".joint-cell[model-id='{createdId}']")).ToBeVisibleAsync();

        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export XML", Exact = true }).ClickAsync();
        var xml = XDocument.Parse(await Page.Locator(".demo-export-xml").InnerTextAsync());
        XNamespace model = "http://www.omg.org/spec/BPMN/20100524/MODEL";
        XNamespace diagram = "http://www.omg.org/spec/BPMN/20100524/DI";
        var node = Assert.Single(xml.Descendants(model + "task"), element => (string?)element.Attribute("id") == createdId);
        Assert.Equal("Process_HandleShipment", (string?)node.Parent!.Attribute("id"));
        var shape = Assert.Single(xml.Descendants(diagram + "BPMNShape"),
            element => (string?)element.Attribute("bpmnElement") == createdId);
        Assert.Equal("Diagram_HandleShipment", (string?)shape.Parent!.Parent!.Attribute("id"));

    }

    // --- the section 9.1 entries that are not Flow Nodes --------------------------

    [Fact]
    public async Task OffersOnlyTheGenericEventAndPoolLaneTools()
    {
        await GoToEditorAsync();

        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool", new() { HasTextString = "Intermediate / Boundary Event" })).ToHaveCountAsync(1);
        var poolLane = Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool", new() { HasTextString = "Pool / Lane" });
        await Expect(poolLane).ToHaveCountAsync(1);
        await Expect(poolLane).ToBeEnabledAsync();
        await Expect(poolLane).ToHaveAttributeAsync("draggable", "true");

        await SelectSampleAsync("collaboration.bpmn");

        Assert.DoesNotContain(
            "bpmn-stencil__tool--disabled",
            await poolLane.GetAttributeAsync("class") ?? string.Empty);
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool", new() { HasTextString = "Intermediate / Boundary Event" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task CreatingALaneDrawsItAndASecondOneSharesTheStack()
    {
        await GoToEditorAsync();
        await Page.SetViewportSizeAsync(1280, 900);
        await SelectSampleAsync("pizza-store.bpmn");
        await SelectInsideAPoolAsync();

        var task = await Page.Locator(".bpmn-canvas .joint-element[aria-label*='Task' i] [joint-selector='body']")
            .First.BoundingBoxAsync();
        Assert.NotNull(task);
        await DropToolAsync("Pool / Lane", task.X + (task.Width / 2), task.Y + (task.Height / 2));
        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id^='Lane']")).ToHaveCountAsync(1);

        // A second activation adds another Lane to the same Pool stack.
        await ActivateAsync("Pool / Lane");
        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id^='Lane']")).ToHaveCountAsync(2);
        await Page.WaitForTimeoutAsync(800);
        var stacked = await Page.EvaluateAsync<bool>(
            """
            () => {
              const boxes = [...document.querySelectorAll(".bpmn-canvas .joint-cell[model-id^='Lane']")]
                .map((c) => c.getBoundingClientRect())
                .sort((a, b) => a.y - b.y);
              // Section 1.5.3: the Lanes tile, so the second starts where the first ends.
              return Math.abs(boxes[0].y + boxes[0].height - boxes[1].y) < 2;
            }
            """);

        Assert.True(stacked, "the two Lanes did not tile the stack");
    }

    [Fact]
    public async Task DroppingPoolLaneOutsideAPoolCreatesAPool()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("collaboration.bpmn");
        for (var step = 0; step < 6; step++)
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = "Zoom out" }).ClickAsync();
        }

        var before = await Page.EvaluateAsync<int>(POOL_COUNT_JS);

        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Pool / Lane", x, y);

        Assert.Equal(before + 1, await Page.EvaluateAsync<int>(POOL_COUNT_JS));
    }

    [Fact]
    public async Task IntermediateBoundaryToolCreatesIntermediateOrBoundaryByDropTarget()
    {
        await GoToEditorAsync();

        var task = await Page.Locator(".joint-cell[model-id^='Task_'] [joint-selector='body']").First.BoundingBoxAsync();
        await DropToolAsync("Intermediate / Boundary Event", task!.X + (task.Width / 2), task.Y + (task.Height / 2));

        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id^='boundaryEvent']")).ToHaveCountAsync(1);
        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id^='intermediateCatchEvent']")).ToHaveCountAsync(0);

        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Intermediate / Boundary Event", x, y);

        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id^='intermediateThrowEvent']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task CallActivityToolOpensPickerAndCreatesCallActivityForChosenDefinition()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("linked-processes.bpmn");

        var before = await Page.Locator(".bpmn-canvas .joint-cell[model-id^='callActivity_']").CountAsync();
        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Call Activity", x, y);

        var picker = Page.GetByRole(AriaRole.Dialog);
        await Expect(picker).ToBeVisibleAsync();
        if (KitRoute.For(this) == KitRoute.Fluent)
        {
            await Page.Locator("fluent-dialog.bpmn-picker").ExpectSingleFluentSurfaceAsync();
            await Page.Keyboard.PressAsync("Escape");
            await Expect(picker).ToHaveCountAsync(0);
            await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id^='callActivity_']")).ToHaveCountAsync(before);
            await Page.Locator(".bpmn-canvas").ClickAsync(new() { Position = new() { X = 4, Y = 4 } });
            await Expect(Page.Locator(".bpmn-canvas")).ToBeFocusedAsync();

            (x, y) = await FreeSpotAsync();
            await DropToolAsync("Call Activity", x, y);
            await Expect(picker).ToBeVisibleAsync();
        }
        await picker.GetByRole(AriaRole.Option, new() { Name = "Process_HandleShipment" }).ClickAsync();

        await Expect(picker).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-canvas .joint-cell[model-id^='callActivity_']")).ToHaveCountAsync(before + 1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
    }

    /// <summary>Counts the drawn Pools, which is what a Participant renders as.</summary>
    private const string POOL_COUNT_JS = "() => [...document.querySelectorAll('.bpmn-canvas .joint-cell[model-id]')].filter((c) => (c.getAttribute('aria-label') ?? '').startsWith('Pool')).length";

    /// <summary>
    /// Selects something inside a Pool, which is what makes that Pool's Process the container the
    /// stencil is computed for.
    /// </summary>
    /// <remarks>
    /// A Task rather than the Pool's header band. Either resolves to the same Process, but a Task
    /// is unambiguously itself — a Pool's left edge is also where its Lanes draw their own bands,
    /// so a click there depends on which of them is on top.
    /// </remarks>
    private async Task SelectInsideAPoolAsync()
    {
        var id = await Page.EvaluateAsync<string>(
            """
            () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
              .find((c) => (c.getAttribute('aria-label') ?? '').toLowerCase().includes('task'))
              ?.getAttribute('model-id') ?? ''
            """);

        Assert.NotEmpty(id);

        var box = await Page.Locator($".joint-cell[model-id='{id}'] [joint-selector='body']").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException($"'{id}' has no drawn body");

        await Page.Mouse.ClickAsync(box.X + (box.Width / 2), box.Y + (box.Height / 2));
        await Page.WaitForTimeoutAsync(800);
    }

    /// <summary>Arms a stencil tool from the keyboard, which creates without needing a drag.</summary>
    private async Task ActivateAsync(string label)
    {
        await Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool", new() { HasTextString = label }).First.FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Page.WaitForTimeoutAsync(1200);
    }

    [Fact]
    public async Task SelectingAnElementOffersAConnectHandleThatDrawsAFlow()
    {
        await GoToEditorAsync();
        var links = await LinkCountAsync();

        // A second Task, so there is a legal pair that is not already connected.
        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Task", x, y);
        var source = await Page.Locator(".joint-cell[model-id^='task_']").First.GetAttributeAsync("model-id");

        await SelectAsync(source!);
        await Expect(Page.Locator(".bpmn-connect-handle")).ToHaveCountAsync(1);

        await DragHandleToAsync(await EndEventIdAsync());

        await Expect(Page.Locator(".bpmn-canvas .joint-link")).ToHaveCountAsync(links + 1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
    }

    [Fact]
    public async Task AnIllegalTargetIsGreyedOutDuringTheDragAndRefusedOnRelease()
    {
        await GoToEditorAsync();

        var (x, y) = await FreeSpotAsync();
        await DropToolAsync("Task", x, y);
        var source = await Page.Locator(".joint-cell[model-id^='task_']").First.GetAttributeAsync("model-id");
        var start = await Page.Locator(".joint-cell[model-id^='Event_']").First.GetAttributeAsync("model-id");

        await SelectAsync(source!);
        var links = await LinkCountAsync();

        await DragHandleToAsync(start!, release: false);

        // Section 7: an illegal target must never look droppable, so the refusal is visible while
        // the pointer is still down rather than only after the release.
        var opacity = await Page.EvaluateAsync<string>(
            "id => getComputedStyle(document.querySelector(`.joint-element[model-id=\"${id}\"]`)).opacity",
            start!);
        Assert.True(double.Parse(opacity, System.Globalization.CultureInfo.InvariantCulture) < 0.9, $"opacity was {opacity}");

        await Page.Mouse.UpAsync();

        // SF-003: a Start Event takes no incoming Sequence Flow. The refusal names the rule rather
        // than the gesture simply vanishing, which reads as the editor having missed it.
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("SF-003");
        await Expect(Page.Locator(".bpmn-canvas .joint-link")).ToHaveCountAsync(links);
        await Expect(Page.Locator(".joint-paper.bpmn-connecting")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TheCollaborationPlaneOffersNoFlowNodesUntilAPoolIsChosen()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("collaboration.bpmn");

        // The Collaboration plane offers Pool/Lane, Text Annotation and Group; flow nodes belong to a Process.
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group")).ToHaveCountAsync(2);
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__title")).ToHaveTextAsync(["Swimlanes", "Artifacts"]);
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool")).ToHaveCountAsync(3);

        var pool = await Page.Locator(".joint-cell[model-id^='Pool_']").First.GetAttributeAsync("model-id");
        await SelectPoolHeaderAsync(pool!);

        // Choosing a Pool is what makes its Process the container being created into. Without
        // this the palette on every collaboration diagram would be permanently empty.
        Assert.True(
            await Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool:not(.bpmn-stencil__tool--disabled)").CountAsync() == 12,
            "selecting a Pool did not open the palette");
    }

    [Fact]
    public async Task DroppingIntoAPoolPutsTheElementInThatPoolsProcess()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("collaboration.bpmn");

        var pool = await Page.Locator(".joint-cell[model-id^='Pool_']").First.GetAttributeAsync("model-id");
        await SelectPoolHeaderAsync(pool!);

        var before = await CellCountAsync();
        var box = await Page.Locator($".joint-cell[model-id='{pool}']").BoundingBoxAsync();

        // Well inside the Pool but clear of its contents, which sit along the top band.
        await DropToolAsync("Task", box!.X + box.Width - 120, box.Y + box.Height - 40);

        await Expect(Page.Locator(".bpmn-canvas .joint-cell")).ToHaveCountAsync(before + 1);
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited");
    }

    [Fact]
    public async Task SelectingALaneOffersThePaletteOfItsProcess()
    {
        await GoToEditorAsync();
        await Page.SetViewportSizeAsync(1600, 1400);
        await SelectSampleAsync("pizza-store.bpmn");

        // A Lane covers most of a Pool's area, so on a lane diagram it is the easiest thing to
        // click. Leaving it unrecognised emptied the palette on exactly those diagrams.
        var lane = await Page.EvaluateAsync<string>(
            "() => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')].find((c) => (c.getAttribute('aria-label') ?? '').startsWith('Lane'))?.getAttribute('model-id') ?? ''");

        Assert.NotEqual(string.Empty, lane);

        // Fit first: this fixture is taller than the test viewport, and a click at coordinates
        // below the window lands nowhere at all, which looks exactly like a selection bug.
        await Page.GetByRole(AriaRole.Button, new() { Name = "Fit" }).ClickAsync();
        await Page.WaitForTimeoutAsync(600);

        var box = await Page.Locator($".joint-cell[model-id='{lane}']").BoundingBoxAsync();
        var viewport = Page.ViewportSize!;
        Assert.True(
            box!.Y + 12 < viewport.Height,
            $"the lane is outside the {viewport.Width}x{viewport.Height} viewport at y={box.Y}");

        // Near the top of the lane's own band, inside its label strip and clear of its contents.
        await Page.Mouse.ClickAsync(box.X + 14, box.Y + 12);
        await Page.WaitForTimeoutAsync(500);

        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Selected");
        Assert.True(
            await Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool").CountAsync() == 12,
            "selecting a Lane did not open the palette");
    }

    [Fact]
    public async Task SelectingACollapsedSubProcessDoesNotOfferToCreateInsideIt()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("nested-subprocesses.bpmn");

        var sub = await Page.EvaluateAsync<string>(
            "() => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')].find((c) => (c.getAttribute('aria-label') ?? '').includes('Sub Process'))?.getAttribute('model-id') ?? ''");

        Assert.NotEqual(string.Empty, sub);

        var box = await BodyBoxAsync(sub);
        await Page.Mouse.ClickAsync(box.X + 8, box.Y + 8);
        await Page.WaitForTimeoutAsync(500);

        // Both Sub-Processes in this fixture are collapsed. A collapsed one renders as an opaque
        // leaf with no children drawn, so the palette must be its container's — the plane's
        // Process — rather than an invitation to create somewhere nothing can be seen.
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Selected");
        Assert.True(
            await Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool").CountAsync() == 12,
            "the palette should still offer the containing Process");
    }

    [Fact]
    public async Task EveryDrawnShapeCarriesAnAccessibleName()
    {
        await GoToEditorAsync();
        await SelectSampleAsync("pizza-store.bpmn");

        // Labelling only what was in the viewport when the first render finished left six of this
        // fixture's twenty-four shapes nameless: a screen reader walks the DOM, not the visible
        // rectangle, and nothing re-labels a shape when the viewport changes.
        var unnamed = await Page.EvaluateAsync<int>(
            "() => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id], .bpmn-canvas .joint-link[model-id]')].filter((c) => !c.hasAttribute('aria-label')).length");

        Assert.Equal(0, unnamed);
    }

    // --- helpers --------------------------------------------------------------

    /// <summary>A number a browser will parse, whatever the test machine's locale is.</summary>
    private static string Invariant(float value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// Selects a Pool by its header band.
    /// </summary>
    /// <remarks>
    /// A Pool's bounding-box centre is occupied by its contents, so clicking there selects a child.
    /// </remarks>
    private async Task SelectPoolHeaderAsync(string poolId)
    {
        var box = await Page.Locator($".joint-cell[model-id='{poolId}']").BoundingBoxAsync()
            ?? throw new InvalidOperationException($"no bounding box for '{poolId}'");

        await Page.Mouse.ClickAsync(box.X + 14, box.Y + (box.Height / 2));
        await Page.WaitForTimeoutAsync(500);
    }

    private async Task SelectSampleAsync(string fileName)
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
        await Page.WaitForTimeoutAsync(1500);
    }

    private Task<int> CellCountAsync() => Page.Locator(".bpmn-canvas .joint-cell[model-id]").CountAsync();

    private Task<int> LinkCountAsync() => Page.Locator(".bpmn-canvas .joint-link[model-id]").CountAsync();

    private async Task<string> EndEventIdAsync() =>
        await Page.Locator(".joint-cell[model-id^='Event_']").Last.GetAttributeAsync("model-id")
        ?? throw new InvalidOperationException("no Event element on the canvas");

    /// <summary>A point on the canvas with nothing already on it.</summary>
    private async Task<(float X, float Y)> FreeSpotAsync()
    {
        var canvas = await Page.Locator(".bpmn-canvas").BoundingBoxAsync()
            ?? throw new InvalidOperationException("the canvas has no bounding box");

        return (canvas.X + canvas.Width - 180, canvas.Y + canvas.Height - 150);
    }

    /// <summary>Drags a stencil tool onto the canvas and releases it.</summary>
    private async Task DropToolAsync(string label, float x, float y)
    {
        await Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool", new() { HasText = label }).First.HoverAsync();
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(x, y, new() { Steps = 8 });
        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(600);
    }

    /// <summary>
    /// Selects a shape by clicking the drawn body.
    /// </summary>
    /// <remarks>
    /// The cell's group bounding box includes the label underneath the shape, so its centre can
    /// land below the shape on blank paper and select nothing.
    /// </remarks>
    private async Task SelectAsync(string modelId)
    {
        var body = await BodyBoxAsync(modelId);
        await Page.Mouse.ClickAsync(body.X + (body.Width / 2), body.Y + (body.Height / 2));
        await Page.WaitForTimeoutAsync(400);
    }

    private async Task DragHandleToAsync(string targetId, bool release = true)
    {
        var handle = await Page.Locator(".bpmn-connect-handle").First.BoundingBoxAsync()
            ?? throw new InvalidOperationException("the selected element offered no connect handle");

        var target = await BodyBoxAsync(targetId);

        await Page.Mouse.MoveAsync(handle.X + (handle.Width / 2), handle.Y + (handle.Height / 2));
        await Page.Mouse.DownAsync();
        await Page.Mouse.MoveAsync(handle.X + 30, handle.Y, new() { Steps = 3 });
        await Page.Mouse.MoveAsync(target.X + (target.Width / 2), target.Y + (target.Height / 2), new() { Steps = 10 });

        if (!release) return;

        await Page.Mouse.UpAsync();
        await Page.WaitForTimeoutAsync(700);
    }

    private async Task<LocatorBoundingBoxResult> BodyBoxAsync(string modelId) =>
        await Page
            .Locator($".joint-cell[model-id='{modelId}'] [joint-selector='body'], .joint-cell[model-id='{modelId}'] [joint-selector='outerRing']")
            .First.BoundingBoxAsync()
        ?? throw new InvalidOperationException($"no drawn body for '{modelId}'");

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
        await Expect(Page.Locator(".bpmn-stencil > .bpmn-stencil__group .bpmn-stencil__tool").First).ToBeVisibleAsync();
    }
}
