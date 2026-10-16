using System.Globalization;
using System.Xml;

using Hasseware.AspNetCore.Components.Bpmn.Configuration;
using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Editing;

/// <summary>
/// Property editing and type switching (specification section 7).
/// </summary>
/// <remarks>
/// Split out because both are schema-driven in a way the geometric commands are not: they read the
/// descriptors <see cref="BpmnElementInspector"/> publishes and write back through them, so the set
/// of editable properties is defined once and cannot drift between what a menu offers and what the
/// editor accepts.
/// </remarks>
public static partial class BpmnDocumentEditor
{
    // --- property editing -------------------------------------------------------

    private static BpmnEditResult ApplyProperties(
        BpmnDocument document,
        UpdatePropertiesCommand command,
        EditingOptions options)
    {
        if (document.Find(command.ElementId) is not { } element)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-072",
                $"There is no element with id '{command.ElementId}' to edit.",
                command.ElementId));
        }

        if (command.Values.Count == 0)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-070",
                "A property edit that sets nothing is not an edit.",
                command.ElementId));
        }

        // Both lists. The surfaces keep standard and extension properties apart (section 7), but the
        // editor accepts exactly what the inspector offered, or one of them would be editable in
        // the panel and unknown here.
        var descriptors = BpmnElementInspector
            .Properties(document, element)
            .Concat(BpmnElementInspector.ExtensionProperties(element))
            .ToDictionary(descriptor => descriptor.Name, StringComparer.Ordinal);

        // Everything is checked before anything is written. Section 7 requires an atomic result,
        // and a batch that is wrong in its third value must not have applied the first two.
        foreach (var value in command.Values)
        {
            if (!descriptors.TryGetValue(value.Name, out var descriptor))
            {
                return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-070",
                    $"'{value.Name}' is not an editable property of this element.",
                    command.ElementId));
            }

            if (Unacceptable(descriptor, value.Value) is { } problem)
            {
                return BpmnEditResult.Reject(document, problem with { ElementId = command.ElementId });
            }
        }

        var notes = new List<BpmnEditDiagnostic>();
        var updated = element;

        foreach (var value in command.Values)
        {
            updated = Set(updated, value, notes, document.Definitions.NamespaceDeclarations);
        }

        if (element is BpmnFlowNode
            && updated is BpmnFlowNode candidateNode
            && command.Values.Any(value => value.Name is "eventDefinition" or "isInterrupting"))
        {
            var legalNode = ApplyEventLegality(document, candidateNode, options, notes, out var eventProblem);
            if (eventProblem is not null) return BpmnEditResult.Reject(document, eventProblem);
            updated = legalNode!;
        }

        if (RuleProblem(updated) is { } violation) return BpmnEditResult.Reject(document, violation);

        var edited = WithElement(document, updated);

        // A default flow is stated twice — once as the flow's own flag and once as the source's
        // defaultFlowRef — so setting either has to move the other, or the document ends up
        // disagreeing with itself about which branch is the default.
        edited = ReconcileDefaultFlow(edited, element, updated, notes);

        return BpmnEditResult.Applied(edited, notes);
    }

    /// <summary>Checks a submitted value against the kind and choices its descriptor declares.</summary>
    private static BpmnEditDiagnostic? Unacceptable(BpmnPropertyDescriptor descriptor, string? value)
    {
        if (descriptor.IsReadOnly)
        {
            return new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-070",
                $"'{descriptor.Name}' is shown for inspection and cannot be set.");
        }

        return descriptor.Kind switch
        {
            BpmnPropertyKind.Boolean when value is not null && !IsBoolean(value) => new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-071",
                $"'{descriptor.Name}' is a true/false property and '{value}' is neither."),

            BpmnPropertyKind.Text
                when descriptor.Name.EndsWith("loopMaximum", StringComparison.Ordinal)
                    && !string.IsNullOrEmpty(value)
                    && (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var maximum) || maximum < 0)
                => new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-071",
                    $"'{descriptor.Name}' must be a non-negative integer."),

            BpmnPropertyKind.Choice when !descriptor.Choices.Any(choice => choice.Value == (value ?? string.Empty))
                => new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-071",
                    $"'{value}' is not one of the values '{descriptor.Name}' accepts."),

            _ => null,
        };
    }

    private static bool IsBoolean(string value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);

    private static bool? AsBoolean(string? value) =>
        value is null ? null : string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>An empty choice value means "no reference", which is null on the model.</summary>
    private static string? AsReference(string? value) => string.IsNullOrEmpty(value) ? null : value;

    /// <summary>
    /// Writes one property onto an element.
    /// </summary>
    /// <remarks>
    /// The two carrier-backed cases leave through their own door first. An extension value and
    /// documentation are not attributes of any BPMN type — they live on <c>Extras</c>, which each
    /// element declares for itself — so routing them through the per-type switch below would mean
    /// repeating both in every branch of it.
    /// </remarks>
    private static object Set(
        object element,
        BpmnPropertyValue value,
        List<BpmnEditDiagnostic> notes,
        IReadOnlyDictionary<string, string> namespaces)
    {
        if (value.Name.StartsWith(BpmnElementInspector.ExtensionPrefix, StringComparison.Ordinal))
        {
            return SetExtension(element, value.Name, value.Value);
        }

        if (value.Name == "documentation") return SetDocumentation(element, value.Value);

        return element switch
        {
            BpmnFlowNode node => SetOnFlowNode(node, value, notes, namespaces),

            BpmnSequenceFlow flow => value.Name switch
            {
                "name" => flow with { Name = value.Value },
                "isDefault" => flow with { IsDefault = AsBoolean(value.Value) ?? false },
                "conditionExpression" => flow with { ConditionExpression = value.Value },
                "conditionLanguage" => flow with { ConditionLanguage = value.Value },
                _ => flow,
            },

            BpmnMessageFlow flow => value.Name switch
            {
                "name" => flow with { Name = value.Value },
                "messageRef" => flow with { MessageRef = AsReference(value.Value) },
                _ => flow,
            },

            BpmnAssociation association when value.Name == "associationDirection" =>
                association with { Direction = ParseDirection(value.Value) },

            BpmnTextAnnotation annotation => value.Name switch
            {
                "text" => annotation with { Text = value.Value ?? string.Empty },
                "textFormat" => annotation with { TextFormat = value.Value },
                _ => annotation,
            },

            BpmnGroup group => value.Name switch
            {
                "name" => group with { Name = value.Value },
                "categoryValueRef" => group with { CategoryValueRef = AsReference(value.Value) },
                _ => group,
            },

            BpmnDataObjectReference reference => value.Name switch
            {
                "name" => reference with { Name = value.Value },
                "state" => reference with { State = value.Value },
                "isCollection" => reference with { IsCollection = AsBoolean(value.Value) ?? false },
                "dataObjectRef" => reference with { DataObjectRef = AsReference(value.Value) },
                _ => reference,
            },

            BpmnDataStoreReference reference => value.Name switch
            {
                "name" => reference with { Name = value.Value },
                "state" => reference with { State = value.Value },
                "dataStoreRef" => reference with { DataStoreRef = AsReference(value.Value) },
                _ => reference,
            },

            BpmnDataAssociation association => SetOnDataAssociation(association, value, namespaces),

            BpmnParticipant participant when value.Name == "name" => participant with { Name = value.Value },
            BpmnLane lane when value.Name == "name" => lane with { Name = value.Value },

            BpmnProcess process => value.Name switch
            {
                "name" => process with { Name = value.Value },
                "isExecutable" => process with { IsExecutable = AsBoolean(value.Value) },
                "processType" => process with { ProcessType = AsReference(value.Value) },
                "isClosed" => process with { IsClosed = AsBoolean(value.Value) },
                _ => process,
            },

            BpmnCollaboration collaboration when value.Name == "name" => collaboration with { Name = value.Value },

            _ => element,
        };
    }

    /// <summary>
    /// Writes <c>documentation</c> on whichever element carries it.
    /// </summary>
    /// <remarks>
    /// Every element's documentation lives on the same carrier, so it is set in one place instead
    /// of once per branch of the type switch. Blank means absent: an empty <c>documentation</c>
    /// element in the XML says something was written and then deleted, which is not what an empty
    /// text box means.
    /// </remarks>
    private static object SetDocumentation(object element, string? value) => WithExtras(
        element,
        extras => extras with { Documentation = string.IsNullOrWhiteSpace(value) ? null : value });

    /// <summary>
    /// Writes one value inside the element's preserved extension content.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The path addresses children by index, so the rewrite walks to the element it names and
    /// rebuilds only the spine down to it: every sibling and every untouched subtree is the same
    /// instance afterwards. Section 6.1 asks for extension content to survive an edit unchanged,
    /// and the surest way to keep that promise is not to copy what was not edited.
    /// </para>
    /// <para>
    /// A blank attribute value removes the attribute rather than writing an empty one, which is the
    /// rule the standard fields already use: an empty box means the thing is not there. Blank text
    /// on a leaf clears it for the same reason.
    /// </para>
    /// </remarks>
    private static object SetExtension(object element, string path, string? value) => WithExtras(
        element,
        extras => ParseExtensionPath(path) is { } target
            ? extras with { Extensions = Rewrite(extras.Extensions, target, 0, value) }
            : extras);

    /// <summary>An extension path: which child at each depth, and what to write on arrival.</summary>
    private sealed record ExtensionTarget(IReadOnlyList<int> Indices, string? Attribute);

    private static ExtensionTarget? ParseExtensionPath(string name)
    {
        var path = name[BpmnElementInspector.ExtensionPrefix.Length..];
        var attributeAt = path.LastIndexOf('@');
        var textAt = path.LastIndexOf('#');

        string? attribute = null;

        if (attributeAt >= 0)
        {
            attribute = path[(attributeAt + 1)..];
            path = path[..attributeAt];
        }
        else if (textAt >= 0)
        {
            path = path[..textAt];
        }
        else
        {
            return null;
        }

        var indices = new List<int>();

        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!int.TryParse(segment, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)) return null;

            indices.Add(index);
        }

        return indices.Count == 0 ? null : new ExtensionTarget(indices, attribute);
    }

    private static IReadOnlyList<BpmnExtensionElement> Rewrite(
        IReadOnlyList<BpmnExtensionElement> elements,
        ExtensionTarget target,
        int depth,
        string? value)
    {
        var index = target.Indices[depth];

        // The descriptors are generated from this same tree, so a path that no longer resolves
        // means the document moved under the edit; leaving the content alone is the safe answer.
        if (index < 0 || index >= elements.Count) return elements;

        var found = elements[index];
        var replacement = depth == target.Indices.Count - 1
            ? WriteInto(found, target.Attribute, value)
            : found with { Children = Rewrite(found.Children, target, depth + 1, value) };

        var copy = elements.ToArray();
        copy[index] = replacement;

        return copy;
    }

    private static BpmnExtensionElement WriteInto(BpmnExtensionElement element, string? attribute, string? value)
    {
        if (attribute is null)
        {
            return element with { Text = string.IsNullOrWhiteSpace(value) ? null : value };
        }

        var attributes = new Dictionary<string, string>(element.Attributes, StringComparer.Ordinal);

        if (string.IsNullOrEmpty(value))
        {
            attributes.Remove(attribute);
        }
        else
        {
            attributes[attribute] = value;
        }

        return element with { Attributes = attributes };
    }

    /// <summary>
    /// Applies a change to whichever extension carrier the element declares.
    /// </summary>
    /// <remarks>
    /// One switch for every write that touches <c>Extras</c>, because the model deliberately
    /// declares the carrier on each record rather than through an interface: a type that cannot
    /// carry extensions must not be able to look as though it can.
    /// </remarks>
    private static object WithExtras(object element, Func<BpmnElementExtras, BpmnElementExtras> apply) => element switch
    {
        BpmnFlowNode node => node with { Extras = apply(node.Extras) },
        BpmnSequenceFlow flow => flow with { Extras = apply(flow.Extras) },
        BpmnMessageFlow flow => flow with { Extras = apply(flow.Extras) },
        BpmnAssociation association => association with { Extras = apply(association.Extras) },
        BpmnTextAnnotation annotation => annotation with { Extras = apply(annotation.Extras) },
        BpmnGroup group => group with { Extras = apply(group.Extras) },
        BpmnDataObjectReference reference => reference with { Extras = apply(reference.Extras) },
        BpmnDataStoreReference reference => reference with { Extras = apply(reference.Extras) },
        BpmnDataObject data => data with { Extras = apply(data.Extras) },
        BpmnParticipant participant => participant with { Extras = apply(participant.Extras) },
        BpmnLane lane => lane with { Extras = apply(lane.Extras) },
        BpmnProcess process => process with { Extras = apply(process.Extras) },
        BpmnCollaboration collaboration => collaboration with { Extras = apply(collaboration.Extras) },
        BpmnEventDefinition definition => definition with { Extras = apply(definition.Extras) },
        BpmnMessage message => message with { Extras = apply(message.Extras) },
        BpmnSignal signal => signal with { Extras = apply(signal.Extras) },
        BpmnError error => error with { Extras = apply(error.Extras) },
        BpmnEscalation escalation => escalation with { Extras = apply(escalation.Extras) },
        _ => element,
    };

    private static BpmnFlowNode SetOnFlowNode(
        BpmnFlowNode node,
        BpmnPropertyValue value,
        List<BpmnEditDiagnostic> notes,
        IReadOnlyDictionary<string, string> namespaces) => value.Name switch
        {
            "name" => node with { Name = value.Value },
            "isInterrupting" => node with { IsInterrupting = AsBoolean(value.Value) ?? true },
            "isForCompensation" => node with { IsForCompensation = AsBoolean(value.Value) ?? false },
            "loopCharacteristics" => SetLoopCharacteristics(node, ParseLoop(value.Value)),
            "calledElementRef" => node with
            {
                CalledElementRef = AsReference(value.Value),
                CalledElementQName = null,
            },
            "defaultFlowRef" => node with { DefaultFlowRef = AsReference(value.Value) },
            "messageRef" => node with { MessageRef = AsReference(value.Value) },
            "eventDefinition" => WithTrigger(node, ParseTrigger(value.Value), notes),
            _ => SetFlowNodeDetail(node, value, namespaces),
        };

    private static BpmnFlowNode SetLoopCharacteristics(BpmnFlowNode node, BpmnLoopCharacteristics loopCharacteristics)
    {
        var multiInstance = loopCharacteristics switch
        {
            BpmnLoopCharacteristics.MultiInstanceSequential => (node.MultiInstanceLoop ?? new BpmnMultiInstanceLoopDetails()) with
            {
                IsSequential = true,
            },
            BpmnLoopCharacteristics.MultiInstanceParallel => (node.MultiInstanceLoop ?? new BpmnMultiInstanceLoopDetails()) with
            {
                IsSequential = false,
            },
            _ => node.MultiInstanceLoop,
        };

        return node with
        {
            LoopCharacteristics = loopCharacteristics,
            MultiInstanceLoop = multiInstance,
        };
    }

    private static BpmnFlowNode SetFlowNodeDetail(
        BpmnFlowNode node,
        BpmnPropertyValue value,
        IReadOnlyDictionary<string, string> namespaces)
    {
        if (value.Name == "activationCondition" || value.Name.StartsWith("activationCondition.", StringComparison.Ordinal))
        {
            return node with
            {
                ActivationCondition = SetExpression(node.ActivationCondition, "activationCondition", value, namespaces),
            };
        }

        if (value.Name.StartsWith("standardLoop.", StringComparison.Ordinal))
        {
            var details = node.StandardLoop ?? new BpmnStandardLoopDetails();
            var property = value.Name["standardLoop.".Length..];
            details = property switch
            {
                "testBefore" => details with { TestBefore = AsBoolean(value.Value) },
                "loopMaximum" => details with
                {
                    LoopMaximum = int.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var maximum)
                        ? maximum
                        : null,
                },
                _ when property.StartsWith("loopCondition", StringComparison.Ordinal) => details with
                {
                    LoopCondition = SetExpression(details.LoopCondition, "standardLoop.loopCondition", value, namespaces),
                },
                _ => details,
            };
            return node with { StandardLoop = details };
        }

        if (value.Name.StartsWith("multiInstance.", StringComparison.Ordinal))
        {
            var details = node.MultiInstanceLoop ?? new BpmnMultiInstanceLoopDetails();
            var property = value.Name["multiInstance.".Length..];
            details = property switch
            {
                "isSequential" => details with { IsSequential = AsBoolean(value.Value) },
                "behavior" => details with { Behavior = value.Value },
                "loopCardinality" or "loopCardinality.language" or "loopCardinality.type" => details with
                {
                    LoopCardinality = SetExpression(details.LoopCardinality, "multiInstance.loopCardinality", value, namespaces),
                },
                "loopDataInputRef" => details with { LoopDataInputRef = EmptyToNull(value.Value) },
                "loopDataOutputRef" => details with { LoopDataOutputRef = EmptyToNull(value.Value) },
                "completionCondition" or "completionCondition.language" or "completionCondition.type" => details with
                {
                    CompletionCondition = SetExpression(details.CompletionCondition, "multiInstance.completionCondition", value, namespaces),
                },
                _ when property.StartsWith("inputDataItem.", StringComparison.Ordinal) => details with
                {
                    InputDataItem = SetLoopDataItem(
                        details.InputDataItem,
                        property["inputDataItem.".Length..],
                        value.Value),
                },
                _ when property.StartsWith("outputDataItem.", StringComparison.Ordinal) => details with
                {
                    OutputDataItem = SetLoopDataItem(
                        details.OutputDataItem,
                        property["outputDataItem.".Length..],
                        value.Value),
                },
                _ when property.StartsWith("complexBehavior.", StringComparison.Ordinal) => details with
                {
                    ComplexBehaviorDefinitions = SetComplexBehavior(details, property, value, namespaces),
                },
                _ => details,
            };

            if (property.StartsWith("loopCardinality.", StringComparison.Ordinal))
            {
                details = details with
                {
                    LoopCardinality = SetExpression(details.LoopCardinality, "multiInstance.loopCardinality", value, namespaces),
                };
            }
            else if (property.StartsWith("completionCondition.", StringComparison.Ordinal))
            {
                details = details with
                {
                    CompletionCondition = SetExpression(details.CompletionCondition, "multiInstance.completionCondition", value, namespaces),
                };
            }

            var loopCharacteristics = property == "isSequential"
                ? details.IsSequential == true
                    ? BpmnLoopCharacteristics.MultiInstanceSequential
                    : BpmnLoopCharacteristics.MultiInstanceParallel
                : node.LoopCharacteristics;

            return node with
            {
                MultiInstanceLoop = details,
                LoopCharacteristics = loopCharacteristics,
            };
        }

        return node;
    }

    private static BpmnDataAssociation SetOnDataAssociation(
        BpmnDataAssociation association,
        BpmnPropertyValue value,
        IReadOnlyDictionary<string, string> namespaces)
    {
        var parts = value.Name.Split('.');
        if (parts.Length < 3
            || parts[0] != "assignment"
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index < 0
            || index >= association.Assignments.Count
            || parts[2] is not ("from" or "to")
            || parts.Length > 4
            || (parts.Length == 4 && parts[3] is not ("language" or "type")))
        {
            return association;
        }

        var assignment = association.Assignments[index];
        var prefix = $"assignment.{index}.{parts[2]}";
        var expression = SetExpression(parts[2] == "from" ? assignment.From : assignment.To, prefix, value, namespaces)
            ?? new BpmnFormalExpression(string.Empty);
        var updated = parts[2] == "from"
            ? assignment with { From = expression }
            : assignment with { To = expression };
        var assignments = association.Assignments.ToArray();
        assignments[index] = updated;
        return association with { Assignments = assignments };
    }

    private static IReadOnlyList<BpmnComplexBehaviorDefinition> SetComplexBehavior(
        BpmnMultiInstanceLoopDetails details,
        string property,
        BpmnPropertyValue value,
        IReadOnlyDictionary<string, string> namespaces)
    {
        var segments = property.Split('.');
        if (segments.Length < 3
            || !int.TryParse(segments[1], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index < 0
            || index >= (details.ComplexBehaviorDefinitions?.Count ?? 0))
        {
            return details.ComplexBehaviorDefinitions ?? [];
        }

        var behaviors = (details.ComplexBehaviorDefinitions ?? []).ToArray();
        var behavior = behaviors[index];
        if (segments[2] == "event")
        {
            behavior = behavior with { Event = value.Value };
        }
        else if (segments[2] == "condition" || segments[2].StartsWith("condition.", StringComparison.Ordinal))
        {
            behavior = behavior with
            {
                Condition = SetExpression(
                    behavior.Condition,
                    $"multiInstance.complexBehavior.{index}.condition",
                    value,
                    namespaces),
            };
        }

        behaviors[index] = behavior;
        return behaviors;
    }

    private static BpmnLoopDataItem? SetLoopDataItem(
        BpmnLoopDataItem? item,
        string property,
        string? value)
    {
        item ??= new BpmnLoopDataItem();
        return property switch
        {
            "id" => item with { Id = EmptyToNull(value) },
            "name" => item with { Name = EmptyToNull(value) },
            "itemSubjectRef" => item with { ItemSubjectRef = EmptyToNull(value) },
            "isCollection" => item with { IsCollection = AsBoolean(value) },
            _ => item,
        };
    }

    private static BpmnFormalExpression? SetExpression(
        BpmnFormalExpression? expression,
        string baseName,
        BpmnPropertyValue value,
        IReadOnlyDictionary<string, string> namespaces)
    {
        expression ??= new BpmnFormalExpression(string.Empty);
        var suffix = value.Name == baseName ? string.Empty : value.Name[(baseName.Length + 1)..];
        expression = suffix switch
        {
            "" => expression with { Body = value.Value ?? string.Empty },
            "language" => expression with { Language = EmptyToNull(value.Value) },
            "type" => expression with
            {
                TypeQName = EmptyToNull(value.Value),
                TypeNamespaceUri = string.Equals(value.Value, expression.TypeQName, StringComparison.Ordinal)
                    ? expression.TypeNamespaceUri
                    : ResolveQNameNamespace(value.Value, namespaces),
            },
            _ => expression,
        };

        return expression.Body.Length == 0 && expression.Language is null && expression.TypeQName is null
            ? null
            : expression;
    }

    private static string? ResolveQNameNamespace(string? value, IReadOnlyDictionary<string, string> namespaces)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var separator = value.IndexOf(':');
        var prefix = separator < 0 ? string.Empty : value[..separator];
        var localName = separator < 0 ? value : value[(separator + 1)..];
        if (separator == 0 || localName.Contains(':')) return null;

        try
        {
            XmlConvert.VerifyNCName(localName);
            if (prefix.Length > 0) XmlConvert.VerifyNCName(prefix);
        }
        catch (XmlException)
        {
            return null;
        }

        if (prefix == "xml") return "http://www.w3.org/XML/1998/namespace";
        if (prefix.Length == 0) return namespaces.GetValueOrDefault(string.Empty, string.Empty);
        return namespaces.TryGetValue(prefix, out var namespaceUri) ? namespaceUri : null;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrEmpty(value) ? null : value;

    private static BpmnAssociationDirection ParseDirection(string? value) => value switch
    {
        "one" => BpmnAssociationDirection.One,
        "both" => BpmnAssociationDirection.Both,
        _ => BpmnAssociationDirection.None,
    };

    private static BpmnLoopCharacteristics ParseLoop(string? value) => value switch
    {
        "standard" => BpmnLoopCharacteristics.Standard,
        "multiInstanceSequential" => BpmnLoopCharacteristics.MultiInstanceSequential,
        "multiInstanceParallel" => BpmnLoopCharacteristics.MultiInstanceParallel,
        _ => BpmnLoopCharacteristics.None,
    };

    private static BpmnEventDefinitionType ParseTrigger(string? value) => value switch
    {
        "message" => BpmnEventDefinitionType.Message,
        "timer" => BpmnEventDefinitionType.Timer,
        "error" => BpmnEventDefinitionType.Error,
        "escalation" => BpmnEventDefinitionType.Escalation,
        "cancel" => BpmnEventDefinitionType.Cancel,
        "compensation" => BpmnEventDefinitionType.Compensation,
        "conditional" => BpmnEventDefinitionType.Conditional,
        "link" => BpmnEventDefinitionType.Link,
        "signal" => BpmnEventDefinitionType.Signal,
        "multiple" => BpmnEventDefinitionType.Multiple,
        "parallelMultiple" => BpmnEventDefinitionType.ParallelMultiple,
        "terminate" => BpmnEventDefinitionType.Terminate,
        _ => BpmnEventDefinitionType.None,
    };

    /// <summary>
    /// Retriggers an Event.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The enum decides the glyph; the <c>*EventDefinition</c> children carry the <c>messageRef</c>,
    /// timer expression or link name that makes the Event mean something. A new trigger cannot
    /// inherit the old one's payload — an <c>errorRef</c> on a Message Event is not a Message — so
    /// the definitions are replaced by one empty definition of the new kind and the loss is
    /// reported rather than left for the user to discover on export.
    /// </para>
    /// <para>
    /// Multiple and Parallel Multiple mean "several of the below", which nothing here can invent.
    /// The trigger is set and the definitions are left empty, with a note saying so.
    /// </para>
    /// </remarks>
    private static BpmnFlowNode WithTrigger(
        BpmnFlowNode node,
        BpmnEventDefinitionType trigger,
        List<BpmnEditDiagnostic> notes)
    {
        if (node.EventDefinition == trigger) return node;

        var carried = node.EventDefinitions
            .Where(definition => definition.Ref is not null || definition.Expression is not null)
            .ToList();

        if (carried.Count > 0)
        {
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-082",
                $"The previous trigger's references were cleared: {string.Join(", ", carried.Select(definition => definition.Ref ?? definition.Expression))}.",
                node.Id));
        }

        if (trigger is BpmnEventDefinitionType.Multiple or BpmnEventDefinitionType.ParallelMultiple)
        {
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-082",
                "A Multiple Event needs its individual triggers declared before it means anything.",
                node.Id));
        }

        return node with
        {
            EventDefinition = trigger,
            EventDefinitions = trigger is BpmnEventDefinitionType.None
                or BpmnEventDefinitionType.Multiple
                or BpmnEventDefinitionType.ParallelMultiple
                ? []
                : [new BpmnEventDefinition(trigger)],
        };
    }

    /// <summary>Rules a finished element can break on its own, checked after every value is set.</summary>
    private static BpmnEditDiagnostic? RuleProblem(object element) => element switch
    {
        // SF-006: the default branch is the one taken when no condition holds, so a default flow
        // with a condition is a contradiction. Both values can be sent in one command, which is how
        // a user turns a conditional flow into the default.
        BpmnSequenceFlow { IsDefault: true, ConditionExpression: { Length: > 0 } } flow => new BpmnEditDiagnostic(
            BpmnEditSeverity.Rejected,
            "SF-006",
            "A default flow cannot carry a condition. Clear the condition in the same edit to make this the default.",
            flow.Id),

        _ => null,
    };

    /// <summary>
    /// Keeps a flow's <c>isDefault</c> and its source's <c>defaultFlowRef</c> saying the same thing.
    /// </summary>
    private static BpmnDocument ReconcileDefaultFlow(
        BpmnDocument document,
        object before,
        object after,
        List<BpmnEditDiagnostic> notes)
    {
        switch (before, after)
        {
            case (BpmnSequenceFlow original, BpmnSequenceFlow flow) when original.IsDefault != flow.IsDefault:
                return WithDefaultFlow(document, flow.SourceRef, flow.IsDefault ? flow.Id : null, notes);

            case (BpmnFlowNode original, BpmnFlowNode node) when original.DefaultFlowRef != node.DefaultFlowRef:
                return WithDefaultFlow(document, node.Id, node.DefaultFlowRef, notes);

            default:
                return document;
        }
    }

    private static BpmnDocument WithDefaultFlow(
        BpmnDocument document,
        string sourceId,
        string? defaultFlowId,
        List<BpmnEditDiagnostic> notes)
    {
        var siblings = document.SequenceFlows.Where(flow => flow.SourceRef == sourceId).ToList();

        var displaced = siblings.FirstOrDefault(flow => flow.IsDefault && flow.Id != defaultFlowId);

        if (displaced is not null && defaultFlowId is not null)
        {
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "SF-006",
                $"'{displaced.Name ?? displaced.Id}' is no longer the default flow — a source has only one.",
                displaced.Id));
        }

        var edited = document with
        {
            SequenceFlows = [.. document.SequenceFlows.Select(flow => flow.SourceRef == sourceId
                ? flow with { IsDefault = flow.Id == defaultFlowId }
                : flow)],
        };

        return document.FindFlowNode(sourceId) is { } source && source.DefaultFlowRef != defaultFlowId
            ? WithElement(edited, source with { DefaultFlowRef = defaultFlowId })
            : edited;
    }

    // --- type switching ---------------------------------------------------------

    private static BpmnEditResult ApplySwitchType(
        BpmnDocument document,
        SwitchElementTypeCommand command,
        EditingOptions options)
    {
        if (document.Find(command.ElementId) is not BpmnFlowNode node)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-080",
                $"'{command.ElementId}' is not an element whose type can be switched.",
                command.ElementId));
        }

        var typeOptions = BpmnElementInspector.SwitchOptionsFor(document, node);
        var target = typeOptions.FirstOrDefault(option =>
            option.Type == command.Type && option.EventDefinition == command.EventDefinition);

        if (typeOptions.Count <= 1 || target is null)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-081",
                $"A {BpmnElementInspector.TypeLabel(node.Type)} cannot be switched to "
                + $"{BpmnElementInspector.TypeLabel(command.Type)}: section 7 allows switching only within "
                + "the same element category.",
                command.ElementId));
        }

        if (target.IsCurrent)
        {
            return BpmnEditResult.Reject(document, new BpmnEditDiagnostic(
                BpmnEditSeverity.Rejected,
                "EDIT-081",
                "The element is already that type.",
                command.ElementId));
        }

        var notes = new List<BpmnEditDiagnostic>();
        var group = BpmnElementInspector.SwitchGroupOf(node.Type);

        if (Incompatible(document, node, command, group) is { } blocker) return BpmnEditResult.Reject(document, blocker);

        var switched = group == BpmnSwitchGroup.EventTrigger
            ? WithTrigger(node, command.EventDefinition, notes)
            : node with { Type = command.Type };

        var shed = Shed(switched, node, command.Type, notes);
        if (BpmnElementInspector.CategoryOf(shed.Type) == BpmnElementCategory.Event)
        {
            var legalNode = ApplyEventLegality(document, shed, options, notes, out var eventProblem);
            if (eventProblem is not null) return BpmnEditResult.Reject(document, eventProblem);
            shed = legalNode!;
        }

        var edited = WithElement(document, shed);

        // A Parallel or Event-Based Gateway has no "otherwise" branch, so the default has to go —
        // from the node and from the flow that claimed it, together.
        if (shed.DefaultFlowRef is null && node.DefaultFlowRef is not null)
        {
            edited = WithDefaultFlow(edited, node.Id, null, []);
        }

        return BpmnEditResult.Applied(WithSwitchedDi(edited, node.Id, command.Type), notes);
    }

    /// <summary>
    /// Enforces legality for an event context changed by a command.
    /// </summary>
    /// <remarks>
    /// Existing invalid Events are left alone by unrelated edits. A changed trigger or owner must
    /// produce a valid Event; the alternate policy may repair only an interruption mismatch.
    /// </remarks>
    private static BpmnFlowNode? ApplyEventLegality(
        BpmnDocument document,
        BpmnFlowNode candidate,
        EditingOptions options,
        List<BpmnEditDiagnostic> diagnostics,
        out BpmnEditDiagnostic? rejection)
    {
        if (BpmnElementInspector.IsLegalEvent(document, candidate))
        {
            rejection = null;
            return candidate;
        }

        if (options.EventEditPolicy == BpmnEventEditPolicy.AutoAdjustInterrupting
            && BpmnElementInspector.IsTriggerLegalInContext(document, candidate, candidate.EventDefinition)
            && BpmnElementInspector.TryGetRequiredInterruptingValue(document, candidate, out var required))
        {
            var adjusted = candidate with { IsInterrupting = required };
            if (BpmnElementInspector.IsLegalEvent(document, adjusted))
            {
                diagnostics.Add(new BpmnEditDiagnostic(
                    BpmnEditSeverity.Info,
                    "EDIT-084",
                    $"'{candidate.Name ?? candidate.Id}' was set to "
                    + $"{(required ? "interrupting" : "non-interrupting")} because its BPMN event trigger requires that state.",
                    candidate.Id));
                rejection = null;
                return adjusted;
            }
        }

        var triggerAllowed = BpmnElementInspector.IsTriggerLegalInContext(
            document,
            candidate,
            candidate.EventDefinition);
        string message;
        if (!triggerAllowed)
        {
            message = $"A {candidate.Type} cannot carry a {candidate.EventDefinition} trigger in this event context.";
        }
        else if (BpmnElementInspector.TryGetRequiredInterruptingValue(document, candidate, out var requiredState))
        {
            message = $"'{candidate.Name ?? candidate.Id}' must be "
                + $"{(requiredState ? "interrupting" : "non-interrupting")} for its {candidate.EventDefinition} trigger.";
        }
        else
        {
            message = $"'{candidate.Name ?? candidate.Id}' must have a valid container and Boundary host context.";
        }

        rejection = new BpmnEditDiagnostic(
            BpmnEditSeverity.Rejected,
            "EDIT-084",
            message,
            candidate.Id);
        return null;
    }

    /// <summary>
    /// The conversions that cannot be made at all, as opposed to the values that merely do not survive.
    /// </summary>
    /// <remarks>
    /// Section 7 rejects a switch atomically when "the resulting element or connections would
    /// violate BPMN rules". These are the cases where something else in the document depends on the
    /// type being what it is, so the user has to deal with that first — clearing it silently would
    /// destroy work they did not ask to lose.
    /// </remarks>
    private static BpmnEditDiagnostic? Incompatible(
        BpmnDocument document,
        BpmnFlowNode node,
        SwitchElementTypeCommand command,
        BpmnSwitchGroup group)
    {
        // EV-002: a Cancel Event belongs to a Transaction. Demoting the Transaction would strand it.
        if (group == BpmnSwitchGroup.EmbeddedSubProcess
            && node.Type == BpmnFlowNodeType.Transaction
            && command.Type != BpmnFlowNodeType.Transaction)
        {
            var cancel = document.FlowNodes.FirstOrDefault(candidate =>
                candidate.AttachedToRef == node.Id && candidate.EventDefinition == BpmnEventDefinitionType.Cancel);

            if (cancel is not null)
            {
                return new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-083",
                    $"'{cancel.Name ?? cancel.Id}' is a Cancel Boundary Event, which only a Transaction may carry. "
                    + "Remove it before changing the type.",
                    node.Id);
            }

            var cancelEnd = document.FlowNodes.FirstOrDefault(candidate =>
                candidate.ContainerId == node.Id && candidate.EventDefinition == BpmnEventDefinitionType.Cancel);

            if (cancelEnd is not null)
            {
                return new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-083",
                    $"'{cancelEnd.Name ?? cancelEnd.Id}' is a Cancel End Event inside this Transaction. "
                    + "Remove it before changing the type.",
                    node.Id);
            }
        }

        // GW-004: an Event-Based Gateway decides by which event happens first, so every outgoing
        // branch must lead to something that can catch one.
        if (command.Type == BpmnFlowNodeType.EventBasedGateway)
        {
            var wrong = document.SequenceFlows
                .Where(flow => flow.SourceRef == node.Id)
                .Select(flow => document.FindFlowNode(flow.TargetRef))
                .FirstOrDefault(target => target is not null && !CanFollowEventGateway(target));

            if (wrong is not null)
            {
                return new BpmnEditDiagnostic(
                    BpmnEditSeverity.Rejected,
                    "EDIT-083",
                    $"An Event-Based Gateway can only branch to catching Events or Receive Tasks, and "
                    + $"'{wrong.Name ?? wrong.Id}' is neither.",
                    node.Id);
            }
        }

        return null;
    }

    private static bool CanFollowEventGateway(BpmnFlowNode target) =>
        target.Type == BpmnFlowNodeType.IntermediateCatchEvent || target.Type == BpmnFlowNodeType.ReceiveTask;

    /// <summary>Clears what the new type cannot carry, saying so each time (section 7).</summary>
    private static BpmnFlowNode Shed(
        BpmnFlowNode switched,
        BpmnFlowNode original,
        BpmnFlowNodeType target,
        List<BpmnEditDiagnostic> notes)
    {
        var result = switched;

        // Only a Receive Task can start its Process by receiving.
        if (original.Instantiate && target != BpmnFlowNodeType.ReceiveTask)
        {
            result = result with { Instantiate = false };
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-082",
                $"'instantiate' was cleared: a {BpmnElementInspector.TypeLabel(target)} does not instantiate its Process.",
                original.Id));
        }

        if (result.MessageRef is not null
            && target is not (BpmnFlowNodeType.SendTask or BpmnFlowNodeType.ReceiveTask))
        {
            result = result with { MessageRef = null };
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-082",
                $"'messageRef' was cleared: a {BpmnElementInspector.TypeLabel(target)} does not send or receive a Message.",
                original.Id));
        }

        if ((result.ScriptFormat is not null || result.Script is not null) && target != BpmnFlowNodeType.ScriptTask)
        {
            result = result with { ScriptFormat = null, Script = null };
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-082",
                $"'scriptFormat' and the script were cleared: a {BpmnElementInspector.TypeLabel(target)} does not run a script.",
                original.Id));
        }

        if (result.ActivationCondition is not null && target != BpmnFlowNodeType.ComplexGateway)
        {
            result = result with { ActivationCondition = null };
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-082",
                $"The activation condition was cleared: a {BpmnElementInspector.TypeLabel(target)} is not a Complex Gateway.",
                original.Id));
        }

        // Parallel and Event-Based Gateways take every branch or the first event, never a default.
        if (result.DefaultFlowRef is not null
            && target is BpmnFlowNodeType.ParallelGateway or BpmnFlowNodeType.EventBasedGateway)
        {
            result = result with { DefaultFlowRef = null };
            notes.Add(new BpmnEditDiagnostic(
                BpmnEditSeverity.Info,
                "EDIT-082",
                $"The default flow was cleared: a {BpmnElementInspector.TypeLabel(target)} has no default branch.",
                original.Id));
        }

        return result;
    }

    /// <summary>
    /// Adjusts the DI that is tied to the type rather than to the geometry.
    /// </summary>
    /// <remarks>
    /// Bounds are untouched — section 7 preserves position and size, and every type within a switch
    /// group is drawn at the same size. <c>isMarkerVisible</c> is the exception: it says whether an
    /// Exclusive Gateway draws its X, and it means nothing on any other Gateway.
    /// </remarks>
    private static BpmnDocument WithSwitchedDi(BpmnDocument document, string elementId, BpmnFlowNodeType target) =>
        target == BpmnFlowNodeType.ExclusiveGateway
            ? document
            : document with
            {
                Diagrams = [.. document.Diagrams.Select(diagram => diagram with
                {
                    Plane = diagram.Plane with
                    {
                        Shapes = [.. diagram.Plane.Shapes.Select(shape =>
                            shape.BpmnElement == elementId && shape.IsMarkerVisible is not null
                                ? shape with { IsMarkerVisible = null }
                                : shape)],
                    },
                })],
            };

    // --- writing one element back -----------------------------------------------

    private static IReadOnlyList<T> Replace<T>(IReadOnlyList<T> items, string id, T replacement, Func<T, string> idOf) =>
        [.. items.Select(item => idOf(item) == id ? replacement : item)];

    /// <summary>Puts an edited element back where it came from.</summary>
    private static BpmnDocument WithElement(BpmnDocument document, object element) => element switch
    {
        BpmnFlowNode node => document with
        {
            FlowNodes = Replace(document.FlowNodes, node.Id, node, item => item.Id),
        },

        BpmnSequenceFlow flow => document with
        {
            SequenceFlows = Replace(document.SequenceFlows, flow.Id, flow, item => item.Id),
        },

        BpmnMessageFlow flow => document with
        {
            MessageFlows = Replace(document.MessageFlows, flow.Id, flow, item => item.Id),
        },

        BpmnAssociation association => document with
        {
            Associations = Replace(document.Associations, association.Id, association, item => item.Id),
        },

        BpmnDataAssociation association => document with
        {
            DataAssociations = Replace(document.DataAssociations, association.Id, association, item => item.Id),
        },

        BpmnTextAnnotation annotation => document with
        {
            TextAnnotations = Replace(document.TextAnnotations, annotation.Id, annotation, item => item.Id),
        },

        BpmnGroup group => document with
        {
            Groups = Replace(document.Groups, group.Id, group, item => item.Id),
        },

        BpmnDataObjectReference reference => document with
        {
            DataObjectReferences = Replace(document.DataObjectReferences, reference.Id, reference, item => item.Id),
        },

        BpmnDataStoreReference reference => document with
        {
            DataStoreReferences = Replace(document.DataStoreReferences, reference.Id, reference, item => item.Id),
        },

        BpmnLane lane => document with
        {
            Lanes = Replace(document.Lanes, lane.Id, lane, item => item.Id),
        },

        BpmnProcess process => document with
        {
            Processes = Replace(document.Processes, process.Id, process, item => item.Id),
        },

        BpmnParticipant participant when document.Collaboration is { } collaboration => document with
        {
            Collaboration = collaboration with
            {
                Participants = Replace(collaboration.Participants, participant.Id, participant, item => item.Id),
            },
        },

        BpmnCollaboration collaboration => document with { Collaboration = collaboration },

        BpmnMessage message => document with
        {
            Catalog = document.Catalog with { Messages = Replace(document.Catalog.Messages, message.Id, message, item => item.Id) },
        },

        BpmnSignal signal => document with
        {
            Catalog = document.Catalog with { Signals = Replace(document.Catalog.Signals, signal.Id, signal, item => item.Id) },
        },

        BpmnError error => document with
        {
            Catalog = document.Catalog with { Errors = Replace(document.Catalog.Errors, error.Id, error, item => item.Id) },
        },

        BpmnEscalation escalation => document with
        {
            Catalog = document.Catalog with { Escalations = Replace(document.Catalog.Escalations, escalation.Id, escalation, item => item.Id) },
        },

        _ => document,
    };
}
