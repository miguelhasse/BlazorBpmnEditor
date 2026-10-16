using System.Globalization;
using System.Xml.Linq;

using Hasseware.AspNetCore.Components.Bpmn.Model;
using Hasseware.AspNetCore.Components.Bpmn.Model.Di;
using Hasseware.AspNetCore.Components.Bpmn.Model.Extensions;

namespace Hasseware.AspNetCore.Components.Bpmn.Serialization;

/// <summary>
/// Builds a complete BPMN document from the semantic model, in schema order.
/// </summary>
/// <remarks>
/// <para>
/// Deterministic by construction: nothing here reads the clock, a GUID or the source file, so the
/// same model always produces the same tree. That is what lets the round-trip suite compare two
/// exports directly instead of diffing XML semantically.
/// </para>
/// <para>
/// Also the first half of a fidelity export. The merger reconciles this tree onto the document that
/// was read, rather than reimplementing element construction a second time with different bugs.
/// </para>
/// </remarks>
internal sealed class BpmnCanonicalWriter(BpmnDocument document, BpmnWriteOptions options)
{
    private readonly List<BpmnExportDiagnostic> _diagnostics = [];
    private readonly IReadOnlyDictionary<string, string> _qnamePrefixes = CreateQNamePrefixes(document);

    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    private static readonly XNamespace Model = BpmnXmlNames.Model;
    private static readonly XNamespace Di = BpmnXmlNames.Diagram;
    private static readonly XNamespace Dc = BpmnXmlNames.DiagramCommon;
    private static readonly XNamespace Dd = BpmnXmlNames.DiagramInterchange;

    /// <summary>Every finding produced while building.</summary>
    internal IReadOnlyList<BpmnExportDiagnostic> Diagnostics => _diagnostics;

    /// <summary>Builds the document.</summary>
    internal XDocument Build()
    {
        var root = new XElement(Model + "definitions");

        Set(root, "id", document.Definitions.Id);
        Set(root, "targetNamespace", document.Definitions.TargetNamespace);
        Set(root, "name", document.Definitions.Name);
        Set(root, "expressionLanguage", document.Definitions.ExpressionLanguage);
        Set(root, "typeLanguage", document.Definitions.TypeLanguage);
        Set(root, "exporter", options.Exporter ?? document.Definitions.Exporter);
        Set(root, "exporterVersion", options.ExporterVersion ?? document.Definitions.ExporterVersion);
        WriteExtras(root, document.Definitions.Extras);

        WriteCatalog(root);

        foreach (var process in document.Processes) root.Add(WriteProcess(process));
        if (document.Collaboration is { } collaboration) root.Add(WriteCollaboration(collaboration));

        // Root content the catalog does not model shares the rootElement slot with the Processes,
        // so its rank cannot decide where it goes. It is written after them because that is where
        // files that have any tend to put it.
        foreach (var preserved in document.Catalog.Preserved) root.Add(WriteExtensionElement(preserved));

        foreach (var diagram in document.Diagrams) root.Add(WriteDiagram(diagram));

        // Relationships close tDefinitions, after the diagrams.
        foreach (var relationship in document.Catalog.Relationships)
        {
            var element = new XElement(Model + "relationship");
            Set(element, "id", relationship.Id);
            Set(element, "type", relationship.Type);
            Set(element, "direction", relationship.Direction);
            foreach (var source in relationship.Sources) element.Add(new XElement(Model + "source", source));
            foreach (var target in relationship.Targets) element.Add(new XElement(Model + "target", target));
            root.Add(element);
        }

        BpmnSchemaOrder.Sort(root);

        // Declared last, once the tree is complete: only then is it knowable which namespaces the
        // document actually uses, and declaring one it does not is noise in every consumer's diff.
        DeclareNamespaces(root);

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    // --- namespaces -----------------------------------------------------------

    /// <summary>
    /// Declares every namespace the document uses, reusing the prefixes it was written with.
    /// </summary>
    /// <remarks>
    /// Section 10.6 requires every namespace an element uses to be declared in the export and to
    /// resolve to the same URI the model holds. Reusing the original prefixes is not required by
    /// anything — references resolve by URI — but a file that comes back with <c>ns0:</c> in place
    /// of <c>zeebe:</c> reads as though the editor rewrote content it only copied.
    /// </remarks>
    private void DeclareNamespaces(XElement root)
    {
        var used = CollectNamespaces(root);
        foreach (var uri in _qnamePrefixes.Keys) used.Add(uri);
        var declared = root.Attributes()
            .Where(attribute => attribute.IsNamespaceDeclaration)
            .ToDictionary(
                attribute => attribute.Name.LocalName == "xmlns" ? string.Empty : attribute.Name.LocalName,
                attribute => attribute.Value,
                StringComparer.Ordinal);

        void Declare(string prefix, string uri)
        {
            if (!used.Contains(uri)) return;
            if (declared.TryGetValue(prefix, out var existing))
            {
                if (existing == uri) return;
                return;
            }

            root.Add(prefix.Length == 0
                ? new XAttribute("xmlns", uri)
                : new XAttribute(XNamespace.Xmlns + prefix, uri));
            declared.Add(prefix, uri);
        }

        // The document's own declarations first, so its prefixes win.
        foreach (var (prefix, uri) in document.Definitions.NamespaceDeclarations)
        {
            Declare(prefix, uri);
        }

        if (!declared.TryGetValue("bpmn", out var bpmnNamespace) || bpmnNamespace == BpmnXmlNames.Model.NamespaceName)
        {
            Declare("bpmn", BpmnXmlNames.Model.NamespaceName);
        }
        Declare("bpmndi", BpmnXmlNames.Diagram.NamespaceName);
        Declare("dc", BpmnXmlNames.DiagramCommon.NamespaceName);
        Declare("di", BpmnXmlNames.DiagramInterchange.NamespaceName);
        Declare("xsi", Xsi.NamespaceName);

        foreach (var (uri, prefix) in _qnamePrefixes)
        {
            Declare(prefix, uri);
        }

        var preferredPrefixes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["http://camunda.org/schema/zeebe/1.0"] = "zeebe",
            ["http://camunda.org/schema/1.0/bpmn"] = "camunda",
            ["http://camunda.org/schema/modeler/1.0"] = "modeler",
        };
        var generated = 1;
        foreach (var uri in used.OrderBy(value => value, StringComparer.Ordinal))
        {
            if (uri.Length == 0 || declared.Values.Contains(uri, StringComparer.Ordinal)) continue;

            string prefix;
            if (preferredPrefixes.TryGetValue(uri, out var preferred) && !declared.ContainsKey(preferred))
            {
                prefix = preferred;
            }
            else
            {
                do
                {
                    prefix = $"ns{generated++.ToString(CultureInfo.InvariantCulture)}";
                }
                while (declared.ContainsKey(prefix));
            }

            Declare(prefix, uri);
            _diagnostics.Add(new BpmnExportDiagnostic(
                BpmnExportSeverity.Info,
                $"Namespace '{uri}' had no declared prefix in the model and was declared as '{prefix}'."));
        }
    }

