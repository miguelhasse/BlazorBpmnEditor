using System.Collections;
using System.Reflection;

using Hasseware.AspNetCore.Components.Bpmn.Editing;

namespace Hasseware.AspNetCore.Components.Bpmn.Tests.Architecture;

/// <summary>
/// Keeps <see cref="BpmnReferenceMap"/> exhaustive as the model grows.
/// </summary>
/// <remarks>
/// <para>
/// The map enumerates reference sites by hand, which is reviewable but goes stale silently: add a
/// <c>calledElementRef</c>-shaped property to a record and nothing fails, until a rename leaves a
/// document that no longer loads. This test reads the model back and fails on any
/// reference-shaped property it has not been told about, so adding one forces a decision instead
/// of an omission.
/// </para>
/// <para>
/// It is a list of decisions rather than a clever rule because the decisions are not uniform. Some
/// of these are references between drawn elements; some point at root-level definitions that are
/// never renamed through the canvas; one or two are ids of the element itself. Each is named with
/// its reason.
/// </para>
/// </remarks>
public sealed class ReferenceMapCoverageTests
{
    /// <summary>
    /// Every reference-shaped property in the model, and what the map does about it.
    /// </summary>
    /// <remarks>
    /// <c>true</c> means the map visits it and a rename rewrites it. <c>false</c> means a
    /// deliberate exclusion, with the reason in the comment beside it.
    /// </remarks>
    private static readonly Dictionary<string, bool> Known = new(StringComparer.Ordinal)
    {
        // --- the element's own identity, which a rename changes rather than follows ---
        ["BpmnFlowNode.Id"] = false,
        ["BpmnSequenceFlow.Id"] = false,
        ["BpmnMessageFlow.Id"] = false,
        ["BpmnAssociation.Id"] = false,
        ["BpmnDataAssociation.Id"] = false,
        ["BpmnTextAnnotation.Id"] = false,
        ["BpmnGroup.Id"] = false,
        ["BpmnDataObjectReference.Id"] = false,
        ["BpmnDataStoreReference.Id"] = false,
        ["BpmnDataObject.Id"] = false,
        ["BpmnParticipant.Id"] = false,
        ["BpmnCollaboration.Id"] = false,
        ["BpmnLane.Id"] = false,
        ["BpmnLaneSet.Id"] = false,
        ["BpmnProcess.Id"] = false,
        ["BpmnGlobalTask.Id"] = false,
        ["BpmnDefinitions.Id"] = false,
        ["BpmnEventDefinition.Id"] = false,
        ["BpmnMessage.Id"] = false,
        ["BpmnSignal.Id"] = false,
        ["BpmnError.Id"] = false,
        ["BpmnEscalation.Id"] = false,
        ["BpmnDataStore.Id"] = false,
        ["BpmnItemDefinition.Id"] = false,
        ["BpmnCategory.Id"] = false,
        ["BpmnCategoryValue.Id"] = false,
        ["BpmnInterface.Id"] = false,
        ["BpmnOperation.Id"] = false,
        ["BpmnResource.Id"] = false,
        ["BpmnDiagram.Id"] = false,
        ["BpmnPlane.Id"] = false,
        ["BpmnShape.Id"] = false,
        ["BpmnEdge.Id"] = false,
        ["BpmnLabelStyle.Id"] = false,

        /*
         * Not part of a document's element graph at all. A DI label carries its own id and a
         * style id; an extension declaration and a relationship are interchange metadata; an
         * invariant violation is a diagnostic the validator produced, not something in the file.
         */
        ["BpmnLabel.Id"] = false,
        ["BpmnLabel.StyleId"] = false,
        ["BpmnExtensionDeclaration.Id"] = false,
        ["BpmnRelationship.Id"] = false,
        ["BpmnInvariantViolation.ElementId"] = false,
        ["BpmnInvariantViolation.RuleId"] = false,

        // --- references between elements a rename has to move ---
        ["BpmnFlowNode.ContainerId"] = true,
        ["BpmnFlowNode.PoolId"] = true,
        // The node's own projection of Lane membership, which a Lane rename has to move with it.
        ["BpmnFlowNode.LaneId"] = true,
        ["BpmnFlowNode.AttachedToRef"] = true,
        ["BpmnFlowNode.CalledElementRef"] = true,
        ["BpmnFlowNode.DefaultFlowRef"] = true,
        ["BpmnFlowNode.MessageRef"] = true,
        ["BpmnLoopDataItem.Id"] = false,
        ["BpmnLoopDataItem.ItemSubjectRef"] = true,
        ["BpmnMultiInstanceLoopDetails.LoopDataInputRef"] = true,
        ["BpmnMultiInstanceLoopDetails.LoopDataOutputRef"] = true,
        ["BpmnEventDefinition.Ref"] = true,
        ["BpmnEventDefinition.ActivityRef"] = true,
        ["BpmnSequenceFlow.SourceRef"] = true,
        ["BpmnSequenceFlow.TargetRef"] = true,
        ["BpmnSequenceFlow.ContainerId"] = true,
        ["BpmnMessageFlow.SourceRef"] = true,
        ["BpmnMessageFlow.TargetRef"] = true,
        ["BpmnMessageFlow.MessageRef"] = true,
        ["BpmnAssociation.SourceRef"] = true,
        ["BpmnAssociation.TargetRef"] = true,
        ["BpmnAssociation.ContainerId"] = true,
        ["BpmnDataAssociation.SourceRefs"] = true,
        ["BpmnDataAssociation.TargetRef"] = true,
        ["BpmnDataAssociation.ContainerId"] = true,
        ["BpmnDataAssociation.OwnerId"] = true,
        ["BpmnTextAnnotation.ContainerId"] = true,
        ["BpmnGroup.ContainerId"] = true,
        ["BpmnGroup.CategoryValueRef"] = true,
        ["BpmnDataObjectReference.ContainerId"] = true,
        ["BpmnDataObjectReference.DataObjectRef"] = true,
        ["BpmnDataStoreReference.ContainerId"] = true,
        ["BpmnDataStoreReference.DataStoreRef"] = true,
        ["BpmnDataObject.ContainerId"] = true,
        ["BpmnDataObject.ItemSubjectRef"] = true,
        ["BpmnParticipant.ProcessRef"] = true,
        ["BpmnLaneSet.ContainerId"] = true,
        ["BpmnLaneSet.LaneIds"] = true,
        ["BpmnLane.LaneSetId"] = true,
        ["BpmnLane.ChildLaneSetId"] = true,
        ["BpmnLane.FlowNodeRefs"] = true,
        ["BpmnPlane.BpmnElement"] = true,
        ["BpmnShape.BpmnElement"] = true,
        ["BpmnEdge.BpmnElement"] = true,

        /*
         * Root-level definitions pointing at other root-level definitions. These resolve within
         * the catalog and are never renamed from the canvas — there is no shape to select. They
         * are excluded knowingly; the day a definitions editor exists they become sites.
         */
        ["BpmnMessage.ItemRef"] = false,
        ["BpmnSignal.StructureRef"] = false,
        ["BpmnError.StructureRef"] = false,
        ["BpmnEscalation.StructureRef"] = false,
        ["BpmnDataStore.ItemSubjectRef"] = false,
        ["BpmnItemDefinition.StructureRef"] = false,
        ["BpmnOperation.InMessageRef"] = false,
        ["BpmnOperation.OutMessageRef"] = false,
        ["BpmnOperation.ImplementationRef"] = false,
        ["BpmnInterface.ImplementationRef"] = false,

        // --- DI's own internal wiring, which is not a BPMN element reference ---
    };

