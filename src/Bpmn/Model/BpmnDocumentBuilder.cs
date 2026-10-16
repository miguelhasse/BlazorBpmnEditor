using Hasseware.AspNetCore.Components.Bpmn.Model.Di;

namespace Hasseware.AspNetCore.Components.Bpmn.Model;

/// <summary>
/// Builds a document and its DI together.
/// </summary>
/// <remarks>
/// <para>
/// Every drawable element needs a semantic object and exactly one BPMNShape or BPMNEdge
/// (specification section 9.3). Adding those separately is how a document ends up with geometry
/// that references nothing, so this builder always creates both.
/// </para>
/// <para>
/// Intended for tests, fixtures and the notation gallery — the places that need a precise model
/// without a file behind it. Documents from users come through the reader.
/// </para>
/// </remarks>
public sealed class BpmnDocumentBuilder
{
    private readonly List<BpmnProcess> _processes = [];
    private readonly List<BpmnParticipant> _participants = [];
    private readonly List<BpmnLaneSet> _laneSets = [];
    private readonly List<BpmnLane> _lanes = [];
    private readonly List<BpmnFlowNode> _flowNodes = [];
    private readonly List<BpmnSequenceFlow> _sequenceFlows = [];
    private readonly List<BpmnMessageFlow> _messageFlows = [];
    private readonly List<BpmnAssociation> _associations = [];
    private readonly List<BpmnTextAnnotation> _textAnnotations = [];
    private readonly List<BpmnGroup> _groups = [];
    private readonly List<BpmnDataObjectReference> _dataObjects = [];
    private readonly List<BpmnDataStoreReference> _dataStores = [];
    private readonly List<BpmnShape> _shapes = [];
    private readonly List<BpmnEdge> _edges = [];

    private readonly string _processId;
    private string? _collaborationId;

    /// <summary>Starts a document with a single Process.</summary>
    /// <param name="processId">The Process every element defaults into.</param>
    /// <param name="name">Optional Process name.</param>
    public BpmnDocumentBuilder(string processId = "Process_1", string? name = null)
    {
        _processId = processId;
        _processes.Add(new BpmnProcess(processId, name, IsExecutable: true));
    }

    /// <summary>Adds another Process without changing the default owner.</summary>
    public BpmnDocumentBuilder Process(string processId, string? name = null)
    {
        _processes.Add(new BpmnProcess(processId, name, IsExecutable: true));
        return this;
    }

    /// <summary>Adds a Pool for the Process, making the document a Collaboration.</summary>
    public BpmnDocumentBuilder Pool(
        string participantId,
        string? name,
        Bounds bounds,
        bool isHorizontal = true,
        string? processRef = null)
    {
        _collaborationId ??= "Collaboration_1";
        _participants.Add(new BpmnParticipant(participantId, name, processRef ?? _processId));
        _shapes.Add(new BpmnShape($"{participantId}_di", participantId, bounds, IsHorizontal: isHorizontal));
        return this;
    }

    /// <summary>Adds a black-box Pool with no Process reference or internal content.</summary>
    public BpmnDocumentBuilder BlackBoxPool(
        string participantId,
        string? name,
        Bounds bounds,
        bool isHorizontal = true)
    {
        _collaborationId ??= "Collaboration_1";
        _participants.Add(new BpmnParticipant(participantId, name, ProcessRef: null));
        _shapes.Add(new BpmnShape($"{participantId}_di", participantId, bounds, IsHorizontal: isHorizontal));
        return this;
    }