    private static Dictionary<string, string> CreateQNamePrefixes(BpmnDocument document)
    {
        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);
        var occupied = new HashSet<string>(document.Definitions.NamespaceDeclarations.Keys, StringComparer.Ordinal)
        {
            "bpmndi", "dc", "di", "xsi",
        };
        var shadowed = new HashSet<string>(StringComparer.Ordinal);

        if (document.Source is { } source)
        {
            var sourceRoot = source.Clone().Root;
            if (sourceRoot is not null)
            {
                foreach (var declaration in sourceRoot.DescendantsAndSelf()
                             .Attributes()
                             .Where(attribute => attribute.IsNamespaceDeclaration))
                {
                    var prefix = declaration.Name.LocalName == "xmlns" ? string.Empty : declaration.Name.LocalName;
                    if (prefix.Length == 0) continue;

                    occupied.Add(prefix);
                    if (document.Definitions.NamespaceDeclarations.TryGetValue(prefix, out var rootUri)
                        && !string.Equals(rootUri, declaration.Value, StringComparison.Ordinal))
                    {
                        shadowed.Add(prefix);
                    }
                }
            }
        }

        foreach (var uri in document.FlowNodes
                     .Select(node => node.Type == BpmnFlowNodeType.CallActivity
                         ? node.CalledElementQName?.NamespaceUri
                             ?? (node.CalledElementRef is null ? null : document.Definitions.TargetNamespace)
                         : null)
                     .Concat(FormalExpressions(document).Select(expression => expression.TypeNamespaceUri))
                     .Concat(FormalExpressions(document)
                         .Where(expression => expression.TypeQName is null)
                         .Select(_ => BpmnXmlNames.Model.NamespaceName))
                     .Where(uri => !string.IsNullOrEmpty(uri))
                     .Distinct(StringComparer.Ordinal)
                     .OrderBy(uri => uri, StringComparer.Ordinal))
        {
            var existing = document.Definitions.NamespaceDeclarations
                .Where(pair => pair.Key.Length > 0
                    && pair.Value == uri
                    && !shadowed.Contains(pair.Key))
                .Select(pair => pair.Key)
                .FirstOrDefault();

            if (existing is not null)
            {
                prefixes[uri!] = existing;
                continue;
            }

            if (uri == BpmnXmlNames.Model.NamespaceName)
            {
                if (!document.Definitions.NamespaceDeclarations.TryGetValue("bpmn", out var bpmnNamespace)
                    || bpmnNamespace == BpmnXmlNames.Model.NamespaceName)
                {
                    prefixes[uri!] = "bpmn";
                    occupied.Add("bpmn");
                }
                else
                {
                    prefixes[uri!] = AllocatePrefix(occupied);
                }
                continue;
            }

            prefixes[uri!] = AllocatePrefix(occupied);
        }

        return prefixes;