    [Fact]
    public void EveryReferenceShapedPropertyHasBeenDecidedAbout()
    {
        var undecided = ReferenceShapedProperties()
            .Where(name => !Known.ContainsKey(name))
            .ToList();

        Assert.True(
            undecided.Count == 0,
            $"""
             The model has reference-shaped properties BpmnReferenceMap has not been told about.
             Add each to BpmnReferenceMap.Sites and to the Known table below, or add it to the
             table as false with the reason it is not a site:

               {string.Join("\n  ", undecided)}
             """);
    }

    [Fact]
    public void TheDecisionTableDoesNotOutliveTheModel()
    {
        // The other direction: a property that was removed or renamed leaves an entry claiming to
        // cover something that is not there, which reads as coverage and is not.
        var actual = ReferenceShapedProperties().ToHashSet(StringComparer.Ordinal);
        var stale = Known.Keys.Where(name => !actual.Contains(name)).ToList();

        Assert.True(stale.Count == 0, $"Entries for properties that no longer exist:\n  {string.Join("\n  ", stale)}");
    }

    /// <summary>
    /// Properties that look like they hold an element id: a string or list of strings whose name
    /// ends the way BPMN's reference attributes end.
    /// </summary>
    private static IEnumerable<string> ReferenceShapedProperties() => typeof(BpmnReferenceMap).Assembly
        .GetTypes()
        .Where(type => type.IsPublic
            && type.Namespace is { } space
            && (space == "Hasseware.AspNetCore.Components.Bpmn.Model" || space == "Hasseware.AspNetCore.Components.Bpmn.Model.Di"))
        .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => IsIdShaped(property) && HoldsIds(property.PropertyType))
            .Select(property => $"{type.Name}.{property.Name}"))
        .Order(StringComparer.Ordinal);

    private static bool IsIdShaped(PropertyInfo property) =>
        property.Name.EndsWith("Ref", StringComparison.Ordinal)
        || property.Name.EndsWith("Refs", StringComparison.Ordinal)
        || property.Name.EndsWith("Id", StringComparison.Ordinal)
        || property.Name.EndsWith("Ids", StringComparison.Ordinal)
        || property.Name == "BpmnElement"
        || property.Name is "SourceElement" or "TargetElement";

    private static bool HoldsIds(Type type) =>
        type == typeof(string)
        || (type.IsGenericType
            && typeof(IEnumerable).IsAssignableFrom(type)
            && type.GetGenericArguments() is [var argument]
            && argument == typeof(string));
}
