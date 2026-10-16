using System.Reflection;
using Bunit;
using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Editing;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

public sealed class ComponentContractTests : BunitContext
{
    private static IEnumerable<Type> Components => new[] { typeof(BpmnCanvas).Assembly, KitFixture.Assembly }
        .SelectMany(assembly => assembly.GetExportedTypes())
        .Where(type => !type.IsAbstract && typeof(IComponent).IsAssignableFrom(type));

    public static TheoryData<Type> EditorComponents => new(Components.Where(type => typeof(BpmnEditorComponentBase).IsAssignableFrom(type)));
    public static TheoryData<Type> CanvasWidgets => new(Components.Where(IsCanvasWidget));
    public static TheoryData<Type> PropertyEntries => new(Components.Where(type => typeof(BpmnPropertyEntryComponent).IsAssignableFrom(type)));
    public static TheoryData<BpmnEditorPart> Parts => new(Enum.GetValues<BpmnEditorPart>());
    public static TheoryData<BpmnPropertyEntryKind> EntryKinds => new(Enum.GetValues<BpmnPropertyEntryKind>());

    public ComponentContractTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        KitFixture.Register(Services);
    }

    [Theory, MemberData(nameof(EditorComponents))]
    public void ThrowsOutsideAnEditorScope(Type type)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render(Fragment(type)));
        Assert.Contains(type.Name, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(BpmnEditorScope), error.Message, StringComparison.Ordinal);
    }

    [Theory, MemberData(nameof(CanvasWidgets))]
    public void CanvasWidgetsThrowOutsideACanvas(Type type)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render<BpmnEditorScope>(p => p.AddChildContent(Fragment(type))));
        Assert.Contains(type.Name, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(BpmnCanvas), error.Message, StringComparison.Ordinal);
    }

    [Theory, MemberData(nameof(PropertyEntries))]
    public void EntriesThrowOutsideAPropertyScope(Type type)
    {
        var error = Assert.Throws<InvalidOperationException>(() => Render(Fragment(type)));
        Assert.Contains(type.Name, error.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(BpmnPropertyScopeProvider), error.Message, StringComparison.Ordinal);
    }

    [Theory, MemberData(nameof(Parts))]
    public void EveryDefaultPartHasRequiredBaseAndRendersParameterless(BpmnEditorPart part)
    {
        var type = Assert.IsAssignableFrom<Type>(Services.GetService<IOptions<BpmnOptions>>()!.Value.Components.Get(part));
        Assert.True(type.IsPublic && !type.IsAbstract);
        Assert.True(BpmnEditorComponentOptions.RequiredBase(part).IsAssignableFrom(type));
        AssertNoRequiredParameters(type);
        var rendered = this.Render<BpmnPart>(p => p.Add(c => c.Part, part), canvas: true);
        Assert.IsType(type, rendered.Cut.FindComponent<DynamicComponent>().Instance.Instance);
    }

    [Theory, MemberData(nameof(EntryKinds))]
    public void EveryDefaultEntryHasCoreBaseAndRendersParameterless(BpmnPropertyEntryKind kind)
    {
        var registry = Services.GetService<IOptions<BpmnOptions>>()!.Value.Properties.Editors;
        var type = registry.Resolve(new("value", "Value", kind));
        Assert.True(type.IsPublic && !type.IsAbstract);
        Assert.True(typeof(BpmnPropertyEntryComponent).IsAssignableFrom(type));
        Assert.Equal(kind is BpmnPropertyEntryKind.ReadOnly or BpmnPropertyEntryKind.Hidden ? typeof(BpmnCanvas).Assembly : KitFixture.Assembly, type.Assembly);
        AssertNoRequiredParameters(type);
        var rendered = Render<BpmnPropertyScopeProvider>(p => p.Add(c => c.Editors, registry).AddChildContent(Fragment(type)));
        Assert.NotNull(rendered.FindComponent<DynamicComponent>().Instance.Instance);
    }

    [Theory, InlineData(BpmnFeelMode.Required), InlineData(BpmnFeelMode.Optional)]
    public void FeelDefaultsAlsoRenderParameterless(BpmnFeelMode mode)
    {
        var registry = Services.GetService<IOptions<BpmnOptions>>()!.Value.Properties.Editors;
        var type = registry.Resolve(new("expression", "Expression", BpmnPropertyEntryKind.Text) { Feel = mode });
        Assert.Equal(KitFixture.Assembly, type.Assembly);
        Assert.True(typeof(BpmnPropertyEntryComponent).IsAssignableFrom(type));
        AssertNoRequiredParameters(type);
        Render<BpmnPropertyScopeProvider>(p => p.Add(c => c.Editors, registry).AddChildContent(Fragment(type)));
    }

    private static bool IsCanvasWidget(Type type)
    {
        for (var parent = type.BaseType; parent is not null; parent = parent.BaseType)
            if (parent.IsGenericType && parent.GetGenericTypeDefinition() == typeof(BpmnCanvasWidgetBase<>)) return true;
        return false;
    }

    private static void AssertNoRequiredParameters(Type type) => Assert.DoesNotContain(type.GetProperties(),
        property => property.GetCustomAttribute<ParameterAttribute>() is not null && property.GetCustomAttribute<EditorRequiredAttribute>() is not null);

    private static RenderFragment Fragment(Type type) => builder =>
    {
        builder.OpenComponent<DynamicComponent>(0);
        builder.AddComponentParameter(1, nameof(DynamicComponent.Type), type);
        builder.CloseComponent();
    };
}
