using System.Reflection;
using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Hasseware.AspNetCore.Components.Bpmn.Feel;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions.Templates;
using Hasseware.FluentUI.AspNetCore.Components.Bpmn;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class FluentKitTests : FluentTestContext
{
    public static TheoryData<Type> Entries =>
    [
        typeof(FluentBpmnTextEntry), typeof(FluentBpmnTextAreaEntry), typeof(FluentBpmnCheckboxEntry),
        typeof(FluentBpmnNumberEntry), typeof(FluentBpmnSelectEntry), typeof(FluentBpmnListEntry),
        typeof(FluentBpmnFeelEntry), typeof(FluentBpmnPropertyEntryFrame), typeof(FluentBpmnPropertyEntryHelp),
    ];

    [Theory, MemberData(nameof(Entries))]
    public void EveryEntryRendersInsidePropertyScope(Type type)
    {
        var entry = new BpmnPropertyEntry("value", "Value", BpmnPropertyEntryKind.Text, "42") { Feel = BpmnFeelMode.Required, Description = "Value help" };
        void content(Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder builder)
        {
            builder.OpenComponent(0, type);
            builder.AddComponentParameter(1, "Context", Context(entry));
            builder.CloseComponent();
        }
        var scope = Render<BpmnPropertyScopeProvider>(p => p.Add(c => c.Editors, FluentPropertyScope.Default.Editors).AddChildContent(content));
        Assert.Contains("Value", scope.Markup, StringComparison.Ordinal);
    }

    [Theory, InlineData(false), InlineData(true)]
    public void FeelPackageOverrideWinsRegardlessOfRegistrationOrder(bool first)
    {
        var services = new ServiceCollection();
        if (first) services.AddBpmnFeelEditor();
        services.AddFluentBpmnEditor();
        if (!first) services.AddBpmnFeelEditor();
        using var provider = services.BuildServiceProvider();
        var editors = provider.GetRequiredService<IOptions<BpmnOptions>>().Value.Properties.Editors;
        var entry = new BpmnPropertyEntry("expression", "Expression", BpmnPropertyEntryKind.Text) { Feel = BpmnFeelMode.Required };
        Assert.Equal(typeof(BpmnFeelCodeEditor), editors.Resolve(entry));
    }

    [Fact]
    public void RegistrationLeavesFluentHostConfigurationToHost()
    {
        var services = new ServiceCollection();
        services.AddFluentBpmnEditor();
        Assert.DoesNotContain(services, service => service.ServiceType == typeof(LibraryConfiguration));
    }

    [Fact]
    public void CompositionMirrorsStandardParametersAndInstanceMethods()
    {
        var standard = typeof(BpmnEditor);
        var fluent = typeof(FluentBpmnEditor);
        foreach (var property in standard.GetProperties().Where(p => p.GetCustomAttribute<ParameterAttribute>() is not null))
            Assert.Equal(property.PropertyType, fluent.GetProperty(property.Name)?.PropertyType);
        foreach (var method in standard.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly).Where(m => !m.IsSpecialName))
            Assert.Equal(method.ReturnType, fluent.GetMethod(method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray())?.ReturnType);
    }

    [Theory, InlineData(false), InlineData(true)]
    public async Task TextDraftSurvivesParentRerenderAndCommitsOnlyOnBlur(bool multiline)
    {
        string? submitted = null;
        var entry = new BpmnPropertyEntry("name", "Name", multiline ? BpmnPropertyEntryKind.TextArea : BpmnPropertyEntryKind.Text, "old");
        var context = Context(entry, value => submitted = value);
        var view = Render<FluentBpmnPropertyEntryView>(p => p.AddCascadingValue(FluentPropertyScope.Default).Add(c => c.Context, context));
        if (multiline)
        {
            var input = view.FindComponent<FluentTextArea>();
            await input.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync("draft"));
            view.Render(p => p.Add(c => c.Context, context));
            Assert.Equal("draft", input.Instance.Value);
            Assert.Null(submitted);
            await input.InvokeAsync(() => input.Instance.OnFocusOut.InvokeAsync(new FocusEventArgs()));
        }
        else
        {
            var input = view.FindComponent<FluentTextInput>();
            await input.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync("draft"));
            view.Render(p => p.Add(c => c.Context, context));
            Assert.Equal("draft", input.Instance.Value);
            Assert.Null(submitted);
            await input.InvokeAsync(() => input.Instance.OnFocusOut.InvokeAsync(new FocusEventArgs()));
        }
        Assert.Equal("draft", submitted);
    }

    [Fact]
    public async Task FeelUsesExpressionPrefixAndAccessibleAdvisoryValidation()
    {
        string? submitted = null;
        var entry = new BpmnPropertyEntry("expression", "Expression", BpmnPropertyEntryKind.Text, "=if x then 1") { Feel = BpmnFeelMode.Required };
        var view = Render<FluentBpmnFeelEntry>(p => p.AddCascadingValue(FluentPropertyScope.Default).Add(c => c.Context, Context(entry, value => submitted = value)));
        Assert.Contains("else", view.Find("[role=status]").TextContent, StringComparison.Ordinal);
        var input = view.FindComponent<FluentTextArea>();
        Assert.Equal("if x then 1", input.Instance.Value);
        await input.InvokeAsync(() => input.Instance.ValueChanged.InvokeAsync("if x then 1 else 2"));
        Assert.Empty(view.FindAll("[role=status]"));
        await input.InvokeAsync(() => input.Instance.OnFocusOut.InvokeAsync(new FocusEventArgs()));
        Assert.Equal("=if x then 1 else 2", submitted);
    }

    [Theory, InlineData(BpmnPropertyEntryKind.Checkbox), InlineData(BpmnPropertyEntryKind.Number), InlineData(BpmnPropertyEntryKind.Select)]
    public async Task TypedInputsSubmitValuesAndHonorReadOnly(BpmnPropertyEntryKind kind)
    {
        string? submitted = null;
        var entry = new BpmnPropertyEntry("value", "Value", kind, "1") { Choices = [new("1", "One"), new("2", "Two")] };
        var view = Render<FluentBpmnPropertyEntryView>(p => p.AddCascadingValue(FluentPropertyScope.Default).Add(c => c.Context, Context(entry, value => submitted = value)));
        Task ChangeAsync() => kind switch
        {
            BpmnPropertyEntryKind.Checkbox => view.FindComponent<FluentCheckbox>().Instance.ValueChanged.InvokeAsync(true),
            BpmnPropertyEntryKind.Number => view.FindComponent<FluentNumberInput<double?>>().Instance.ValueChanged.InvokeAsync(2.5),
            _ => view.FindComponent<FluentSelect<BpmnPropertyChoice, string>>().Instance.ValueChanged.InvokeAsync("2"),
        };
        await view.InvokeAsync(ChangeAsync);
        Assert.Equal(kind == BpmnPropertyEntryKind.Checkbox ? "true" : kind == BpmnPropertyEntryKind.Number ? "2.5" : "2", submitted);
        submitted = null;
        view.Render(p => p.Add(c => c.Context, Context(entry, value => submitted = value) with { IsReadOnly = true }));
        await view.InvokeAsync(ChangeAsync);
        Assert.Null(submitted);
    }

    [Fact]
    public async Task TemplateChooserSearchAndApplyUseCoreCommands()
    {
        var template = new BpmnElementTemplate { Id = "http", Name = "HTTP request", AppliesTo = ["bpmn:Task"], Description = "Send message" };
        Services.AddFluentBpmnEditor(options => options.Templates.Registry.Add(template));
        var snapshot = new BpmnElementSnapshot { ElementId = "Task_1", ElementType = "task", Category = BpmnElementCategory.Activity };
        BpmnEditCommand? command = null;
        var chooser = this.RenderSeeded<FluentBpmnTemplateChooser>(seed => seed.Snapshots([snapshot]).OnCommand(value => command = value),
            p => p.Add(c => c.Element, snapshot));
        chooser.Find(".bpmn-templates__select").Click();
        Assert.Single(chooser.FindAll("[role=option]"));
        var search = chooser.FindComponent<FluentTextInput>();
        await search.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("missing"));
        Assert.Empty(chooser.FindAll("[role=option]"));
        await search.InvokeAsync(() => search.Instance.ValueChanged.InvokeAsync("HTTP"));
        chooser.Find("[role=option]").Click();
        Assert.IsType<ApplyElementTemplateCommand>(command);
    }

    private static BpmnPropertyEntryContext Context(BpmnPropertyEntry entry, Action<string?>? submit = null) =>
        new(entry, EventCallback.Factory.Create<string?>(new object(), value => submit?.Invoke(value)), default, false, 0);
}