    /// <summary>Adds a Lane and the membership of the nodes it contains.</summary>
    public BpmnDocumentBuilder Lane(
        string laneId,
        string? name,
        Bounds bounds,
        IReadOnlyList<string> flowNodeRefs,
        bool isHorizontal = true,
        string laneSetId = "LaneSet_1",
        string? containerId = null)
    {
        if (!_laneSets.Any(set => set.Id == laneSetId))
        {
            _laneSets.Add(new BpmnLaneSet(laneSetId, containerId ?? _processId, []));
        }
        else if (containerId is not null && _laneSets.First(set => set.Id == laneSetId).ContainerId != containerId)
        {
            throw new ArgumentException("A Lane Set cannot belong to two containers.", nameof(containerId));
        }

        _laneSets[_laneSets.FindIndex(set => set.Id == laneSetId)] = _laneSets
            .First(set => set.Id == laneSetId) with
        {
            LaneIds = [.. _laneSets.First(set => set.Id == laneSetId).LaneIds, laneId],
        };

        _lanes.Add(new BpmnLane(laneId, name, laneSetId, flowNodeRefs));
        _shapes.Add(new BpmnShape($"{laneId}_di", laneId, bounds, IsHorizontal: isHorizontal));
        return this;
    }

    /// <summary>Adds a flow node and its shape.</summary>
    public BpmnDocumentBuilder Node(
        string id,
        BpmnFlowNodeType type,
        Bounds bounds,
        string? name = null,
        BpmnEventDefinitionType eventDefinition = BpmnEventDefinitionType.None,
        bool isInterrupting = true,
        BpmnLoopCharacteristics loop = BpmnLoopCharacteristics.None,
        bool isForCompensation = false,
        bool? isExpanded = null,
        string? attachedToRef = null,
        string? containerId = null,
        string? poolId = null)
    {
        var owner = containerId ?? _processId;
        var ownsContent = type is BpmnFlowNodeType.SubProcess
            or BpmnFlowNodeType.EventSubProcess
            or BpmnFlowNodeType.Transaction
            or BpmnFlowNodeType.AdHocSubProcess;

        _flowNodes.Add(new BpmnFlowNode(
            id,
            type,
            owner,
            name,
            eventDefinition,
            isInterrupting,
            attachedToRef,
            poolId,
            LaneId: null,
            isForCompensation,
            loop,
            Instantiate: false,
            CalledElementRef: type == BpmnFlowNodeType.CallActivity ? "GlobalProcess_1" : null,
            ownsContent));

        _shapes.Add(new BpmnShape($"{id}_di", id, bounds, IsExpanded: isExpanded));
        return this;
    }

    /// <summary>Adds a Sequence Flow and its edge.</summary>
    public BpmnDocumentBuilder Flow(
        string id,
        string sourceRef,
        string targetRef,
        IReadOnlyList<DiPoint> waypoints,
        string? name = null,
        string? condition = null,
        bool isDefault = false,
        string? containerId = null)
    {
        _sequenceFlows.Add(new BpmnSequenceFlow(
            id, sourceRef, targetRef, containerId ?? _processId, name, condition, isDefault));
        _edges.Add(new BpmnEdge($"{id}_di", id, waypoints));
        return this;
    }

    /// <summary>Adds a Message Flow and its edge.</summary>
    public BpmnDocumentBuilder MessageFlow(
        string id,
        string sourceRef,
        string targetRef,
        IReadOnlyList<DiPoint> waypoints,
        string? name = null)
    {
        _collaborationId ??= "Collaboration_1";
        _messageFlows.Add(new BpmnMessageFlow(id, sourceRef, targetRef, name));
        _edges.Add(new BpmnEdge($"{id}_di", id, waypoints));
        return this;
    }

    /// <summary>Adds an Association and its edge.</summary>
    public BpmnDocumentBuilder Association(
        string id,
        string sourceRef,
        string targetRef,
        IReadOnlyList<DiPoint> waypoints,
        BpmnAssociationDirection direction = BpmnAssociationDirection.None)
    {
        _associations.Add(new BpmnAssociation(id, sourceRef, targetRef, _processId, direction));
        _edges.Add(new BpmnEdge($"{id}_di", id, waypoints));
        return this;
    }

    /// <summary>Adds a Text Annotation and its shape.</summary>
    public BpmnDocumentBuilder TextAnnotation(string id, string text, Bounds bounds)
    {
        _textAnnotations.Add(new BpmnTextAnnotation(id, text, _processId));
        _shapes.Add(new BpmnShape($"{id}_di", id, bounds));
        return this;
    }

