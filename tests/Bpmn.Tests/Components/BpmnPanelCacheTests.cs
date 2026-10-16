using System.Globalization;
using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Resources;
using Hasseware.AspNetCore.Components.Bpmn.Validation;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class BpmnPanelCacheTests : BunitContext
{
    public BpmnPanelCacheTests() => Services.AddBpmnComponents();

    [Fact]
    public async Task PropertyGroupsAndContextsReuseInputsAndInvalidateOnChanges()
    {
        var provider = new CountingProvider();
        Services.AddSingleton<IBpmnPropertiesProvider>(provider);
        var scope = Render<BpmnEditorScope>(p => p.AddChildContent<BpmnEditorComponentRenderTests.ProbeHost<PanelProbe>>());
        var probe = scope.FindComponent<PanelProbe>();
        var session = scope.Instance.Session;
        session.DescribeOverride = _ => Snapshot("Work");
        session.PropertyCommandOverride = _ => Task.FromResult<BpmnEditResult?>(null);
        await scope.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));
        var groups = probe.Instance.CurrentGroups;
        var context = probe.Instance.Context;
        var groupContext = probe.Instance.GroupContext;
        var count = provider.Count;
        await scope.InvokeAsync(() => session.HandleViewportChangedAsync(new(1.1, 0, 0)));
        probe.Render();
        Assert.Equal(count, provider.Count);
        Assert.Same(groups, probe.Instance.CurrentGroups);
        Assert.Same(context, probe.Instance.Context);
        Assert.Same(groupContext, probe.Instance.GroupContext);
        await probe.InvokeAsync(() => context.OnValueChanged.InvokeAsync("Refused"));
        Assert.Equal(count, provider.Count);
        Assert.NotSame(context, probe.Instance.Context);
        Assert.Equal(context.Revision + 1, probe.Instance.Context.Revision);
        Assert.Equal(context.OnValueChanged, probe.Instance.Context.OnValueChanged);
        session.DescribeOverride = _ => Snapshot("Changed snapshot");
        await scope.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));
        Assert.True(provider.Count > count);
        Assert.Equal("Changed snapshot", probe.Instance.Context.Entry.Value);
        var replacement = new CountingProvider();
        probe.Render(p => p.Add(c => c.Providers, [new BpmnGeneralPropertiesProvider(), replacement]));
        Assert.Equal(1, replacement.Count);
        Assert.NotSame(groups, probe.Instance.CurrentGroups);
        scope.Render(p => p.Add(c => c.IsReadOnly, true));
        Assert.True(probe.Instance.Context.IsReadOnly);
    }

    [Fact]
    public async Task CustomPropertyTemplateMayReadStateBeyondThePanelSlices()
    {
        var scope = Render<BpmnEditorScope>(p => p.AddChildContent<BpmnEditorComponentRenderTests.ProbeHost<PanelProbe>>());
        var probe = scope.FindComponent<PanelProbe>();
        var session = scope.Instance.Session;
        session.DescribeOverride = _ => Snapshot("Work");
        await scope.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));
        RenderFragment<BpmnPropertyGroupContext> template = _ => builder => builder.AddContent(0, session.State.Zoom);
        probe.Render(p => p.Add(c => c.GroupTemplate, template));
        await scope.InvokeAsync(() => session.HandleViewportChangedAsync(new(1.5, 0, 0)));
        Assert.Contains("1.5", probe.Markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderAndEditorRegistryMutationsRenderEvenOnUnrelatedStateChanges()
    {
        var scope = Render<BpmnEditorScope>(p => p.AddChildContent<BpmnEditorComponentRenderTests.ProbeHost<EditorPanelProbe>>());
        var probe = scope.FindComponent<EditorPanelProbe>();
        var session = scope.Instance.Session;
        session.DescribeOverride = _ => Snapshot("Work");
        await scope.InvokeAsync(() => session.SetSelectionAsync(["Task_1"]));
        var options = Services.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties;
        options.Providers.Add(new ExtraGroupProvider());
        await scope.InvokeAsync(() => session.HandleViewportChangedAsync(new(1.1, 0, 0)));
        Assert.Contains("extra", probe.Markup, StringComparison.Ordinal);
        session.Editors.AddEditor<CustomEntry>(BpmnPropertyEntryKind.Text);
        await scope.InvokeAsync(() => session.HandleViewportChangedAsync(new(1.2, 0, 0)));
        Assert.Equal(2, probe.FindComponents<CustomEntry>().Count);
    }

    [Fact]
    public async Task ValidationRowsCacheReportAndRelocalizeWhenCultureChanges()
    {
        var strings = new CountingRuleStrings();
        Services.AddSingleton<IStringLocalizer<BpmnRuleMessages>>(strings);
        var scope = Render<BpmnEditorScope>();
        var warning = new BpmnDiagnostic("warning", BpmnSeverity.Warning, "Warning");
        var error = new BpmnDiagnostic("error", BpmnSeverity.Error, "Error");
        var report = new BpmnValidationReport([warning, error]);
        var state = scope.Instance.Session.State with { Validation = report };
        var parent = Render<CascadingValue<BpmnEditorSession>>(p => p.Add(c => c.Value, scope.Instance.Session)
            .Add(c => c.IsFixed, true).AddChildContent<CascadingValue<BpmnEditorState>>(inner => inner
                .Add(c => c.Value, state).AddChildContent<ValidationProbe>()));
        var cascade = parent.FindComponent<CascadingValue<BpmnEditorState>>();
        var probe = parent.FindComponent<ValidationProbe>();
        Task Assign(BpmnEditorState next)
        {
            cascade.Render(p => p.Add(c => c.Value, next).AddChildContent<ValidationProbe>());
            return Task.CompletedTask;
        }
        await probe.InvokeAsync(() => Assign(state));
        Assert.Equal([error, warning], probe.Instance.Rows);
        var rows = probe.Instance.Rows;
        var count = strings.Count;
        await probe.InvokeAsync(() => Assign(state with { Zoom = 2 }));
        Assert.Same(rows, probe.Instance.Rows);
        Assert.Equal(count, strings.Count);
        await probe.InvokeAsync(() => Assign(state with { Validation = new([warning]) }));
        Assert.Single(probe.Instance.Rows);
        Assert.DoesNotContain(":error", probe.Markup, StringComparison.Ordinal);
        await probe.InvokeAsync(async () =>
        {
            var previous = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
                await Assign(state with { Zoom = 3 });
                Assert.Contains("de:error", probe.Markup, StringComparison.Ordinal);
                Assert.Contains("de:warning", probe.Markup, StringComparison.Ordinal);
                var localized = strings.Count;
                await Assign(state with { Zoom = 4 });
                Assert.Equal(localized, strings.Count);
                CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr");
                await Assign(state with { Zoom = 5 });
                Assert.Contains("fr:error", probe.Markup, StringComparison.Ordinal);
            }
            finally { CultureInfo.CurrentUICulture = previous; }
        });
    }

    [Fact]
    public void PartResolutionCacheTracksOptionsMutatedInPlace()
    {
        var options = Services.GetRequiredService<IOptions<BpmnOptions>>().Value.Components;
        options.Use<FirstToolbar>(BpmnEditorPart.Toolbar);
        var scope = TestScope.Render<BpmnPart>(this, p => p.Add(c => c.Part, BpmnEditorPart.Toolbar));
        Assert.Single(scope.Cut.FindComponents<FirstToolbar>());
        options.Use<SecondToolbar>(BpmnEditorPart.Toolbar);
        scope.Cut.Render();
        Assert.Empty(scope.Cut.FindComponents<FirstToolbar>());
        Assert.Single(scope.Cut.FindComponents<SecondToolbar>());
        options.Remove(BpmnEditorPart.Toolbar);
        scope.Cut.Render();
        Assert.Empty(scope.Cut.FindComponents<SecondToolbar>());
    }

    private static BpmnElementSnapshot Snapshot(string value) => new()
    {
        ElementId = "Task_1",
        ElementType = "task",
        Category = BpmnElementCategory.Activity,
        Properties = [new("name", "Name", BpmnPropertyKind.Text, value)],
    };

    private sealed class CountingProvider : IBpmnPropertiesProvider
    {
        public int Count { get; private set; }
        public int Priority => 500;
        public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups)
        { Count++; return groups; }
    }

    public sealed class PanelProbe : BpmnPropertyPanelBase
    {
        public IReadOnlyList<BpmnPropertyGroup> CurrentGroups => Groups;
        public BpmnPropertyEntryContext Context => BuildEntryContext(Groups[0].Entries[0]);
        public BpmnPropertyGroupContext GroupContext => BuildGroupContext(Groups[0]);
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Groups.Count == 0) return;
            if (GroupTemplate is { } template) builder.AddContent(0, template(GroupContext));
            else builder.AddContent(0, Context.Entry.Value);
        }
    }

    private sealed class ExtraGroupProvider : IBpmnPropertiesProvider
    {
        public IReadOnlyList<BpmnPropertyGroup> GetGroups(BpmnElementSnapshot element, IReadOnlyList<BpmnPropertyGroup> groups) =>
            [.. groups, new("extra", "Extra", [new("extra", "Extra", BpmnPropertyEntryKind.Text, "Extra value")])];
    }

    public sealed class EditorPanelProbe : BpmnPropertyPanelBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            foreach (var group in Groups)
            {
                builder.AddContent(0, group.Id);
                foreach (var entry in group.Entries) builder.AddContent(1, RenderEntry(BuildEntryContext(entry)));
            }
        }
    }

    public sealed class CustomEntry : ComponentBase
    {
        [Parameter] public BpmnPropertyEntryContext Context { get; set; } = default!;
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, Context.Entry.Value);
    }

    public sealed class ValidationProbe : BpmnValidationPanelBase
    {
        public IEnumerable<BpmnDiagnostic> Rows => Ordered;
        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            foreach (var diagnostic in Ordered) builder.AddContent(0, MessageOf(diagnostic));
        }
    }

    public sealed class FirstToolbar : BpmnToolbarBase;
    public sealed class SecondToolbar : BpmnToolbarBase;

    private sealed class CountingRuleStrings : IStringLocalizer<BpmnRuleMessages>
    {
        public int Count { get; private set; }
        public LocalizedString this[string name]
        { get { Count++; return new(name, $"{CultureInfo.CurrentUICulture.Name}:{name}"); } }
        public LocalizedString this[string name, params object[] arguments] => this[name];
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
