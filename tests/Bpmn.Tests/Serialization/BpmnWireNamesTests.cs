using System.Text.Json;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Serialization;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Serialization;

public sealed class BpmnWireNamesTests
{
    [Fact]
    public void AllModelWireNamesMatchExistingJsonConverters()
    {
        Check<BpmnFlowNodeType>();
        Check<BpmnEventDefinitionType>();
        Check<BpmnLoopCharacteristics>();
        Check<BpmnAssociationDirection>();
        Check<BpmnDataAssociationKind>();
    }

    [Fact]
    public void UnknownValuesKeepTheirNumericRepresentation() =>
        Assert.Equal("2147483647", BpmnWireNames.Of((BpmnFlowNodeType)int.MaxValue));

    private static void Check<TEnum>() where TEnum : struct, Enum
    {
        foreach (var value in Enum.GetValues<TEnum>())
        {
            Assert.Equal(JsonSerializer.Serialize(value).Trim('"'), BpmnWireNames.Of(value));
        }
    }
}