    /// <summary>Adds a Group and its shape.</summary>
    public BpmnDocumentBuilder Group(string id, string? name, Bounds bounds)
    {
        _groups.Add(new BpmnGroup(id, _processId, name));
        _shapes.Add(new BpmnShape($"{id}_di", id, bounds));
        return this;
    }

    /// <summary>Adds a Data Object Reference and its shape.</summary>
    public BpmnDocumentBuilder DataObject(string id, string? name, Bounds bounds, bool isCollection = false)
    {
        _dataObjects.Add(new BpmnDataObjectReference(id, _processId, name, IsCollection: isCollection));
        _shapes.Add(new BpmnShape($"{id}_di", id, bounds));
        return this;
    }

    /// <summary>Adds a Data Store Reference and its shape.</summary>
    public BpmnDocumentBuilder DataStore(string id, string? name, Bounds bounds)
    {
        _dataStores.Add(new BpmnDataStoreReference(id, _processId, name));
        _shapes.Add(new BpmnShape($"{id}_di", id, bounds));
        return this;
    }

    /// <summary>Produces the document with a single diagram over everything added.</summary>
    public BpmnDocument Build(string diagramId = "Diagram_1", string? diagramName = null)
    {
        var collaboration = _collaborationId is null
            ? null
            : new BpmnCollaboration(_collaborationId, null, _participants);

        return new BpmnDocument
        {
            Definitions = new BpmnDefinitions("Definitions_1", "http://hasseware.test/bpmn"),
            Processes = _processes,
            Collaboration = collaboration,
            LaneSets = _laneSets,
            Lanes = _lanes,
            FlowNodes = ApplyDefaultFlows(ApplyLaneMembership()),
            SequenceFlows = _sequenceFlows,
            MessageFlows = _messageFlows,
            Associations = _associations,
            TextAnnotations = _textAnnotations,
            Groups = _groups,
            DataObjectReferences = _dataObjects,
            DataStoreReferences = _dataStores,
            Diagrams =
            [
                new BpmnDiagram(
                    diagramId,
                    new BpmnPlane(
                        $"{diagramId}_plane",
                        collaboration?.Id ?? _processId,
                        _shapes,
                        _edges),
                    diagramName),
            ],
        };
    }

    /// <summary>
    /// Mirrors a flow's default marker back onto the node the attribute actually lives on.
    /// </summary>
    /// <remarks>
    /// BPMN puts <c>default</c> on the Gateway or Activity, not on the Sequence Flow, so a builder
    /// that recorded only the flow's side would produce a document whose default marker disappears
    /// the first time it is written out and read back.
    /// </remarks>
    private List<BpmnFlowNode> ApplyDefaultFlows(List<BpmnFlowNode> nodes)
    {
        var defaults = _sequenceFlows
            .Where(flow => flow.IsDefault)
            .ToDictionary(flow => flow.SourceRef, flow => flow.Id, StringComparer.Ordinal);

        if (defaults.Count == 0) return nodes;

        return [.. nodes.Select(node => defaults.TryGetValue(node.Id, out var flowId)
            ? node with { DefaultFlowRef = flowId }
            : node)];
    }

    /// <summary>
    /// Projects Lane membership and Pool ownership onto the nodes, exactly as the reader does, so
    /// a built document and a read one are indistinguishable downstream.
    /// </summary>
    private List<BpmnFlowNode> ApplyLaneMembership()
    {
        var laneOfNode = _lanes
            .SelectMany(lane => lane.FlowNodeRefs.Select(nodeId => (nodeId, lane.Id)))
            .ToDictionary(entry => entry.nodeId, entry => entry.Id, StringComparer.Ordinal);

        var nodes = _flowNodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        return _flowNodes.ConvertAll(node => node with
        {
            LaneId = laneOfNode.GetValueOrDefault(node.Id),
            PoolId = node.PoolId ?? PoolFor(node.ContainerId),
        });

        string? PoolFor(string containerId)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (seen.Add(containerId) && nodes.TryGetValue(containerId, out var container))
            {
                if (container.PoolId is not null) return container.PoolId;
                containerId = container.ContainerId;
            }

            return _participants.FirstOrDefault(participant => participant.ProcessRef == containerId)?.Id;
        }
    }
}
