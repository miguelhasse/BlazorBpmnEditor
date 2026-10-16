using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Validation;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>
/// The property panel (implementation plan M6).
/// </summary>
/// <remarks>
/// The panel binds a snapshot and nothing else, so these tests give it snapshots directly. What
/// they are really about is the part a single-element surface never had to answer: what a panel
/// should offer when the selection is several elements that are not the same kind of thing.
/// </remarks>
public sealed class PropertyPanelTests : BunitContext
{
    [Fact]
    public void SaysSoWhenNothingIsSelected()
    {
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => { });

        Assert.Contains("Select an element", panel.Markup, StringComparison.Ordinal);
        Assert.Empty(panel.FindAll(".bpmn-properties__property"));
    }

    [Fact]
    public void ShowsWhatTheSnapshotSaysTheElementIs()
    {
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1", "serviceTask", label: "Charge card")]));

        Assert.Equal("Charge card", panel.Find(".bpmn-properties__title").TextContent.Trim());
        Assert.Equal("Service Task", panel.Find(".bpmn-properties__subtitle").TextContent.Trim());
        Assert.Equal("Task_1", panel.Find(".bpmn-properties__id").GetAttribute("value"));
    }

    [Fact]
    public void EditingOneElementSubmitsOneCommand()
    {
        BpmnEditCommand? submitted = null;

        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1", "task", properties: [Text("name", "Work")])])
            .OnCommand(command => submitted = command));

        panel.Find(".bpmn-properties__property .bpmn-properties__text").Change("Renamed");

        var update = Assert.IsType<UpdatePropertiesCommand>(submitted);
        Assert.Equal("Task_1", update.ElementId);
        Assert.Equal("name", Assert.Single(update.Values).Name);
        Assert.Equal("Renamed", Assert.Single(update.Values).Value);
    }

    [Fact]
    public void AnEmptyTextBoxMeansTheAttributeIsAbsent()
    {
        // Not present-and-empty: an empty `name=""` in the XML says something different from no
        // name at all, and it is not what clearing a box means.
        BpmnEditCommand? submitted = null;

        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1", "task", properties: [Text("name", "Work")])])
            .OnCommand(command => submitted = command));

        panel.Find(".bpmn-properties__property .bpmn-properties__text").Change(string.Empty);

        Assert.Null(Assert.Single(Assert.IsType<UpdatePropertiesCommand>(submitted).Values).Value);
    }

    // --- a selection of more than one --------------------------------------------------

    [Fact]
    public void OffersOnlyThePropertiesEveryElementUnderstands()
    {
        /*
         * A Task and a Sequence Flow both have a name; only the flow has a condition. Offering the
         * condition for the pair would offer to set a property on an element that has never heard
         * of it, and the editor would refuse it — after the user had already typed it.
         */
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots(
            [
                Snapshot("Task_1", "task", properties: [Text("name", "Work")]),
                Snapshot("Flow_1", "sequenceFlow", properties:
                [
                    Text("name", "Work"),
                    Text("conditionExpression", "x > 1"),
                ]),
            ]));

        var labels = panel.FindAll(".bpmn-properties__label").Select(element => element.TextContent.Trim());

        Assert.Equal(["Name"], labels);
    }

    [Fact]
    public void MatchesOnKindAsWellAsName()
    {
        // Two families can both call something `name` while one is a choice and the other free
        // text. Rendering a select built from the first element would offer the second values it
        // cannot take.
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots(
            [
                Snapshot("A", "task", properties: [Text("mode", "a")]),
                Snapshot("B", "task", properties:
                [
                    new BpmnPropertyDescriptor("mode", "Mode", BpmnPropertyKind.Choice, "a"),
                ]),
            ]));

        Assert.Empty(panel.FindAll(".bpmn-properties__property"));
        Assert.Contains("no properties in common", panel.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public void BlanksAValueTheSelectionDisagreesOn()
    {
        // Showing the first element's value for all of them is exactly the stale standard field
        // section 7 forbids — and the user would not know which one they were about to overwrite.
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots(
            [
                Snapshot("A", "task", properties: [Text("name", "Alpha")]),
                Snapshot("B", "task", properties: [Text("name", "Beta")]),
            ]));

        // Blazor omits the attribute entirely for a null value, which renders the same blank box.
        Assert.True(string.IsNullOrEmpty(panel.Find(".bpmn-properties__property .bpmn-properties__text").GetAttribute("value")));
    }

    [Fact]
    public void KeepsAValueTheSelectionAgreesOn()
    {
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots(
            [
                Snapshot("A", "task", properties: [Text("name", "Same")]),
                Snapshot("B", "task", properties: [Text("name", "Same")]),
            ]));

        Assert.Equal("Same", panel.Find(".bpmn-properties__property .bpmn-properties__text").GetAttribute("value"));
    }

    [Fact]
    public void OneReadOnlyElementLocksThePropertyForTheSelection()
    {
        // A bulk edit that silently skipped the locked ones would report success for work it did
        // not do.
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots(
            [
                Snapshot("A", "task", properties: [Text("name", "Same")]),
                Snapshot("B", "task", properties: [Text("name", "Same") with { IsReadOnly = true }]),
            ]));

        Assert.NotNull(panel.Find(".bpmn-properties__property .bpmn-properties__text").GetAttribute("readonly"));
    }

    [Fact]
    public void ABulkEditIsOneCommand()
    {
        // Setting three names is one decision and one undo, not three of each.
        BpmnEditCommand? submitted = null;

        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots(
            [
                Snapshot("A", "task", properties: [Text("name", "Same")]),
                Snapshot("B", "task", properties: [Text("name", "Same")]),
                Snapshot("C", "task", properties: [Text("name", "Same")]),
            ])
            .OnCommand(command => submitted = command));

        panel.Find(".bpmn-properties__property .bpmn-properties__text").Change("Renamed");

        var compound = Assert.IsType<CompoundCommand>(submitted);

        Assert.Equal(3, compound.Commands.Count);
        Assert.Equal(
            ["A", "B", "C"],
            compound.Commands.Cast<UpdatePropertiesCommand>().Select(command => command.ElementId));
    }

    // --- the surrounding facts ---------------------------------------------------------

    [Fact]
    public void ShowsTheDiagnosticsRaisedAgainstTheSelection()
    {
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1", "task")])
            .Diagnostics(
                [new BpmnDiagnostic("D009", BpmnSeverity.Warning, "Not reachable.", "Task_1")]));

        Assert.Equal("D009", panel.Find(".bpmn-properties__code").TextContent.Trim());
    }

    [Fact]
    public void NamesExtensionContentWithoutBindingIt()
    {
        /*
         * Section 7 forbids presenting extension content as a standard field and section 6.1 keeps
         * it opaque — but a panel that hides it leaves no way to tell a configured element from a
         * bare one, or to know that editing it carries something the editor does not understand.
         */
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots([Snapshot("Task_1", "serviceTask", properties: [Text("name", "Work")]) with
            {
                Extensions = [new BpmnExtensionSummary("http://camunda.org/schema/zeebe/1.0", "taskDefinition")],
            }]));

        Assert.Equal("taskDefinition", panel.Find(".bpmn-properties__extension code").TextContent.Trim());

        // Named, but not offered as something to edit.
        Assert.Equal(
            ["Name"],
            panel.FindAll(".bpmn-properties__label").Select(element => element.TextContent.Trim()));
    }

    [Fact]
    public void ReadonlyNeedsNoSeparateBranch()
    {
        // The snapshot arrives with every property read-only, so the same markup is an inspector.
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots(
                [Snapshot("Task_1", "task", properties: [Text("name", "Work") with { IsReadOnly = true }])]));

        Assert.NotNull(panel.Find(".bpmn-properties__property .bpmn-properties__text").GetAttribute("readonly"));
    }

    // --- extension properties ----------------------------------------------------------

    [Fact]
    public void OffersExtensionValuesThroughTheSameForm()
    {
        /*
         * A vendor's value behaves like any other in the form. What keeps section 7 satisfied is
         * not a different control but a different list and a heading between them: these are never
         * mixed into the standard fields.
         */
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots([Extended()]));

        var labels = panel.FindAll(".bpmn-properties__label").Select(element => element.TextContent.Trim());

        Assert.Equal(["Name", "taskDefinition @type"], labels);
        Assert.NotEmpty(panel.FindAll(".bpmn-properties__extension"));
    }

    [Fact]
    public void EditingAnExtensionValueSubmitsItsPath()
    {
        BpmnEditCommand? submitted = null;

        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots([Extended()])
            .OnCommand(command => submitted = command));

        // The second text box is the extension one; the first is the standard Name.
        panel.FindAll(".bpmn-properties__property .bpmn-properties__text")[1].Change("charge-card-v2");

        var update = Assert.IsType<UpdatePropertiesCommand>(submitted);
        var value = Assert.Single(update.Values);

        Assert.Equal("Task_1", update.ElementId);
        Assert.Equal("extension:0@type", value.Name);
        Assert.Equal("charge-card-v2", value.Value);
    }

    [Fact]
    public void ASelectionIsOfferedNoExtensionValuesAtAll()
    {
        /*
         * A path addresses content by child index inside one element's own tree, so the same path
         * means something different — or nothing — on the next element. Two elements that happen to
         * share a vendor's schema cannot be told apart from two that do not, and a bulk edit would
         * write into whatever each one had at that index.
         */
        var panel = this.RenderSeeded<BpmnPropertyPanel>(seed => seed
            .Snapshots([Extended(), Extended() with { ElementId = "Task_2" }]));

        Assert.Empty(panel.FindAll(".bpmn-properties__extension"));
        Assert.Equal(
            ["Name"],
            panel.FindAll(".bpmn-properties__label").Select(element => element.TextContent.Trim()));
    }

    private static BpmnElementSnapshot Extended() =>
        Snapshot("Task_1", "serviceTask", properties: [Text("name", "Charge card")]) with
        {
            Extensions = [new BpmnExtensionSummary("http://camunda.org/schema/zeebe/1.0", "taskDefinition")],
            ExtensionProperties =
            [
                new BpmnPropertyDescriptor(
                    "extension:0@type",
                    "taskDefinition @type",
                    BpmnPropertyKind.Text,
                    "charge-card"),
            ],
        };

    // --- helpers -----------------------------------------------------------------------

    private static BpmnPropertyDescriptor Text(string name, string? value) =>
        new(name, Capitalise(name), BpmnPropertyKind.Text, value);

    private static string Capitalise(string name) =>
        char.ToUpperInvariant(name[0]) + name[1..];

    // --- read-only ------------------------------------------------------------------

    [Fact]
    public void InspectsButDoesNotEditWhenTheEditorIsReadOnly()
    {
        /*
         * The id is the one field the panel renders itself rather than taking from the snapshot,
         * so it is the one the editor's read-only state does not reach on its own. A host should
         * not have to remember to pass it a second time: section 7 asks read-only to remove every
         * mutation surface, and an id field that still accepted a rename would offer one the
         * editor refuses with EDIT-002 afterwards.
         */
        var panel = RenderCascaded(readOnly: true);

        Assert.Equal("Task_1", panel.Find(".bpmn-properties__id").GetAttribute("value"));
        Assert.NotNull(panel.Find(".bpmn-properties__id").GetAttribute("readonly"));
    }

    [Fact]
    public void StaysEditableInAnEditorThatIsNot()
    {
        var panel = RenderCascaded(readOnly: false);

        Assert.Null(panel.Find(".bpmn-properties__id").GetAttribute("readonly"));
    }

    private IRenderedComponent<BpmnPropertyPanel> RenderCascaded(bool readOnly) =>
        this.RenderSeeded<BpmnPropertyPanel>(seed =>
        {
            seed.Snapshots([Snapshot("Task_1", "task")]);
            if (readOnly) seed.ReadOnly();
        });

    private static BpmnElementSnapshot Snapshot(
        string id,
        string type,
        string? label = null,
        IReadOnlyList<BpmnPropertyDescriptor>? properties = null) => new()
        {
            ElementId = id,
            ElementType = type,
            Category = BpmnElementCategory.Activity,
            Label = label,
            Properties = properties ?? [],
        };
}
