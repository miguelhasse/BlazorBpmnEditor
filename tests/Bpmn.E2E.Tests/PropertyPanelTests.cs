using Hasseware.AspNetCore.Components.Bpmn.E2E.Tests.Fixtures;

using Microsoft.Playwright;
using Microsoft.Playwright.Xunit.v3;

namespace Hasseware.AspNetCore.Components.Bpmn.E2E.Tests;

/// <summary>
/// The property panel in a browser (implementation plan M6).
/// </summary>
/// <remarks>
/// <para>
/// What a bUnit test cannot show: that an edit made in the panel reaches the document and comes
/// back out as a redrawn label, and that the panel re-describes the element afterwards instead of
/// going on showing what was typed.
/// </para>
/// <para>
/// The demo keeps the panel off by default, so each test switches it on. That is not a convenience
/// — the panel's column is a fixed width, and docking it permanently on a small window pushes the
/// right edge of a wide Pool off the paper.
/// </para>
/// </remarks>
public class PropertyPanelTests(DemoAppFixture app) : PageTest, IClassFixture<DemoAppFixture>
{
    [Fact]
    public async Task ShowsNothingUntilSomethingIsSelected()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();

        await Expect(Page.Locator(".bpmn-properties__empty")).ToContainTextAsync("Select an element");
    }

    [Fact]
    public async Task DescribesTheSelectedElement()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectAsync(await TaskIdAsync());

        await Expect(Page.Locator(".bpmn-properties__subtitle")).ToHaveTextAsync("User Task");
        // An input since safe rename arrived, so the id is a value rather than text content.
        await Expect(Page.Textbox("Element id")).ToHaveValueAsync(await TaskIdAsync());

        // Contextual, not one form for everything: an Activity has a loop characteristic and a
        // compensation flag, and a Start Event has neither.
        await Expect(Page.Locator("[data-group-id=general] .bpmn-properties__label")).ToHaveTextAsync(["Name", "Documentation"]);
        await Expect(Page.Locator("[data-group-id=activity] .bpmn-properties__label")).ToHaveTextAsync(["Compensation handler"]);
        await Expect(Page.Locator("[data-group-id=loop-characteristics] .bpmn-properties__label")).ToHaveTextAsync(["Loop"]);
    }

    [Fact]
    public async Task PropertyControlsAndSectionSeparatorsStayInsideThePanel()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectAsync(await TaskIdAsync());

        var layout = await Page.EvaluateAsync<double[]>(
            """
            () => {
              const panel = document.querySelector('.bpmn-properties');
              const panelBounds = panel.getBoundingClientRect();
              const rows = [...panel.querySelectorAll('.bpmn-properties__property')];
              const controls = rows.map(row => {
                const label = row.querySelector('.bpmn-properties__label');
                const control = row.querySelector('input, textarea, select, fluent-text-input, fluent-text-area, fluent-select');
                return label && control
                  ? [control.getBoundingClientRect().right - panelBounds.right,
                     label.getBoundingClientRect().bottom - control.getBoundingClientRect().top]
                  : [0, 0];
              });
              const widths = [...panel.querySelectorAll('.bpmn-properties__group')]
                .map(group => group.getBoundingClientRect().width);
              return [
                panel.scrollWidth - panel.clientWidth,
                Math.max(...controls.map(([right]) => right)),
                Math.max(...controls.map(([, labelGap]) => labelGap)),
                Math.max(...widths) - Math.min(...widths),
                ...widths,
              ];
            }
            """);

        Assert.True(layout[0] <= 1, $"Panel has {layout[0]}px horizontal overflow.");
        Assert.True(layout[1] <= 1, $"A property control exceeds the panel by {layout[1]}px.");
        Assert.True(layout[2] <= 1, "A property label is not above its control.");
        var sections = await Page.EvaluateAsync<string>(
            """
            () => JSON.stringify([...document.querySelectorAll('.bpmn-properties__group')].map(group => ({
              tag: group.tagName,
              width: group.getBoundingClientRect().width,
              parentTag: group.parentElement.tagName,
              parentWidth: group.parentElement.getBoundingClientRect().width,
              rootMatches: group.matches('.fluent-bpmn-editor .bpmn-properties > fluent-accordion > fluent-accordion-item'),
              style: group.getAttribute('style'),
              parentDisplay: getComputedStyle(group.parentElement).display,
              parentFlexDirection: getComputedStyle(group.parentElement).flexDirection,
              display: getComputedStyle(group).display,
              cssWidth: getComputedStyle(group).width,
              alignSelf: getComputedStyle(group).alignSelf,
            })))
            """);
        Assert.True(layout[3] <= 1,
            $"Property section separators differ in width by {layout[3]}px: {string.Join(", ", layout.Skip(4))}. {sections}");
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "OrderSubmittedWithAnUnbrokenMessageNameThatMustNotWidenThePropertyControl")]
    [InlineData(true, "OrderSubmittedWithAnUnbrokenMessageNameThatMustNotWidenThePropertyControl")]
    public async Task MessageReferenceControlsStayInsideTheirSurface(bool contextMenu, string messageName)
    {
        await GoToEditorAsync();
        Assert.True(Page.Context.Browser?.IsConnected);
        await ShowPanelAsync();
        var taskId = await TaskIdAsync();
        var task = Page.Locator($".joint-cell[model-id='{taskId}'] [joint-selector='body']").First;
        await task.ClickAsync(new() { Button = MouseButton.Right });
        await Page.PickAsync(Page.Locator(".bpmn-menu__type-select"), "Send Task");
        await Page.Keyboard.PressAsync("Escape");
        await SelectAsync(taskId);
        await Page.Locator(".bpmn-properties [data-message-action=new]").ClickAsync();
        var dialog = Page.Locator(".bpmn-message-catalog");
        await dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" }).FillAsync(messageName);
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        if (contextMenu)
        {
            await task.ClickAsync(new() { Button = MouseButton.Right });
        }

        var surface = Page.Locator(contextMenu ? ".bpmn-menu" : ".bpmn-properties");
        var reference = surface.Locator(".bpmn-message-reference");
        var selectedName = string.IsNullOrEmpty(messageName) ? "Unnamed message" : messageName;
        await Expect(reference.GetByRole(AriaRole.Combobox)).ToContainTextAsync(selectedName);
        await Page.PickAsync(reference.Locator("[class$=__select]"), 1);
        var layout = await reference.EvaluateAsync<double[]>(
            """
            reference => {
              const row = reference.closest('label');
              const bounds = row.getBoundingClientRect();
              const elements = [];
              const visit = root => {
                for (const element of root.children) {
                  if (element.getBoundingClientRect().width > 0) elements.push(element);
                  visit(element);
                  if (element.shadowRoot) visit(element.shadowRoot);
                }
              };
              visit(reference);
              const controls = elements.filter(element => element.matches(
                'select, fluent-select, fluent-dropdown, button, fluent-button, [part=control]'));
              return [
                row.scrollWidth - row.clientWidth,
                Math.max(...controls.map(element => element.getBoundingClientRect().right - bounds.right)),
                Math.max(...controls.map(element => bounds.left - element.getBoundingClientRect().left)),
              ];
            }
            """);
        Assert.True(layout[0] <= 1, $"Message row has {layout[0]}px horizontal overflow.");
        Assert.True(layout[1] <= 1, $"Message control exceeds the right edge by {layout[1]}px.");
        Assert.True(layout[2] <= 1, $"Message control exceeds the left edge by {layout[2]}px.");
        Assert.True(await surface.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth + 1"),
            "Message surface has horizontal overflow.");
    }

    [Fact]
    public async Task AnEditInThePanelRedrawsTheLabel()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();

        var taskId = await TaskIdAsync();
        await SelectAsync(taskId);

        await Page.Locator(".bpmn-properties__property", new() { HasTextString = "Name" })
            .Locator("input").First.FillAsync("Renamed in the panel");
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(900);

        // Joined across the tspans the wrapper produced: the drawn text has a line break where the
        // label ran out of width, so the element's raw textContent has no space at that point.
        Assert.Equal("Renamed in the panel", await DrawnLabelAsync(taskId));
    }

    [Fact]
    public async Task LoopAndComplexBehaviorPropertiesHaveSeparateSemanticSections()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();
        const string xml = """
            <definitions xmlns="http://www.omg.org/spec/BPMN/20100524/MODEL"
                         xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
                         xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
                         id="Definitions_Groups" targetNamespace="urn:test">
              <process id="Process_Groups">
                <task id="Task_Groups" name="Grouped task">
                  <multiInstanceLoopCharacteristics behavior="Complex">
                    <loopCardinality>4</loopCardinality>
                    <complexBehaviorDefinition><condition>first</condition></complexBehaviorDefinition>
                    <complexBehaviorDefinition><condition>second</condition></complexBehaviorDefinition>
                  </multiInstanceLoopCharacteristics>
                </task>
              </process>
              <bpmndi:BPMNDiagram id="Diagram_Groups">
                <bpmndi:BPMNPlane id="Plane_Groups" bpmnElement="Process_Groups">
                  <bpmndi:BPMNShape id="Shape_Groups" bpmnElement="Task_Groups">
                    <dc:Bounds x="160" y="120" width="100" height="80" />
                  </bpmndi:BPMNShape>
                </bpmndi:BPMNPlane>
              </bpmndi:BPMNDiagram>
            </definitions>
            """;
        await using var transfer = await Page.EvaluateHandleAsync(
            """
            xml => {
                const transfer = new DataTransfer();
                transfer.items.add(new File([xml], 'groups.bpmn', { type: 'application/xml' }));
                return transfer;
            }
            """, xml);
        await Page.Locator(".bpmn-canvas").First.DispatchEventAsync("drop", new Dictionary<string, object> { ["dataTransfer"] = transfer });
        await Page.ImportAccept().ClickAsync();
        await Expect(Page.Locator(".joint-cell[model-id=Task_Groups]")).ToBeVisibleAsync();
        await SelectAsync("Task_Groups");

        var loops = Page.GetByRole(AriaRole.Group, new() { Name = "Loop Characteristics", Exact = true });
        var complex = Page.GetByRole(AriaRole.Group, new() { Name = "Complex Behavior Definition", Exact = true });
        await Expect(loops).ToBeVisibleAsync();
        await Expect(complex).ToBeVisibleAsync();
        await Expect(loops.GetByRole(AriaRole.Textbox, new() { Name = "Loop cardinality", Exact = true })).ToHaveValueAsync("4");
        await Expect(loops.GetByRole(AriaRole.Textbox, new() { Name = "Complex behavior 1 condition", Exact = true })).ToHaveCountAsync(0);
        var first = complex.GetByRole(AriaRole.Textbox, new() { Name = "Complex behavior 1 condition", Exact = true });
        var second = complex.GetByRole(AriaRole.Textbox, new() { Name = "Complex behavior 2 condition", Exact = true });
        await Expect(first).ToHaveValueAsync("first");
        await Expect(second).ToHaveValueAsync("second");
        await second.FillAsync("updated");
        await second.PressAsync("Tab");
        await Expect(second).ToHaveValueAsync("updated");
        await Expect(first).ToHaveValueAsync("first");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(second).ToHaveValueAsync("second");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo", Exact = true }).ClickAsync();
        await Expect(second).ToHaveValueAsync("updated");
    }

    [Fact]
    public async Task ScriptTaskPropertiesAreEditableAndUndoable()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();
        var taskId = await TaskIdAsync();
        var body = Page.Locator($".joint-cell[model-id='{taskId}'] [joint-selector='body']").First;
        await body.ClickAsync(new() { Button = MouseButton.Right });
        await Page.PickAsync(Page.Locator(".bpmn-menu__type-select"), "Script Task");
        await Page.Keyboard.PressAsync("Escape");
        await SelectAsync(taskId);

        var panel = Page.Locator("[data-group-id=script-task]");
        var format = panel.GetByRole(AriaRole.Textbox, new() { Name = "Script format", Exact = true });
        var script = panel.GetByRole(AriaRole.Textbox, new() { Name = "Script", Exact = true });
        await Expect(format).ToHaveValueAsync("");
        await Expect(script).ToHaveValueAsync("");
        await Expect(script).ToHaveJSPropertyAsync("tagName", "TEXTAREA");

        await format.FillAsync("text/javascript");
        await format.PressAsync("Tab");
        await Expect(format).ToHaveValueAsync("text/javascript");
        const string source = "  var x = 1;\n  return x < 2;\n";
        await script.FillAsync(source);
        await script.PressAsync("Tab");
        await Expect(script).ToHaveValueAsync(source);

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo", Exact = true }).ClickAsync();
        await Expect(script).ToHaveValueAsync("");
        await Expect(format).ToHaveValueAsync("text/javascript");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Redo", Exact = true }).ClickAsync();
        await Expect(script).ToHaveValueAsync(source);

        await script.FillAsync("");
        await script.PressAsync("Tab");
        await format.FillAsync("");
        await format.PressAsync("Tab");
        await Expect(script).ToHaveValueAsync("");
        await Expect(format).ToHaveValueAsync("");
    }

    [Fact]
    public async Task DocumentationIsEditable()
    {
        // The one field every element carries, and the one the property schema had no entry for
        // until this milestone.
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectAsync(await TaskIdAsync());

        var box = Page.Locator(".bpmn-properties__property", new() { HasTextString = "Documentation" }).Locator("textarea").First;

        await box.FillAsync("Checked by a human.");
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(900);

        // Re-described from the document that came back, not left showing what was typed.
        await Expect(box).ToHaveValueAsync("Checked by a human.");
    }

    [Fact]
    public async Task ASelectionOffersOnlyWhatItsElementsShare()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();

        await Page.Locator(".bpmn-canvas__paper").First.ClickAsync();
        await Page.Keyboard.PressAsync("Control+a");
        await Page.WaitForTimeoutAsync(900);

        // A Start Event, a Task and an End Event have a name and documentation in common and
        // nothing else; offering a Task's loop characteristic for the Events would offer to set a
        // property they have never heard of.
        await Expect(Page.Locator(".bpmn-properties__title")).ToContainTextAsync("3 elements");
        await Expect(Page.Locator(".bpmn-properties__label")).ToHaveTextAsync(["Name", "Documentation"]);
    }

    [Fact]
    public async Task ABulkEditIsASingleUndo()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();

        await Page.Locator(".bpmn-canvas__paper").First.ClickAsync();
        await Page.Keyboard.PressAsync("Control+a");
        await Page.WaitForTimeoutAsync(900);

        await Page.Locator(".bpmn-properties__property", new() { HasTextString = "Documentation" })
            .Locator("textarea").First.FillAsync("Reviewed together.");
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(1000);

        // Three elements changed by one command, so one undo takes all three back — which is what
        // makes it a bulk edit rather than three edits that happened to be typed once.
        await Expect(Page.Locator(".demo-edit-status")).ToContainTextAsync("Edited:");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Page.WaitForTimeoutAsync(900);

        await Expect(Page.Locator(".bpmn-properties__property", new() { HasTextString = "Documentation" })
            .Locator("textarea").First).ToHaveValueAsync(string.Empty);
    }

    [Fact]
    public async Task NamesTheExtensionContentItCarries()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectSampleAsync("zeebe-process.bpmn");
        await SelectAsync("ServiceTask_ChargeCard");

        // Named with their namespace, because section 6.1.2 is what gives extension content its
        // identity and two vendors' `properties` elements are otherwise indistinguishable.
        await Expect(Page.Locator(".bpmn-properties__extension")).ToHaveCountAsync(3);
        await Expect(Page.Locator(".bpmn-properties__extension").First).ToContainTextAsync("taskDefinition");
        await Expect(Page.Locator(".bpmn-properties__namespace").First).ToContainTextAsync("camunda.org/schema/zeebe");
    }

    [Fact]
    public async Task ExtensionValuesAreEditableButNeverStandardFields()
    {
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectSampleAsync("zeebe-process.bpmn");
        await SelectAsync("ServiceTask_ChargeCard");

        // Vendor fields stay in visibly namespaced groups, separate from standard BPMN entries.
        await Expect(Page.Locator("[data-group-id=general] .bpmn-properties__label")).ToHaveTextAsync(["Name", "Documentation"]);
        await Expect(Page.Locator("[data-group-id='task-definition'] input").First).ToHaveValueAsync("charge-card");

        await Page.Locator("[data-group-id='task-definition'] input").First.FillAsync("charge-card-v2");
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(1000);

        // Re-described from the document that came back, not left showing what was typed.
        await Expect(Page.Locator("[data-group-id='task-definition'] input").First).ToHaveValueAsync("charge-card-v2");
    }

    [Fact]
    public async Task EditingOneOfTwoSameNamedSiblingsLeavesTheOtherAlone()
    {
        /*
         * The reason the path addresses children by index. A zeebe:ioMapping holds two
         * zeebe:input elements, and a scheme that addressed them by name would edit whichever came
         * first — silently, and only ever that one.
         */
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectSampleAsync("zeebe-process.bpmn");
        await SelectAsync("ServiceTask_ChargeCard");

        await Expect(Page.Textbox("Target, row 1").First).ToHaveValueAsync("orderId");
        await Expect(Page.Textbox("Target, row 2").First).ToHaveValueAsync("amount");

        await Page.Textbox("Target, row 2").First.FillAsync("totalAmount");
        await Page.Keyboard.PressAsync("Tab");
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Textbox("Target, row 2").First).ToHaveValueAsync("totalAmount");
        await Expect(Page.Textbox("Target, row 1").First).ToHaveValueAsync("orderId");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Undo" }).ClickAsync();
        await Page.WaitForTimeoutAsync(1000);

        await Expect(Page.Textbox("Target, row 2").First).ToHaveValueAsync("amount");
    }

    [Fact]
    public async Task ASelectionIsOfferedNoExtensionValues()
    {
        // A path means something different on the next element, so there is no honest way to offer
        // one for several at once.
        await GoToEditorAsync();
        await ShowPanelAsync();
        await SelectSampleAsync("zeebe-process.bpmn");

        await Page.Locator(".bpmn-canvas__paper").First.ClickAsync();
        await Page.Keyboard.PressAsync("Control+a");
        await Page.WaitForTimeoutAsync(900);

        await Expect(Page.Locator(".bpmn-properties__extension")).ToHaveCountAsync(0);
        await Expect(Page.Locator(".bpmn-properties__label")).ToHaveTextAsync(["Name", "Documentation"]);
    }

    // --- helpers ------------------------------------------------------------------

    [Theory]
    [InlineData("Send Task")]
    [InlineData("Receive Task")]
    public async Task CreatesMessageFromTaskAndPersistsRootDefinition(string taskType)
    {
        await GoToEditorAsync();
        Assert.True(Page.Context.Browser?.IsConnected);
        await ShowPanelAsync();
        var taskId = await TaskIdAsync();
        await Page.Locator($".joint-cell[model-id='{taskId}'] [joint-selector='body']").First
            .ClickAsync(new() { Button = MouseButton.Right });
        await Page.PickAsync(Page.Locator(".bpmn-menu__type-select"), taskType);
        await Page.Keyboard.PressAsync("Escape");
        await SelectAsync(taskId);
        await Page.Locator(".bpmn-message-reference [data-message-action=new]").ClickAsync();
        var dialog = Page.Locator(".bpmn-message-catalog");
        await Expect(dialog).ToBeVisibleAsync();
        await Expect(dialog.Locator("[data-message-action=save]")).ToBeVisibleAsync();
        await dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" }).FillAsync("OrderSubmitted");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();
        Assert.True(await Page.Locator(".bpmn-message-reference").EvaluateAsync<bool>(
            "element => element === document.activeElement || element.contains(document.activeElement)"),
            await Page.EvaluateAsync<string>("() => document.activeElement?.outerHTML ?? 'none'"));
        await Expect(Page.Locator(".bpmn-message-reference").GetByRole(AriaRole.Combobox))
            .ToContainTextAsync("OrderSubmitted");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Messages", Exact = true }).ClickAsync();
        await Expect(dialog.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true })).ToBeDisabledAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Edit name", Exact = true }).ClickAsync();
        await Expect(dialog).ToContainTextAsync("This definition is shared");
        await dialog.GetByRole(AriaRole.Textbox, new() { Name = "Message name (optional)" }).FillAsync("OrderSubmittedV2");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        await Expect(dialog).ToBeHiddenAsync();

        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Export XML", Exact = true }).ClickAsync();
        var xml = await Page.Locator(".demo-export-xml").TextContentAsync() ?? throw new InvalidOperationException("XML export is empty.");
        var saved = Serialization.BpmnXmlReader.Read(xml).Document;
        var message = Assert.Single(saved.Catalog.Messages);
        Assert.Equal("OrderSubmittedV2", message.Name);
        Assert.True(message.Id == saved.FindFlowNode(taskId)!.MessageRef, xml);
        Assert.DoesNotContain(saved.Diagrams.SelectMany(diagram => diagram.Plane.Shapes), shape => shape.BpmnElement == message.Id);
        await Page.Locator(".demo-exported > summary").DispatchEventAsync("click");
        var transfer = await Page.EvaluateHandleAsync("""
            xml => {
              const transfer = new DataTransfer();
              transfer.items.add(new File([xml], 'messages.bpmn', { type: 'application/xml' }));
              return transfer;
            }
            """, xml);
        await Page.Locator(".bpmn-canvas").DispatchEventAsync("drop", new Dictionary<string, object> { ["dataTransfer"] = transfer });
        await Page.ImportAccept().ClickAsync();
        await Expect(Page.Locator(".bpmn-import")).ToHaveCountAsync(0);
        await Page.Locator(".bpmn-a11y-tree__item", new() { HasTextString = "Review Request" }).FocusAsync();
        await Expect(Page.Locator(".bpmn-message-reference").GetByRole(AriaRole.Combobox)).ToContainTextAsync("OrderSubmittedV2");
    }

    /// <summary>The label as drawn, with the wrapper's line breaks turned back into spaces.</summary>
    private Task<string> DrawnLabelAsync(string modelId) => Page.EvaluateAsync<string>(
        $$"""
        () => {
          const cell = document.querySelector(".joint-cell[model-id='{{modelId}}']");
          if (cell === null) return '';
          const spans = [...cell.querySelectorAll('tspan')].map((t) => t.textContent?.trim() ?? '');
          return spans.filter((line) => line.length > 0).join(' ');
        }
        """);

    private Task<string> TaskIdAsync() => Page.EvaluateAsync<string>(
        """
        () => [...document.querySelectorAll('.bpmn-canvas .joint-element[model-id]')]
          .find((c) => (c.getAttribute('model-id') ?? '').startsWith('Task_'))
          ?.getAttribute('model-id') ?? ''
        """);

    private async Task SelectAsync(string modelId)
    {
        await Page.Locator($".joint-cell[model-id='{modelId}'] [joint-selector='body']").First.ClickAsync();
        await Page.WaitForTimeoutAsync(800);
    }

    private async Task ShowPanelAsync()
    {
        await Page.GetByRole(AriaRole.Switch, new() { Name = "Properties", Exact = true }).CheckAsync();
        await Expect(Page.Locator(".bpmn-properties")).ToBeVisibleAsync();
    }

    private async Task SelectSampleAsync(string fileName)
    {
        await Page.OpenDemoFilesAsync();
        await Page.GetByRole(AriaRole.Combobox, new() { Name = "Sample diagram" }).ClickAsync();
        await Page.Locator("fluent-option", new() { HasTextString = fileName }).ClickAsync();
        await Page.WaitForTimeoutAsync(1800);
    }

    private async Task GoToEditorAsync()
    {
        await Page.GotoAsync(app.Url(this));

        await Expect(Page.Locator(".bpmn-canvas__paper > svg")).ToBeVisibleAsync(new() { Timeout = 30_000 });
        await Expect(Page.Locator(".bpmn-canvas .joint-cell").First).ToBeVisibleAsync();
    }
}
