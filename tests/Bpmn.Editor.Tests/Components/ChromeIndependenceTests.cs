using System.Reflection;
using System.Reflection.Emit;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Components;

/// <summary>Core infrastructure must not name the concrete chrome components, so they can move to a UI kit.</summary>
public sealed class ChromeIndependenceTests
{
    private static readonly Assembly Core = typeof(BpmnCanvas).Assembly;

    private static readonly HashSet<Type> Chrome =
    [
        typeof(BpmnContextMenu), typeof(BpmnContextualActions), typeof(BpmnToolbar), typeof(BpmnStencilPanel),
        typeof(BpmnPropertyPanel), typeof(BpmnValidationPanel), typeof(BpmnDiagramNavigator), typeof(BpmnShortcutHelp),
        typeof(BpmnImportOffer), typeof(BpmnCalledElementPicker), typeof(BpmnTemplateChooser), typeof(BpmnReferenceBrowser),
    ];

    private static readonly Dictionary<short, OpCode> OpCodes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!)
        .ToDictionary(code => code.Value);

    [Theory]
    [InlineData(typeof(BpmnEditorCanvas))]
    [InlineData(typeof(BpmnEditorScope))]
    [InlineData(typeof(BpmnEditorSession))]
    [InlineData(typeof(BpmnPart))]
    [InlineData(typeof(BpmnEditorState))]
    [InlineData(typeof(BpmnCanvasSurface))]
    public void InfrastructureDoesNotReferenceChromeComponents(Type type)
    {
        var offenders = new List<string>();
        foreach (var candidate in WithNested(type))
            Scan(candidate, offenders);
        Assert.Empty(offenders);
    }

    [Fact]
    public void CoreReferencesNoStandardChromeComponents()
    {
        var offenders = new List<string>();
        foreach (var type in Core.GetTypes().Where(t => !Chrome.Contains(t) && !IsChromeInternal(t)))
        {
            if (Chrome.Any(c => c.IsAssignableFrom(type))) continue;
            Scan(type, offenders);
        }
        Assert.Empty(offenders);
    }

    [Fact]
    public void ScannerFindsReferencesInKnownChromeUsers()
    {
        var offenders = new List<string>();
        Scan(typeof(BpmnStandardDefaults), offenders);
        Assert.Contains(offenders, offender => offender.Contains(nameof(BpmnContextMenu), StringComparison.Ordinal));
    }

    // Chrome components and their generated render-tree helpers may name each other (the panel renders the chooser).
    private static bool IsChromeInternal(Type type)
    {
        for (var current = type; current is not null; current = current.DeclaringType)
            if (Chrome.Contains(current)) return true;
        return false;
    }

    private static IEnumerable<Type> WithNested(Type type)
    {
        yield return type;
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).SelectMany(WithNested))
            yield return nested;
    }

    private static void Scan(Type type, List<string> offenders)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        void Check(Type? referenced, string where)
        {
            if (referenced is null) return;
            if (referenced.HasElementType) { Check(referenced.GetElementType(), where); return; }
            if (referenced.IsGenericType)
                foreach (var argument in referenced.GetGenericArguments()) Check(argument, where);
            if (Chrome.Contains(referenced.IsGenericType ? referenced.GetGenericTypeDefinition() : referenced))
                offenders.Add($"{type.FullName}: {where} -> {referenced.Name}");
        }

        Check(type.BaseType, "base");
        foreach (var field in type.GetFields(all)) Check(field.FieldType, $"field {field.Name}");
        foreach (var property in type.GetProperties(all)) Check(property.PropertyType, $"property {property.Name}");
        foreach (var method in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
        {
            if (method is MethodInfo info) Check(info.ReturnType, $"{method.Name} return");
            foreach (var parameter in method.GetParameters()) Check(parameter.ParameterType, $"{method.Name} parameter");
            var body = method.GetMethodBody();
            if (body is null) continue;
            foreach (var local in body.LocalVariables) Check(local.LocalType, $"{method.Name} local");
            foreach (var token in Tokens(body.GetILAsByteArray()!))
            {
                MemberInfo? member;
                try
                {
                    member = type.Module.ResolveMember(
                        token,
                        type.IsGenericType ? type.GetGenericArguments() : null,
                        method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null);
                }
                catch (ArgumentException)
                {
                    continue;
                }

                switch (member)
                {
                    case Type referenced: Check(referenced, method.Name); break;
                    case MethodBase called:
                        Check(called.DeclaringType, method.Name);
                        if (called.IsGenericMethod)
                            foreach (var argument in called.GetGenericArguments()) Check(argument, method.Name);
                        break;
                    case FieldInfo field: Check(field.DeclaringType, method.Name); Check(field.FieldType, method.Name); break;
                }
            }
        }
    }

    private static IEnumerable<int> Tokens(byte[] il)
    {
        for (var index = 0; index < il.Length;)
        {
            var value = (short)il[index++];
            if (value == 0xFE) value = (short)(0xFE00 | il[index++]);
            var code = OpCodes[value];
            switch (code.OperandType)
            {
                case OperandType.InlineMethod:
                case OperandType.InlineType:
                case OperandType.InlineField:
                case OperandType.InlineTok:
                    yield return BitConverter.ToInt32(il, index);
                    index += 4;
                    break;
                case OperandType.InlineNone: break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar: index += 1; break;
                case OperandType.InlineVar: index += 2; break;
                case OperandType.InlineI8:
                case OperandType.InlineR: index += 8; break;
                case OperandType.InlineSwitch:
                    var count = BitConverter.ToInt32(il, index);
                    index += 4 + 4 * count;
                    break;
                default: index += 4; break;
            }
        }
    }
}
