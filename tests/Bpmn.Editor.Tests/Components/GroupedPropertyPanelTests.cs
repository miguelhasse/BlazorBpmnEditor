using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>Grouped providers, templates, registry dispatch and bound property editors.</summary>
public sealed class GroupedPropertyPanelTests : BunitContext
{
    [Fact]
    public void SemanticSectionsRetainTemplateAndRawExtensionGroups()
    {
        var snapshot = Snapshot() with
        {
            Properties =
            [
                new("multiInstance.complexBehavior.0.event", "Complex behavior 1 event", BpmnPropertyKind.Text, null)
                    { Group = BpmnPropertyGroupKind.ComplexBehaviorDefinition },
                new("name", "Name", BpmnPropertyKind.Text, "Work"),
                new("loopCharacteristics", "Loop", BpmnPropertyKind.Choice, "none",
                    Choices: [new("none", "None")]) { Group = BpmnPropertyGroupKind.LoopCharacteristics },
                new("documentation", "Documentation", BpmnPropertyKind.MultilineText, "Notes"),
            ],
            ExtensionGroups = [new("template:custom", "Template caption", [new("custom", "Custom", BpmnPropertyEntryKind.Text)])
                { Namespace = "template" }],
            ExtensionProperties = [new("extension:0@type", "Type", BpmnPropertyKind.Text, "job")],
        };

        var panel = Panel([snapshot]);

        Assert.Equal(["general", "loop-characteristics", "complex-behavior-definition", "template:custom", "extensions"],
            panel.FindAll("details[data-group-id]").Select(element => element.GetAttribute("data-group-id")));
        Assert.Equal(["Name", "Documentation"], panel.FindAll("[data-group-id=general] .bpmn-properties__label").Select(element => element.TextContent));
        Assert.Equal("Loop Characteristics", panel.Find("[data-group-id=loop-characteristics] summary").TextContent.Trim());
        Assert.Equal("Complex Behavior Definition", panel.Find("[data-group-id=complex-behavior-definition] summary").TextContent.Trim());
        Assert.Equal("Template caption", panel.Find("[data-group-id='template:custom'] summary").TextContent.Trim());
        Assert.Equal(2, panel.FindAll("details.bpmn-properties__group--extension").Count);
        Assert.All(panel.FindAll("details[data-group-id]"), element => Assert.NotNull(element.GetAttribute("open")));
    }

    [Theory]
    [InlineData(BpmnPropertyGroupKind.SendTask, BpmnPropertyGroupKind.ReceiveTask, "general")]
    [InlineData(BpmnPropertyGroupKind.SendTask, BpmnPropertyGroupKind.SendTask, "send-task")]
    public void BulkPropertiesKeepAgreedClassificationAndFallbackForDifferentOwners(
        BpmnPropertyGroupKind firstGroup, BpmnPropertyGroupKind secondGroup, string expectedGroup)
    {
        var first = Snapshot() with
        {
            Properties = [new("messageRef", "Message", BpmnPropertyKind.Text, "Message_1") { Group = firstGroup }],
        };
        var second = first with
        {
            ElementId = "Task_2",
            Properties = [first.Properties[0] with { Group = secondGroup, Value = "Message_2" }],
        };
        BpmnEditCommand? command = null;
        var panel = Panel([first, second], configure: seed => seed.OnCommand(update => command = update));

        var input = panel.Find($"[data-group-id='{expectedGroup}'] input");
        Assert.Null(input.GetAttribute("value"));
        input.Change("Message_3");

        var compound = Assert.IsType<CompoundCommand>(command);
        Assert.Equal(["Task_1", "Task_2"], compound.Commands.Cast<UpdatePropertiesCommand>().Select(update => update.ElementId));
        Assert.All(compound.Commands, update =>
            Assert.Equal(new BpmnPropertyValue("messageRef", "Message_3"), Assert.Single(Assert.IsType<UpdatePropertiesCommand>(update).Values)));
    }

    [Fact]
    public void ClassifiedTextAreasKeepTheirCommandPropertyPath()
    {
        var snapshot = Snapshot() with
        {
            Properties = [new("multiInstance.complexBehavior.1.condition", "Complex behavior 2 condition", BpmnPropertyKind.MultilineText, "old")
                { Group = BpmnPropertyGroupKind.ComplexBehaviorDefinition }],
        };
        BpmnEditCommand? command = null;
        var panel = Panel([snapshot], configure: seed => seed.OnCommand(update => command = update));

        panel.Find("[data-group-id=complex-behavior-definition] textarea").Change("updated");

        var update = Assert.IsType<UpdatePropertiesCommand>(command);
        Assert.Equal("Task_1", update.ElementId);
        Assert.Equal(new BpmnPropertyValue("multiInstance.complexBehavior.1.condition", "updated"), Assert.Single(update.Values));
    }

