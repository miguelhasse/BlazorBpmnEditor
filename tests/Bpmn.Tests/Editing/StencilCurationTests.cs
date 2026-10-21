using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Editing;

/// <summary>
/// A host curating the palette (specification sections 9.1 to 9.3).
/// </summary>
/// <remarks>
/// Curation runs after the rules, never instead of them. Section 9.3 makes availability a
/// consequence of the container and the connection rules, so the thing these tests are really
/// protecting is that no configuration can put a tool back that the rules took away.
/// </remarks>
public sealed class StencilCurationTests
{
    [Fact]
    public void NoCurationLeavesThePaletteExactlyAsTheRulesProducedIt()
    {
        var groups = Groups();

        Assert.Same(groups, BpmnStencilCuration.Apply(groups, null));
        Assert.Same(groups, BpmnStencilCuration.Apply(groups, BpmnStencilOptions.Default));
    }

    [Fact]
    public void HidesAnEntryByItsKey()
    {
        var curated = BpmnStencilCuration.Apply(Groups(), new BpmnStencilOptions(
            new Dictionary<string, BpmnStencilEntryOptions>(StringComparer.Ordinal)
            {
                ["flowNode/task"] = new(IsVisible: false),
            }));

        Assert.DoesNotContain(Entries(curated), entry => entry.Key == "flowNode/task");
        Assert.Contains(Entries(curated), entry => entry.Key == "flowNode/subProcess");
    }

    [Fact]
    public void RenamesWithoutLosingTheEntry()
    {
        // The key deliberately excludes the label, because renaming is one of the things curation
        // is for and a key containing the label would stop matching the moment it was used.
        var curated = BpmnStencilCuration.Apply(Groups(), new BpmnStencilOptions(
            new Dictionary<string, BpmnStencilEntryOptions>(StringComparer.Ordinal)
            {
                ["flowNode/task"] = new(Label: "Human step", Description: "Someone has to do this one"),
            }));

        var renamed = Assert.Single(Entries(curated), entry => entry.Key == "flowNode/task");

        Assert.Equal("Human step", renamed.Label);
        Assert.Equal("Someone has to do this one", renamed.Description);
    }

    [Fact]
    public void DropsAGroupLeftEmptyByHiding()
    {
        // A heading with nothing under it is worse than no heading.
        var gateways = Entries(Groups()).Where(entry => entry.Key.Contains("Gateway", StringComparison.Ordinal));

        var curated = BpmnStencilCuration.Apply(Groups(), new BpmnStencilOptions(
            gateways.ToDictionary(entry => entry.Key, _ => new BpmnStencilEntryOptions(IsVisible: false), StringComparer.Ordinal)));

        Assert.DoesNotContain(curated, group => group.Title == "Gateways");
    }

    [Fact]
    public void HidesAWholeGroup()
    {
        var curated = BpmnStencilCuration.Apply(Groups(), new BpmnStencilOptions(HiddenGroups: ["Gateways"]));

        Assert.DoesNotContain(curated, group => group.Title == "Gateways");
        Assert.Contains(curated, group => group.Title == "Activities");
    }

    // --- order -------------------------------------------------------------------------

    [Fact]
    public void PutsNamedGroupsFirstAndLeavesTheRestAlone()
    {
        var curated = BpmnStencilCuration.Apply(Groups(), new BpmnStencilOptions(GroupOrder: ["Gateways", "Activities"]));

        Assert.Equal("Gateways", curated[0].Title);
        Assert.Equal("Activities", curated[1].Title);

        // Everything unnamed keeps the order the rules produced, after the named ones.
        Assert.Equal(Groups().Count, curated.Count);
    }

    [Fact]
    public void PinsAnEntryToTheTopOfItsGroup()
    {
        var curated = BpmnStencilCuration.Apply(Groups(), new BpmnStencilOptions(
            new Dictionary<string, BpmnStencilEntryOptions>(StringComparer.Ordinal)
            {
                ["flowNode/subProcess"] = new(Order: 0),
            }));

        var activities = Assert.Single(curated, group => group.Title == "Activities");

        Assert.Equal("flowNode/subProcess", activities.Entries[0].Key);
    }

    // --- what curation may not do -------------------------------------------------------

    [Fact]
    public void CannotEnableAToolTheRulesDisabled()
    {
        /*
         * The guarantee this whole design exists to keep. Section 9.3 makes availability a
         * consequence of the container and the connection rules; a host that could switch a tool
         * back on would be able to configure its way to a document the editor would then refuse to
         * create — or worse, create and fail to validate.
         */
        // An Event Sub-Process is the container that disables one: its Start Event must be typed,
        // so the untyped entry is offered greyed out rather than hidden (EV-004).
        var groups = BpmnStencil.GroupsFor(WithEventSubProcess(), "EventSub_1");
        var disabled = Entries(groups).FirstOrDefault(entry => !entry.IsEnabled);

        Assert.NotNull(disabled);

        var curated = BpmnStencilCuration.Apply(groups, new BpmnStencilOptions(
            new Dictionary<string, BpmnStencilEntryOptions>(StringComparer.Ordinal)
            {
                [disabled.Key] = new(Label: "Try me", Description: "Definitely allowed"),
            }));

        var still = Assert.Single(Entries(curated), entry => entry.Key == disabled.Key);

        Assert.False(still.IsEnabled);
        Assert.Equal(disabled.DisabledReason, still.DisabledReason);
    }

    [Fact]
    public void CannotAddAToolTheRulesDidNotOffer()
    {
        // Curation names entries; it does not describe them. A key nothing produced matches nothing.
        var curated = BpmnStencilCuration.Apply(Groups(), new BpmnStencilOptions(
            new Dictionary<string, BpmnStencilEntryOptions>(StringComparer.Ordinal)
            {
                ["flowNode/somethingInvented"] = new(Label: "Invented"),
            }));

        Assert.DoesNotContain(Entries(curated), entry => entry.Label == "Invented");
        Assert.Equal(Entries(Groups()).Count, Entries(curated).Count);
    }

    [Fact]
    public void KeysAreStableAndUnique()
    {
        // Curation is keyed by them, so two entries sharing a key would be indistinguishable and a
        // key that changed with the label would stop matching after the first rename.
        var keys = Entries(Groups()).Select(entry => entry.Key).ToList();

        Assert.Equal(keys.Count, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.All(keys, key => Assert.DoesNotContain(' ', key));
    }

    // --- helpers -----------------------------------------------------------------------

    private static IReadOnlyList<BpmnStencilEntry> Entries(IReadOnlyList<BpmnStencilGroup> groups) =>
        [.. groups.SelectMany(group => group.Entries)];

    private static IReadOnlyList<BpmnStencilGroup> Groups() => BpmnStencil.GroupsFor(Document(), "Process_1");

    private static BpmnDocument WithEventSubProcess()
    {
        var document = new BpmnDocumentBuilder("Process_1")
            .Node("EventSub_1", BpmnFlowNodeType.EventSubProcess, new Bounds(100, 100, 400, 300), isExpanded: true)
            .Build();

        return document with
        {
            FlowNodes = [.. document.FlowNodes.Select(node => node.Id == "EventSub_1"
                ? node with { FlowElementsOwned = true }
                : node)],
        };
    }

    private static BpmnDocument Document() => new BpmnDocumentBuilder("Process_1")
        .Node("Start_1", BpmnFlowNodeType.StartEvent, new Bounds(160, 140, 36, 36))
        .Node("Task_1", BpmnFlowNodeType.Task, new Bounds(260, 120, 100, 80))
        .Build();
}