        static string AllocatePrefix(HashSet<string> occupied)
        {
            var suffix = 0;
            string generated;
            do
            {
                generated = $"ns{suffix++.ToString(CultureInfo.InvariantCulture)}";
            }
            while (!occupied.Add(generated));

            return generated;
        }
    }

    private static IEnumerable<BpmnFormalExpression> FormalExpressions(BpmnDocument document)
    {
        foreach (var node in document.FlowNodes)
        {
            if (node.ActivationCondition is { } activation) yield return activation;
            if (node.StandardLoop?.LoopCondition is { } loopCondition) yield return loopCondition;

            if (node.MultiInstanceLoop is { } multi)
            {
                if (multi.LoopCardinality is { } cardinality) yield return cardinality;
                if (multi.CompletionCondition is { } completion) yield return completion;
                foreach (var behavior in multi.ComplexBehaviorDefinitions ?? [])
                {
                    if (behavior.Condition is { } condition) yield return condition;
                }
            }
        }

        foreach (var association in document.DataAssociations)
        {
            foreach (var assignment in association.Assignments)
            {
                yield return assignment.From;
                yield return assignment.To;
            }
        }
    }

    /// <summary>
    /// Every namespace the finished tree actually uses, on an element or a qualified attribute.
    /// </summary>
    /// <remarks>
    /// Read off the tree rather than predicted from the model, so preserved vendor content is
    /// covered without this having to know what preserved content exists.
    /// </remarks>
    private static HashSet<string> CollectNamespaces(XElement root)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);

        foreach (var element in root.DescendantsAndSelf())
        {
            used.Add(element.Name.NamespaceName);

            foreach (var attribute in element.Attributes())
            {
                if (!attribute.IsNamespaceDeclaration) used.Add(attribute.Name.NamespaceName);
            }
        }

        used.Remove(string.Empty);
        return used;
    }

    // --- global definitions ---------------------------------------------------

    private void WriteCatalog(XElement root)
    {
        var catalog = document.Catalog;

        foreach (var import in catalog.Imports)
        {
            var element = new XElement(Model + "import");
            Set(element, "importType", import.ImportType);
            Set(element, "location", import.Location);
            Set(element, "namespace", import.Namespace);
            root.Add(element);
        }

        foreach (var declaration in catalog.ExtensionDeclarations)
        {
            var element = new XElement(Model + "extension");
            Set(element, "id", declaration.Id);
            Set(element, "definition", declaration.Definition);
            if (declaration.MustUnderstand) Set(element, "mustUnderstand", "true");
            foreach (var content in declaration.Content) element.Add(WriteExtensionElement(content));
            root.Add(element);
        }

        foreach (var item in catalog.ItemDefinitions)
        {
            var element = new XElement(Model + "itemDefinition");
            Set(element, "id", item.Id);
            Set(element, "structureRef", item.StructureRef);
            if (item.IsCollection) Set(element, "isCollection", "true");
            Set(element, "itemKind", item.ItemKind);
            root.Add(element);
        }

        foreach (var message in catalog.Messages)
        {
            var element = new XElement(Model + "message");
            Set(element, "id", message.Id);
            Set(element, "name", message.Name);
            Set(element, "itemRef", message.ItemRef);
            WriteExtras(element, message.Extras);
            root.Add(element);
        }

        foreach (var signal in catalog.Signals)
        {
            var element = new XElement(Model + "signal");
            Set(element, "id", signal.Id);
            Set(element, "name", signal.Name);
            Set(element, "structureRef", signal.StructureRef);
            WriteExtras(element, signal.Extras);
            root.Add(element);
        }

        foreach (var error in catalog.Errors)
        {
            var element = new XElement(Model + "error");
            Set(element, "id", error.Id);
            Set(element, "name", error.Name);
            Set(element, "errorCode", error.ErrorCode);
            Set(element, "structureRef", error.StructureRef);
            WriteExtras(element, error.Extras);
            root.Add(element);
        }

        foreach (var escalation in catalog.Escalations)
        {
            var element = new XElement(Model + "escalation");
            Set(element, "id", escalation.Id);
            Set(element, "name", escalation.Name);
            Set(element, "escalationCode", escalation.EscalationCode);
            Set(element, "structureRef", escalation.StructureRef);
            WriteExtras(element, escalation.Extras);
            root.Add(element);
        }

        foreach (var store in catalog.DataStores)
        {
            var element = new XElement(Model + "dataStore");
            Set(element, "id", store.Id);
            Set(element, "name", store.Name);
            Set(element, "capacity", store.Capacity);
            if (store.IsUnlimited is { } unlimited) Set(element, "isUnlimited", Bool(unlimited));
            Set(element, "itemSubjectRef", store.ItemSubjectRef);
            root.Add(element);
        }

        foreach (var category in catalog.Categories)
        {
            var element = new XElement(Model + "category");
            Set(element, "id", category.Id);
            Set(element, "name", category.Name);

            foreach (var value in category.Values)
            {
                var child = new XElement(Model + "categoryValue");
                Set(child, "id", value.Id);
                Set(child, "value", value.Value);
                element.Add(child);
            }

            root.Add(element);
        }

        foreach (var resource in catalog.Resources)
        {
            var element = new XElement(Model + "resource");
            Set(element, "id", resource.Id);
            Set(element, "name", resource.Name);
            root.Add(element);
        }

        foreach (var contract in catalog.Interfaces)
        {
            var element = new XElement(Model + "interface");
            Set(element, "id", contract.Id);
            Set(element, "name", contract.Name);
            Set(element, "implementationRef", contract.ImplementationRef);

            foreach (var operation in contract.Operations)
            {
                var child = new XElement(Model + "operation");
                Set(child, "id", operation.Id);
                Set(child, "name", operation.Name);
                Set(child, "implementationRef", operation.ImplementationRef);
                if (operation.InMessageRef is { } inRef) child.Add(new XElement(Model + "inMessageRef", inRef));
                if (operation.OutMessageRef is { } outRef) child.Add(new XElement(Model + "outMessageRef", outRef));
                element.Add(child);
            }

            root.Add(element);
        }

        foreach (var globalTask in catalog.GlobalTasks)
        {
            var element = new XElement(Model + globalTask.Kind);
            Set(element, "id", globalTask.Id);
            Set(element, "name", globalTask.Name);
            root.Add(element);
        }

    }

    // --- processes ------------------------------------------------------------

    private XElement WriteProcess(BpmnProcess process)
    {
        var element = new XElement(Model + "process");
        Set(element, "id", process.Id);
        Set(element, "name", process.Name);
        if (process.IsExecutable is { } executable) Set(element, "isExecutable", Bool(executable));
        Set(element, "processType", process.ProcessType);
        if (process.IsClosed is { } closed) Set(element, "isClosed", Bool(closed));
        WriteExtras(element, process.Extras);

        WriteContainerContent(element, process.Id);
        BpmnSchemaOrder.Sort(element);
        return element;
    }

    /// <summary>
    /// Writes everything owned by one container, whether a Process or an embedded Sub-Process.
    /// </summary>
    /// <remarks>
    /// The model stores containment flat and keyed by <c>ContainerId</c>, so nesting is rebuilt
    /// here by filtering rather than by walking a tree. That is the same shape the reader flattened
    /// on the way in, which is what makes the two provably inverse.
    /// </remarks>
    private void WriteContainerContent(XElement container, string containerId)
    {
        foreach (var laneSet in document.LaneSets.Where(set => set.ContainerId == containerId))
        {
            // Nested lane sets are written inside their parent Lane, not here.
            if (document.Lanes.Any(lane => lane.ChildLaneSetId == laneSet.Id)) continue;

            container.Add(WriteLaneSet(laneSet));
        }

        foreach (var dataObject in document.DataObjects.Where(item => item.ContainerId == containerId))
        {
            var element = new XElement(Model + "dataObject");
            Set(element, "id", dataObject.Id);
            Set(element, "name", dataObject.Name);
            if (dataObject.IsCollection) Set(element, "isCollection", "true");
            Set(element, "itemSubjectRef", dataObject.ItemSubjectRef);
            WriteExtras(element, dataObject.Extras);
            container.Add(element);
        }

        foreach (var reference in document.DataObjectReferences.Where(item => item.ContainerId == containerId))
        {
            var element = new XElement(Model + "dataObjectReference");
            Set(element, "id", reference.Id);
            Set(element, "name", reference.Name);
            Set(element, "dataObjectRef", reference.DataObjectRef);
            WriteExtras(element, reference.Extras);
            WriteDataState(element, reference.State);
            container.Add(element);
        }

        foreach (var reference in document.DataStoreReferences.Where(item => item.ContainerId == containerId))
        {
            var element = new XElement(Model + "dataStoreReference");
            Set(element, "id", reference.Id);
            Set(element, "name", reference.Name);
            Set(element, "dataStoreRef", reference.DataStoreRef);
            WriteExtras(element, reference.Extras);
            WriteDataState(element, reference.State);
            container.Add(element);
        }

        foreach (var node in document.FlowNodes.Where(item => item.ContainerId == containerId))
        {
            container.Add(WriteFlowNode(node));
        }

        foreach (var flow in document.SequenceFlows.Where(item => item.ContainerId == containerId))
        {
            container.Add(WriteSequenceFlow(flow));
        }

        WriteArtifacts(container, containerId);
    }

    private static void WriteDataState(XElement owner, string? state)
    {
        if (state is null) return;

        var element = new XElement(Model + "dataState");
        Set(element, "name", state);
        owner.Add(element);
    }

    private XElement WriteLaneSet(BpmnLaneSet laneSet)
    {
        var element = new XElement(Model + "laneSet");

        // A lane set the reader named because the document did not gets its id left off again,
        // rather than gaining one the author never wrote.
        if (!laneSet.IsSynthetic) Set(element, "id", laneSet.Id);

        foreach (var laneId in laneSet.LaneIds)
        {
            var lane = document.Lanes.FirstOrDefault(item => item.Id == laneId);
            if (lane is null) continue;

            var laneElement = new XElement(Model + "lane");
            Set(laneElement, "id", lane.Id);
            Set(laneElement, "name", lane.Name);
            WriteExtras(laneElement, lane.Extras);

            foreach (var reference in lane.FlowNodeRefs)
            {
                laneElement.Add(new XElement(Model + "flowNodeRef", reference));
            }

            if (lane.ChildLaneSetId is { } childId
                && document.LaneSets.FirstOrDefault(set => set.Id == childId) is { } child)
            {
                var childElement = WriteLaneSet(child);
                childElement.Name = Model + "childLaneSet";
                laneElement.Add(childElement);
            }

            BpmnSchemaOrder.Sort(laneElement);
            element.Add(laneElement);
        }

        return element;
    }

    private XElement WriteFlowNode(BpmnFlowNode node)
    {
        var element = new XElement(Model + BpmnXmlNames.ElementNameFor(node.Type));
        Set(element, "id", node.Id);
        Set(element, "name", node.Name);

        if (node.Type == BpmnFlowNodeType.EventSubProcess) Set(element, "triggeredByEvent", "true");
        if (node.AttachedToRef is { } host) Set(element, "attachedToRef", host);
        if (node.Type == BpmnFlowNodeType.BoundaryEvent && !node.IsInterrupting) Set(element, "cancelActivity", "false");
        if (node.Type == BpmnFlowNodeType.StartEvent && !node.IsInterrupting) Set(element, "isInterrupting", "false");
        if (node.IsForCompensation) Set(element, "isForCompensation", "true");
        if (node.Instantiate) Set(element, "instantiate", "true");
        if (node.MessageRef is { } message && node.Type is BpmnFlowNodeType.SendTask or BpmnFlowNodeType.ReceiveTask)
        {
            Set(element, "messageRef", message);
        }
        if (node.Type == BpmnFlowNodeType.ScriptTask) Set(element, "scriptFormat", node.ScriptFormat);
        if (node.CalledElementQName is { } qname)
        {
            Set(element, "calledElement", FormatQName(qname));
        }
        else if (node.CalledElementRef is { } called)
        {
            Set(element, "calledElement", FormatQName(
                new BpmnQName(called, document.Definitions.TargetNamespace, called)));
        }
        if (node.DefaultFlowRef is { } defaultFlow) Set(element, "default", defaultFlow);

        if (node.EventDefinition is BpmnEventDefinitionType.ParallelMultiple)
        {
            Set(element, "parallelMultiple", "true");
        }

        WriteExtras(element, node.Extras);

        if (options.WriteFlowNodeReferences)
        {
            foreach (var flow in document.SequenceFlows.Where(item => item.TargetRef == node.Id))
            {
                element.Add(new XElement(Model + "incoming", flow.Id));
            }

            foreach (var flow in document.SequenceFlows.Where(item => item.SourceRef == node.Id))
            {
                element.Add(new XElement(Model + "outgoing", flow.Id));
            }
        }

        WriteDataAssociations(element, node.Id);
        WriteLoopCharacteristics(element, node);

        if (node.Type == BpmnFlowNodeType.ComplexGateway
            && node.ActivationCondition is { } activation)
        {
            WriteFormalExpression(element, "activationCondition", activation);
        }

        if (node.Type == BpmnFlowNodeType.ScriptTask) SetTextElement(element, "script", node.Script);

        foreach (var definition in node.EventDefinitions) WriteEventDefinition(element, definition, node.Id);

        if (node.FlowElementsOwned)
        {
            WriteContainerContent(element, node.Id);
        }

        BpmnSchemaOrder.Sort(element);
        return element;
    }

    private void WriteLoopCharacteristics(XElement owner, BpmnFlowNode node)
    {
        switch (node.LoopCharacteristics)
        {
            case BpmnLoopCharacteristics.Standard:
                var standard = node.StandardLoop ?? new BpmnStandardLoopDetails();
                var standardElement = new XElement(Model + "standardLoopCharacteristics");
                Set(standardElement, "testBefore", standard.TestBefore);
                if (standard.LoopMaximum is { } maximum)
                {
                    Set(standardElement, "loopMaximum", maximum.ToString(CultureInfo.InvariantCulture));
                }

                if (standard.LoopCondition is { } loopCondition)
                {
                    WriteFormalExpression(standardElement, "loopCondition", loopCondition);
                }

                owner.Add(standardElement);
                break;

            case BpmnLoopCharacteristics.MultiInstanceSequential:
            case BpmnLoopCharacteristics.MultiInstanceParallel:
                var multi = node.MultiInstanceLoop ?? new BpmnMultiInstanceLoopDetails(
                    IsSequential: node.LoopCharacteristics == BpmnLoopCharacteristics.MultiInstanceSequential);
                var multiElement = new XElement(Model + "multiInstanceLoopCharacteristics");
                Set(multiElement, "isSequential", multi.IsSequential);
                Set(multiElement, "behavior", multi.Behavior);
                WriteOptionalExpression(multiElement, "loopCardinality", multi.LoopCardinality);
                SetTextElement(multiElement, "loopDataInputRef", multi.LoopDataInputRef);
                WriteLoopDataItem(multiElement, "inputDataItem", multi.InputDataItem);
                WriteOptionalExpression(multiElement, "completionCondition", multi.CompletionCondition);
                SetTextElement(multiElement, "loopDataOutputRef", multi.LoopDataOutputRef);
                WriteLoopDataItem(multiElement, "outputDataItem", multi.OutputDataItem);
                foreach (var behavior in multi.ComplexBehaviorDefinitions ?? [])
                {
                    var behaviorElement = new XElement(Model + "complexBehaviorDefinition");
                    Set(behaviorElement, "event", behavior.Event);
                    WriteOptionalExpression(behaviorElement, "condition", behavior.Condition);
                    multiElement.Add(behaviorElement);
                }

                WriteExtras(multiElement, multi.Extras);
                BpmnSchemaOrder.Sort(multiElement);
                owner.Add(multiElement);
                break;

            case BpmnLoopCharacteristics.None:
            default:
                break;
        }
    }

    private string FormatQName(BpmnQName qname)
    {
        if (qname.NamespaceUri is null)
        {
            return qname.LexicalValue;
        }

        if (qname.NamespaceUri.Length == 0)
        {
            return qname.LocalName;
        }

        if (!_qnamePrefixes.TryGetValue(qname.NamespaceUri, out var prefix))
        {
            prefix = document.Definitions.NamespaceDeclarations
                .Where(pair => pair.Key.Length > 0 && pair.Value == qname.NamespaceUri)
                .Select(pair => pair.Key)
                .FirstOrDefault()
                ?? "bpmn";
        }

        return $"{prefix}:{qname.LocalName}";
    }

    private static void WriteLoopDataItem(XElement owner, string name, BpmnLoopDataItem? item)
    {
        if (item is null) return;
        var element = new XElement(Model + name);
        Set(element, "id", item.Id);
        Set(element, "name", item.Name);
        Set(element, "itemSubjectRef", item.ItemSubjectRef);
        Set(element, "isCollection", item.IsCollection);
        owner.Add(element);
    }

    private void WriteOptionalExpression(XElement owner, string name, BpmnFormalExpression? expression)
    {
        if (expression is not null) WriteFormalExpression(owner, name, expression);
    }

    private void WriteFormalExpression(XElement owner, string name, BpmnFormalExpression expression)
    {
        var element = new XElement(Model + name, expression.Body);
        Set(element, "language", expression.Language);
        var type = expression.TypeQName is null
            ? FormatQName(new BpmnQName(
                "tFormalExpression",
                BpmnXmlNames.Model.NamespaceName,
                "tFormalExpression"))
            : expression.TypeNamespaceUri is { } namespaceUri
                ? FormatQName(new BpmnQName(
                    expression.TypeQName[(expression.TypeQName.LastIndexOf(':') + 1)..],
                    namespaceUri,
                    expression.TypeQName))
                : expression.TypeQName;
        element.Add(new XAttribute(Xsi + "type", type));
        owner.Add(element);
    }

    private static void SetTextElement(XElement owner, string name, string? value)
    {
        if (value is not null) owner.Add(new XElement(Model + name, value));
    }

    private void WriteEventDefinition(XElement owner, BpmnEventDefinition definition, string nodeId)
    {
        var name = BpmnXmlNames.ElementNameFor(definition.Type);
        if (name is null)
        {
            _diagnostics.Add(new BpmnExportDiagnostic(
                BpmnExportSeverity.Dropped,
                $"Trigger '{definition.Type}' has no BPMN element of its own and was not written.",
                nodeId));
            return;
        }

        var element = new XElement(Model + name);
        Set(element, "id", definition.Id);
        Set(element, "name", definition.Name);
        Set(element, "activityRef", definition.ActivityRef);
        if (definition.WaitForCompletion is { } wait) Set(element, "waitForCompletion", Bool(wait));

        if (BpmnXmlNames.ReferenceAttributeFor(definition.Type) is { } attribute)
        {
            Set(element, attribute, definition.Ref);
        }

        WriteExtras(element, definition.Extras);

        if (definition.Expression is { } expression)
        {
            var childName = definition.ExpressionKind
                ?? (definition.Type == BpmnEventDefinitionType.Conditional ? "condition" : "timeDuration");

            element.Add(new XElement(Model + childName, expression));
        }

        owner.Add(element);
    }

    private static XElement WriteSequenceFlow(BpmnSequenceFlow flow)
    {
        var element = new XElement(Model + "sequenceFlow");
        Set(element, "id", flow.Id);
        Set(element, "name", flow.Name);
        Set(element, "sourceRef", flow.SourceRef);
        Set(element, "targetRef", flow.TargetRef);
        WriteExtras(element, flow.Extras);

        if (flow.ConditionExpression is { } condition)
        {
            var conditionElement = new XElement(Model + "conditionExpression", condition);
            Set(conditionElement, "language", flow.ConditionLanguage);

            // tFormalExpression is abstract in the substitution sense: the schema types
            // conditionExpression as tExpression, and a condition with a body needs the concrete
            // type named, or strict parsers reject it.
            conditionElement.Add(new XAttribute(Xsi + "type", "bpmn:tFormalExpression"));
            element.Add(conditionElement);
        }

        BpmnSchemaOrder.Sort(element);
        return element;
    }

    private void WriteDataAssociations(XElement owner, string ownerId)
    {
        foreach (var association in document.DataAssociations)
        {
            var isInput = association.Kind == BpmnDataAssociationKind.Input;
            var belongs = association.OwnerId == ownerId
                || association.OwnerId is null
                    && (isInput ? association.TargetRef == ownerId : association.SourceRefs.Contains(ownerId));
            if (!belongs) continue;

            var element = new XElement(Model + (isInput ? "dataInputAssociation" : "dataOutputAssociation"));
            Set(element, "id", association.Id);
            WriteExtras(element, association.Extras);

            foreach (var source in association.SourceRefs)
            {
                // The Activity supplies the endpoint the XML omits, so writing it back would add a
                // self-reference the document never had.
                if (!isInput && source == ownerId) continue;
                element.Add(new XElement(Model + "sourceRef", source));
            }

            if (!(isInput && association.TargetRef == ownerId))
            {
                element.Add(new XElement(Model + "targetRef", association.TargetRef));
            }

            if (association.Transformation is { } transformation)
            {
                element.Add(new XElement(Model + "transformation", transformation));
            }

            foreach (var assignment in association.Assignments)
            {
                var assignmentElement = new XElement(Model + "assignment");
                WriteFormalExpression(assignmentElement, "from", assignment.From);
                WriteFormalExpression(assignmentElement, "to", assignment.To);
                element.Add(assignmentElement);
            }

            BpmnSchemaOrder.Sort(element);
            owner.Add(element);
        }
    }

    private void WriteArtifacts(XElement container, string containerId)
    {
        foreach (var association in document.Associations.Where(item => item.ContainerId == containerId))
        {
            var element = new XElement(Model + "association");
            Set(element, "id", association.Id);
            Set(element, "sourceRef", association.SourceRef);
            Set(element, "targetRef", association.TargetRef);

            if (association.Direction != BpmnAssociationDirection.None)
            {
                Set(element, "associationDirection", association.Direction == BpmnAssociationDirection.One ? "One" : "Both");
            }

            WriteExtras(element, association.Extras);
            BpmnSchemaOrder.Sort(element);
            container.Add(element);
        }

        foreach (var group in document.Groups.Where(item => item.ContainerId == containerId))
        {
            var element = new XElement(Model + "group");
            Set(element, "id", group.Id);
            Set(element, "name", group.Name);
            Set(element, "categoryValueRef", group.CategoryValueRef);
            WriteExtras(element, group.Extras);
            container.Add(element);
        }

        foreach (var annotation in document.TextAnnotations.Where(item => item.ContainerId == containerId))
        {
            var element = new XElement(Model + "textAnnotation");
            Set(element, "id", annotation.Id);
            Set(element, "textFormat", annotation.TextFormat);
            WriteExtras(element, annotation.Extras);
            element.Add(new XElement(Model + "text", annotation.Text));
            container.Add(element);
        }
    }

    private XElement WriteCollaboration(BpmnCollaboration collaboration)
    {
        var element = new XElement(Model + "collaboration");
        Set(element, "id", collaboration.Id);
        Set(element, "name", collaboration.Name);
        WriteExtras(element, collaboration.Extras);

        foreach (var participant in collaboration.Participants)
        {
            var child = new XElement(Model + "participant");
            Set(child, "id", participant.Id);
            Set(child, "name", participant.Name);
            Set(child, "processRef", participant.ProcessRef);
            WriteExtras(child, participant.Extras);
            element.Add(child);
        }

        foreach (var flow in document.MessageFlows)
        {
            var child = new XElement(Model + "messageFlow");
            Set(child, "id", flow.Id);
            Set(child, "name", flow.Name);
            Set(child, "sourceRef", flow.SourceRef);
            Set(child, "targetRef", flow.TargetRef);
            Set(child, "messageRef", flow.MessageRef);
            WriteExtras(child, flow.Extras);
            element.Add(child);
        }

        WriteArtifacts(element, collaboration.Id);
        BpmnSchemaOrder.Sort(element);
        return element;
    }

    // --- DI -------------------------------------------------------------------

    private static XElement WriteDiagram(BpmnDiagram diagram)
    {
        var element = new XElement(Di + "BPMNDiagram");
        Set(element, "id", diagram.Id);
        Set(element, "name", diagram.Name);
        WriteDiagramExtras(element, diagram.Extras);

        var plane = new XElement(Di + "BPMNPlane");
        Set(plane, "id", diagram.Plane.Id);
        Set(plane, "bpmnElement", diagram.Plane.BpmnElement);
        WriteDiagramExtras(plane, diagram.Plane.Extras);

        foreach (var shape in diagram.Plane.Shapes)
        {
            var child = new XElement(Di + "BPMNShape");
            Set(child, "id", shape.Id);
            Set(child, "bpmnElement", shape.BpmnElement);
            if (shape.IsHorizontal is { } horizontal) Set(child, "isHorizontal", Bool(horizontal));
            if (shape.IsExpanded is { } expanded) Set(child, "isExpanded", Bool(expanded));
            if (shape.IsMarkerVisible is { } marker) Set(child, "isMarkerVisible", Bool(marker));
            WriteDiagramExtras(child, shape.Extras);
            child.Add(WriteBounds(shape.Bounds));
            WriteLabel(child, shape.Label);
            plane.Add(child);
        }

        foreach (var edge in diagram.Plane.Edges)
        {
            var child = new XElement(Di + "BPMNEdge");
            Set(child, "id", edge.Id);
            Set(child, "bpmnElement", edge.BpmnElement);
            WriteDiagramExtras(child, edge.Extras);

            foreach (var point in edge.Waypoints)
            {
                var waypoint = new XElement(Dd + "waypoint");
                Set(waypoint, "x", Number(point.X));
                Set(waypoint, "y", Number(point.Y));
                child.Add(waypoint);
            }

            WriteLabel(child, edge.Label);
            plane.Add(child);
        }

        element.Add(plane);

        foreach (var style in diagram.LabelStyles)
        {
            var child = new XElement(Di + "BPMNLabelStyle");
            Set(child, "id", style.Id);

            var font = new XElement(Dc + "Font");
            Set(font, "name", style.FontFamily);
            if (style.FontSize is { } size) Set(font, "size", Number(size));
            if (style.IsBold is { } bold) Set(font, "isBold", Bool(bold));
            if (style.IsItalic is { } italic) Set(font, "isItalic", Bool(italic));
            if (style.IsUnderline is { } underline) Set(font, "isUnderline", Bool(underline));
            if (style.IsStrikeThrough is { } strike) Set(font, "isStrikeThrough", Bool(strike));
            child.Add(font);

            element.Add(child);
        }

        return element;
    }

    private static XElement WriteBounds(Bounds bounds)
    {
        var element = new XElement(Dc + "Bounds");
        Set(element, "x", Number(bounds.X));
        Set(element, "y", Number(bounds.Y));
        Set(element, "width", Number(bounds.Width));
        Set(element, "height", Number(bounds.Height));
        return element;
    }

    private static void WriteLabel(XElement owner, BpmnLabel? label)
    {
        if (label is null) return;

        // An empty BPMNLabel is meaningful: section 10.1 makes label visibility depend on the
        // element's presence, not on its bounds, so it is written even with nothing inside.
        var element = new XElement(Di + "BPMNLabel");
        Set(element, "id", label.Id);
        Set(element, "labelStyle", label.StyleId);
        if (label.Bounds is { } bounds) element.Add(WriteBounds(bounds));
        owner.Add(element);
    }

    // --- preserved content ----------------------------------------------------

    /// <summary>
    /// Marks an element whose model captures foreign attributes, so the fidelity merge may treat
    /// the canonical element's foreign attributes as authoritative.
    /// </summary>
    internal sealed class CapturesForeignAttributes
    {
        public static readonly CapturesForeignAttributes Instance = new();
    }

    private static void WriteExtras(XElement owner, BpmnElementExtras extras)
    {
        owner.AddAnnotation(CapturesForeignAttributes.Instance);
        if (extras.IsEmpty) return;

        foreach (var (key, value) in extras.ForeignAttributes)
        {
            owner.SetAttributeValue(XName.Get(key), value);
        }

        if (extras.Documentation is { } documentation)
        {
            owner.Add(new XElement(Model + "documentation", documentation));
        }

        if (extras.Extensions.Count > 0)
        {
            var container = new XElement(Model + "extensionElements");
            foreach (var extension in extras.Extensions) container.Add(WriteExtensionElement(extension));
            owner.Add(container);
        }
    }

    private static void WriteDiagramExtras(XElement owner, BpmnElementExtras extras)
    {
        owner.AddAnnotation(CapturesForeignAttributes.Instance);
        if (extras.IsEmpty) return;

        foreach (var (key, value) in extras.ForeignAttributes)
        {
            owner.SetAttributeValue(XName.Get(key), value);
        }

        if (extras.Extensions.Count == 0) return;

        var container = new XElement(Dd + "extension");
        foreach (var extension in extras.Extensions) container.Add(WriteExtensionElement(extension));
        owner.Add(container);
    }

    private static XElement WriteExtensionElement(BpmnExtensionElement element)
    {
        XNamespace ns = element.Namespace;
        var written = new XElement(ns + element.LocalName);

        foreach (var (key, value) in element.Attributes)
        {
            written.SetAttributeValue(XName.Get(key), value);
        }

        foreach (var child in element.Children) written.Add(WriteExtensionElement(child));

        if (element.Text is { } text) written.Add(new XText(text));

        return written;
    }

    // --- primitives -----------------------------------------------------------

    private static void Set(XElement element, string name, string? value)
    {
        if (value is null) return;
        element.SetAttributeValue(name, value);
    }

    private static void Set(XElement element, string name, bool? value)
    {
        if (value is { } flag) element.SetAttributeValue(name, Bool(flag));
    }

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>
    /// Formats a coordinate the way BPMN files do, and identically on every machine.
    /// </summary>
    /// <remarks>
    /// Invariant culture is not a nicety here: a Swedish or German locale would write a decimal
    /// comma, and <c>dc:Bounds/@x</c> is an <c>xsd:double</c>. The round-trip suite would catch it,
    /// but only when run on a machine set to such a locale.
    /// </remarks>
    private static string Number(double value) =>
        value == Math.Floor(value) && double.IsFinite(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);
}