    [Fact]
    public void StandardAndVendorGroupsStaySeparate()
    {
        var snapshot = Snapshot() with
        {
            Properties = [new("name", "Name", BpmnPropertyKind.Text, "Work")],
            ExtensionProperties = [new("extension:0@type", "Type", BpmnPropertyKind.Text, "job")],
        };
        var panel = Panel([snapshot]);

        Assert.Equal("General", panel.Find("details[data-group-id=general] summary").TextContent.Trim());
        Assert.NotNull(panel.Find("details[data-group-id=general]").GetAttribute("open"));
        Assert.Single(panel.FindAll("details.bpmn-properties__group--extension"));
        Assert.Equal(["Name", "Type"], panel.FindAll(".bpmn-properties__label").Select(e => e.TextContent));
    }

    [Fact]
    public void AProviderRunsAfterHigherPrioritiesAndCanChangeTheirGroups()
    {
        var order = new List<int>();
        var first = new Provider(1200, (_, groups) => { order.Add(1200); return groups; });
        var last = new Provider(500, (_, groups) =>
        {
            order.Add(500);
            Assert.Equal("general", Assert.Single(groups).Id);
            return [groups[0] with { Label = "Changed", Tooltip = "Help", OpenByDefault = false }];
        });
        var panel = Panel([Snapshot() with { Properties = [new("name", "Name", BpmnPropertyKind.Text, "Work")] }],
            p => p.Add(c => c.Providers, [last, new BpmnGeneralPropertiesProvider(), first]));

        Assert.All(order.Chunk(2), pass => Assert.Equal([1200, 500], pass));
        Assert.Equal("Changed", panel.Find("summary").TextContent.Trim());
        Assert.Equal("Help", panel.Find("summary").GetAttribute("title"));
        Assert.Null(panel.Find("details").GetAttribute("open"));
    }

    [Fact]
    public void DefaultCompositionIncludesOptionsAndDiProviders()
    {
        Services.Configure<BpmnOptions>(options => options.Properties.Providers.Add(Entries("option")));
        Services.AddSingleton<IBpmnPropertiesProvider>(Entries("service"));
        var panel = Panel([Snapshot()]);

        Assert.Equal(["option", "service"], panel.FindAll("details").Select(e => e.GetAttribute("data-group-id")));
    }

    [Fact]
    public async Task GroupTemplateRetainsTheResolvedEntryRendererAndWriteCallbacks()
    {
        BpmnPropertyEntryContext? captured = null;
        BpmnEditCommand? command = null;
        RenderFragment<BpmnPropertyEntryContext> entryTemplate = context => builder =>
        {
            captured = context;
            builder.AddContent(0, $"custom:{context.Entry.Label}");
        };
        RenderFragment<BpmnPropertyGroupContext> groupTemplate = group => builder =>
        {
            builder.OpenElement(0, "article");
            builder.AddAttribute(1, "class", "custom-group");
            builder.AddContent(2, group.EntryTemplate(new(group.Entries[0], default, default, false, 0)));
            builder.CloseElement();
        };
        var panel = Panel([Snapshot() with { Properties = [new("name", "Name", BpmnPropertyKind.Text, "Work")] }],
            p => p.Add(c => c.GroupTemplate, groupTemplate).Add(c => c.EntryTemplate, entryTemplate),
            seed => seed.OnCommand(update => command = update));

        Assert.Equal("custom:Name", panel.Find(".custom-group").TextContent);
        Assert.Empty(panel.FindAll("details"));
        Assert.NotNull(captured);
        await panel.InvokeAsync(() => captured.OnValueChanged.InvokeAsync("Renamed"));
        Assert.Equal("Renamed", Assert.Single(Assert.IsType<UpdatePropertiesCommand>(command).Values).Value);
        Assert.Equal(1, captured.Revision);
    }

    [Fact]
    public void EmptyTemplateHandlesNoSelectionAndNoVisibleEntries()
    {
        var panel = Panel([], p => p.Add(c => c.EmptyTemplate, builder => builder.AddContent(0, "Nothing here")));
        Assert.Contains("Nothing here", panel.Markup);
        panel = Panel([Snapshot()], p => p.Add(c => c.EmptyTemplate, builder => builder.AddContent(0, "Nothing here")));
        Assert.Contains("Nothing here", panel.Markup);
    }

    [Fact]
    public void RegistryResolvesExplicitKeyThenLastPredicateThenKindThenDefault()
    {
        var registry = BpmnStandardDefaults.Register(new BpmnPropertyEditorRegistry());
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text);
        Assert.Equal(typeof(BpmnTextEntry), registry.Resolve(entry));
        registry.AddEditor<BpmnNumberEntry>(BpmnPropertyEntryKind.Text);
        Assert.Equal(typeof(BpmnNumberEntry), registry.Resolve(entry));
        registry.AddEditor<BpmnCheckboxEntry>(_ => true);
        registry.AddEditor<BpmnSelectEntry>(_ => true);
        registry.AddEditor<BpmnReadOnlyEntry>(_ => false);
        Assert.Equal(typeof(BpmnSelectEntry), registry.Resolve(entry));
        registry.AddEditor<BpmnTextAreaEntry>("special");
        Assert.Equal(typeof(BpmnTextAreaEntry), registry.Resolve(entry with { EditorKey = "special" }));
        Assert.Equal(typeof(BpmnSelectEntry), registry.Resolve(entry with { EditorKey = "unknown" }));
    }

    [Fact]
    public void PanelUsesOptionsRegistryWithDynamicComponentContext()
    {
        Services.Configure<BpmnOptions>(options => options.Properties.Editors.AddEditor<BpmnNumberEntry>("number"));
        var entry = new BpmnPropertyEntry("count", "Count", BpmnPropertyEntryKind.Text, "42") { EditorKey = "number" };
        var panel = Panel(entry);
        Assert.Equal("number", panel.Find(".bpmn-properties__property input").GetAttribute("type"));
        Assert.Equal("42", panel.Find(".bpmn-properties__property input").GetAttribute("value"));
    }

    [Fact]
    public void BoundScalarEditsBypassTheLegacyCommandCallback()
    {
        BpmnEditCommand? command = null;
        var panel = Panel(Bound(), update => command = update);
        panel.Find(".bpmn-properties__property input").Change("changed");
        var edit = Assert.IsType<SetForeignAttributeCommand>(command);
        Assert.Equal("Task_1", edit.ElementId);
        Assert.Equal("changed", edit.Value);
    }

    [Fact]
    public void HiddenEntriesNeverReachCustomTemplatesOrEditors()
    {
        var rendered = new List<string>();
        RenderFragment<BpmnPropertyEntryContext> template = context => builder => rendered.Add(context.Entry.Id);
        var provider = new Provider(500, (_, _) => [new("group", "Group", [Bound() with { Kind = BpmnPropertyEntryKind.Hidden }, new("shown", "Shown", BpmnPropertyEntryKind.Text)])]);
        var panel = Panel([Snapshot()], p => p.Add(c => c.Providers, [provider]).Add(c => c.EntryTemplate, template));
        Assert.Equal(["shown"], rendered);
        var hidden = Render<BpmnPropertyEntryView>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            new(Bound() with { Kind = BpmnPropertyEntryKind.Hidden }, default, default, false, 0)));
        Assert.Empty(hidden.Markup);
    }

    [Fact]
    public void MultiSelectionNeverOffersBoundOrVendorEntries()
    {
        var provider = new Provider(500, (_, _) =>
        [
            new("vendor", "Vendor", [new("vendor-field", "Vendor field", BpmnPropertyEntryKind.Text)]) { Namespace = "urn:test" },
            new("bound", "Bound", [Bound()]),
        ]);
        var panel = Panel([Snapshot(), Snapshot() with { ElementId = "Task_2" }], p => p.Add(c => c.Providers, [provider]));
        Assert.Empty(panel.FindAll(".bpmn-properties__property"));
    }

    [Fact]
    public async Task CustomEntryCallbacksCannotBypassReadOnlyState()
    {
        BpmnPropertyEntryContext? context = null;
        var edits = 0;
        var panel = Panel([Snapshot()],
            p => p.Add(c => c.Providers, [Entries("group", Bound())]).Add(c => c.EntryTemplate, entry => builder => context = entry),
            seed => seed.ReadOnly().OnCommand(_ => edits++));
        Assert.NotNull(context);
        Assert.True(context.IsReadOnly);
        await panel.InvokeAsync(() => context.OnValueChanged.InvokeAsync("blocked"));
        await panel.InvokeAsync(() => context.OnRowsChanged.InvokeAsync([]));
        Assert.Equal(0, edits);
    }

    [Fact]
    public void DefaultEditorsShowHelpErrorAndPlaceholder()
    {
        var panel = Panel(Bound() with { Description = "Description", Error = "Invalid", Placeholder = "Enter value", Tooltip = "Tip" });
        Assert.Equal("Description", panel.Find(".bpmn-properties__description").TextContent);
        Assert.Equal("Invalid", panel.Find("[role=alert]").TextContent);
        Assert.Equal("Enter value", panel.Find(".bpmn-properties__property input").GetAttribute("placeholder"));
        Assert.Equal("Tip", panel.Find("label.bpmn-properties__property").GetAttribute("title"));
        Assert.Equal("true", panel.Find(".bpmn-properties__property input").GetAttribute("aria-invalid"));
    }

    [Fact]
    public void ListEditsReplaceRowsWithoutMutatingTheSnapshot()
    {
        var source = new Dictionary<string, string?> { ["name"] = "first" };
        BpmnEditCommand? submitted = null;
        var panel = Panel(Bound() with
        {
            Binding = new("camunda-collection", new Dictionary<string, string> { ["itemLocalName"] = "item" }),
            Kind = BpmnPropertyEntryKind.List,
            Columns = [new("name", "Name")],
            Rows = [source],
        }, edit => submitted = edit);
        panel.Find("td input").Change("changed");
        Assert.Equal("changed", Assert.Single(Assert.IsType<ReplaceCamundaCollectionCommand>(submitted).Rows)["name"]);
        Assert.Equal("first", source["name"]);
        // A refused edit resets the control from the unchanged authoritative snapshot.
        Assert.Equal("first", panel.Find("td input").GetAttribute("value"));
    }

    [Fact]
    public void ListSupportsAddingRemovingAndReorderingRows()
    {
        IReadOnlyList<IReadOnlyDictionary<string, string?>>? rows = null;
        const string uri = "urn:list-test";
        BpmnExtensionElement Item(string name) => new(uri, "item",
            new Dictionary<string, string> { ["name"] = name, ["opaque"] = name },
            [new(uri, name + "Child", new Dictionary<string, string>(), [])], name + " text");
        var extras = new BpmnElementExtras
        {
            Extensions = [new(uri, "collection", new Dictionary<string, string>(), [Item("first"), Item("second")])]
        };
        var path = BpmnExtensionPath.Parse($"{{{uri}}}collection");
        IReadOnlyList<BpmnExtensionElement> items = [];
        void Submit(IReadOnlyList<IReadOnlyDictionary<string, string?>> updated)
        {
            rows = updated;
            items = BpmnExtensionNavigator.ReadCollection(
                BpmnExtensionNavigator.ReplaceCollection(extras, path, uri, "item", updated), path, uri, "item");
        }
        var entry = Bound() with
        {
            Kind = BpmnPropertyEntryKind.List,
            Columns = [new("name", "Name"), new("$index", "Internal identity")],
            Rows = [new Dictionary<string, string?> { ["name"] = "first", ["$index"] = "0" }, new Dictionary<string, string?> { ["name"] = "second", ["$index"] = "1" }],
        };
        var view = Render<BpmnListEntry>(p => p.AddCascadingValue(StandardPropertyScope.Default).Add(c => c.Context,
            new(entry, default, EventCallback.Factory.Create<IReadOnlyList<IReadOnlyDictionary<string, string?>>>(this, Submit), false, 0)));
        Assert.DoesNotContain("Internal identity", view.Markup, StringComparison.Ordinal);
        Assert.Equal(2, view.FindAll("td input").Count);
        view.Find("[aria-label='Move row 2 up']").Click();
        Assert.Equal("second", rows![0]["name"]);
        Assert.Equal("1", rows[0]["$index"]);
        Assert.Equal("0", rows[1]["$index"]);
        Assert.Equal("second", items[0].Attributes["opaque"]);
        Assert.Equal("secondChild", Assert.Single(items[0].Children).LocalName);
        Assert.Equal("first", items[1].Attributes["opaque"]);
        view.Find("td input").Change("renamed");
        Assert.Equal("1", rows![0]["$index"]);
        view.Find(".bpmn-properties__add-row").Click();
        Assert.Equal(3, rows!.Count);
        Assert.Null(rows[2]["name"]);
        Assert.False(rows[2].ContainsKey("$index"));
        Assert.Empty(items[2].Children);
        Assert.Empty(items[2].Attributes);
        Assert.Null(items[2].Text);
        view.Find("[aria-label='Remove row 1']").Click();
        Assert.Equal(2, rows!.Count);
        Assert.Equal("first", rows[0]["name"]);
        Assert.Equal("0", rows[0]["$index"]);
        Assert.False(rows[1].ContainsKey("$index"));
        Assert.Equal("first", items[0].Attributes["opaque"]);
        Assert.Equal("firstChild", Assert.Single(items[0].Children).LocalName);
        Assert.Equal("first text", items[0].Text);
        Assert.Empty(items[1].Children);
        Assert.All(items, item => Assert.False(item.Attributes.ContainsKey("$index")));
    }

    [Fact]
    public void ReadOnlyListHasNoMutationButtons()
    {
        var panel = Panel(Bound() with
        {
            Kind = BpmnPropertyEntryKind.List,
            IsReadOnly = true,
            Columns = [new("name", "Name")],
            Rows = [new Dictionary<string, string?> { ["name"] = "value" }]
        });
        Assert.Empty(panel.FindAll(".bpmn-properties__list button"));
        Assert.NotNull(panel.Find("td input").GetAttribute("readonly"));
    }

    [Fact]
    public void AllLegacyDescriptorKindsMapWithoutLosingMetadata()
    {
        var choice = new BpmnPropertyChoice("one", "One");
        var snapshot = Snapshot() with
        {
            Properties =
        [
            new("text", "Text", BpmnPropertyKind.Text, "value", true),
            new("area", "Area", BpmnPropertyKind.MultilineText, "lines"),
            new("check", "Check", BpmnPropertyKind.Boolean, "true"),
            new("choice", "Choice", BpmnPropertyKind.Choice, "one", Choices: [choice]),
        ]
        };
        var group = Assert.Single(new BpmnGeneralPropertiesProvider().GetGroups(snapshot, []));
        Assert.Equal([BpmnPropertyEntryKind.Text, BpmnPropertyEntryKind.TextArea, BpmnPropertyEntryKind.Checkbox, BpmnPropertyEntryKind.Select], group.Entries.Select(entry => entry.Kind));
        Assert.Equal(snapshot.Properties.Select(property => property.Name), group.Entries.Select(entry => entry.Id));
        Assert.True(group.Entries[0].IsReadOnly);
        Assert.Equal(choice, Assert.Single(group.Entries[3].Choices));
        Assert.All(group.Entries, entry => Assert.Null(entry.Binding));
    }

    private IRenderedComponent<BpmnPropertyPanel> Panel(BpmnPropertyEntry entry, Action<BpmnEditCommand>? onCommand = null) =>
        Panel([Snapshot()], p => p.Add(c => c.Providers, [Entries("custom", entry)]), seed => seed.OnCommand(update => onCommand?.Invoke(update)));

    private IRenderedComponent<BpmnPropertyPanel> Panel(BpmnElementSnapshot[] snapshots,
        Action<ComponentParameterCollectionBuilder<BpmnPropertyPanel>>? parameters = null, Action<SeedBuilder>? configure = null) =>
        this.RenderSeeded<BpmnPropertyPanel>(seed => { seed.Snapshots(snapshots); configure?.Invoke(seed); }, parameters);

    private static BpmnPropertyEntry Bound() => new("value", "Value", BpmnPropertyEntryKind.Text, "original")
    { Binding = new("foreign-attribute", new Dictionary<string, string> { ["namespace"] = "urn:test", ["name"] = "value" }) };

    private static BpmnElementSnapshot Snapshot() => new()
    { ElementId = "Task_1", ElementType = "task", Category = BpmnElementCategory.Activity };

    private static Provider Entries(string id, params BpmnPropertyEntry[] entries) =>
        new(500, (_, groups) => [.. groups, new(id, id, entries.Length == 0 ? [new(id, id, BpmnPropertyEntryKind.Text)] : entries)]);

    private sealed record Provider(int Priority,
        Func<BpmnElementSnapshot, IReadOnlyList<BpmnPropertyGroup>, IReadOnlyList<BpmnPropertyGroup>> Compose) : IBpmnPropertiesProvider
    {
        public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups) => Compose(element, groups);
    }
}
