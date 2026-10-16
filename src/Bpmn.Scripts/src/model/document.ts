import type {
  BpmnDataObjectReferenceContract,
  BpmnDataStoreReferenceContract,
  BpmnDiagramContract,
  BpmnDocumentContract,
  BpmnEdgeContract,
  BpmnFlowNodeContract,
  BpmnGroupContract,
  BpmnLaneContract,
  BpmnParticipantContract,
  BpmnShapeContract,
  BpmnTextAnnotationContract,
} from './contracts.js';

/**
 * Anything the document can address by id. A discriminated union rather than a bare
 * object, so every consumer narrows explicitly instead of casting.
 */
export type IndexedElement =
  | { readonly kind: 'flowNode'; readonly value: BpmnFlowNodeContract }
  | { readonly kind: 'participant'; readonly value: BpmnParticipantContract }
  | { readonly kind: 'lane'; readonly value: BpmnLaneContract }
  | { readonly kind: 'textAnnotation'; readonly value: BpmnTextAnnotationContract }
  | { readonly kind: 'group'; readonly value: BpmnGroupContract }
  | { readonly kind: 'dataObjectReference'; readonly value: BpmnDataObjectReferenceContract }
  | { readonly kind: 'dataStoreReference'; readonly value: BpmnDataStoreReferenceContract };

/**
 * Read-only lookup structures over a document.
 *
 * Built once per loaded document. Specification section 1.0 requires the editor to keep
 * an id index for reference resolution, and the renderer needs container-scoped lookups
 * on every pass; recomputing either by scanning flat arrays turns rendering quadratic on
 * the larger fixtures.
 */
export class BpmnDocumentIndex {
  readonly #elements = new Map<string, IndexedElement>();
  readonly #nodesByContainer = new Map<string, BpmnFlowNodeContract[]>();
  readonly #boundaryEventsByHost = new Map<string, BpmnFlowNodeContract[]>();
  readonly #laneOfNode = new Map<string, string>();
  readonly #participantOfProcess = new Map<string, string>();

  public constructor(public readonly document: BpmnDocumentContract) {
    for (const node of document.flowNodes) {
      this.#elements.set(node.id, { kind: 'flowNode', value: node });
      push(this.#nodesByContainer, node.containerId, node);
      if (node.attachedToRef !== null) push(this.#boundaryEventsByHost, node.attachedToRef, node);
    }

    for (const participant of document.collaboration?.participants ?? []) {
      this.#elements.set(participant.id, { kind: 'participant', value: participant });
      if (participant.processRef !== null) {
        this.#participantOfProcess.set(participant.processRef, participant.id);
      }
    }

    for (const lane of document.lanes) {
      this.#elements.set(lane.id, { kind: 'lane', value: lane });
      for (const nodeId of lane.flowNodeRefs) this.#laneOfNode.set(nodeId, lane.id);
    }

    for (const annotation of document.textAnnotations) {
      this.#elements.set(annotation.id, { kind: 'textAnnotation', value: annotation });
    }
    for (const group of document.groups) {
      this.#elements.set(group.id, { kind: 'group', value: group });
    }
    for (const reference of document.dataObjectReferences) {
      this.#elements.set(reference.id, { kind: 'dataObjectReference', value: reference });
    }
    for (const reference of document.dataStoreReferences) {
      this.#elements.set(reference.id, { kind: 'dataStoreReference', value: reference });
    }
  }

  public find(id: string): IndexedElement | undefined {
    return this.#elements.get(id);
  }

  public flowNode(id: string): BpmnFlowNodeContract | undefined {
    const found = this.#elements.get(id);
    return found?.kind === 'flowNode' ? found.value : undefined;
  }

  /** Flow nodes whose containerId is the given Process or Sub-Process. */
  public nodesIn(containerId: string): readonly BpmnFlowNodeContract[] {
    return this.#nodesByContainer.get(containerId) ?? [];
  }

  /** Boundary events attached to the given Activity (section 2.5). */
  public boundaryEventsOf(hostId: string): readonly BpmnFlowNodeContract[] {
    return this.#boundaryEventsByHost.get(hostId) ?? [];
  }

  /**
   * Lane membership, read from the Lane's own flowNodeRefs. Membership is projected
   * onto the node, never the other way round (section 2.8).
   */
  public laneOf(nodeId: string): string | undefined {
    return this.#laneOfNode.get(nodeId);
  }

  /** The Participant whose processRef names this Process, if the document has one. */
  public participantOfProcess(processId: string): string | undefined {
    return this.#participantOfProcess.get(processId);
  }
}

/** Shape/edge lookup for one diagram's plane, keyed by the referenced BPMN element. */
export class BpmnPlaneIndex {
  readonly #shapes = new Map<string, BpmnShapeContract>();
  readonly #edges = new Map<string, BpmnEdgeContract>();

  public constructor(public readonly diagram: BpmnDiagramContract) {
    for (const shape of diagram.plane.shapes) this.#shapes.set(shape.bpmnElement, shape);
    for (const edge of diagram.plane.edges) this.#edges.set(edge.bpmnElement, edge);
  }

  public shapeFor(elementId: string): BpmnShapeContract | undefined {
    return this.#shapes.get(elementId);
  }

  public edgeFor(elementId: string): BpmnEdgeContract | undefined {
    return this.#edges.get(elementId);
  }

  public get shapes(): readonly BpmnShapeContract[] {
    return this.diagram.plane.shapes;
  }

  public get edges(): readonly BpmnEdgeContract[] {
    return this.diagram.plane.edges;
  }
}

/** Resolves the diagram to render: the requested one, else the first declared. */
export function resolveDiagram(
  document: BpmnDocumentContract,
  diagramId: string | null,
): BpmnDiagramContract | undefined {
  if (diagramId === null) return document.diagrams[0];
  return document.diagrams.find((diagram) => diagram.id === diagramId) ?? document.diagrams[0];
}

function push<T>(map: Map<string, T[]>, key: string, value: T): void {
  const existing = map.get(key);
  if (existing === undefined) {
    map.set(key, [value]);
  } else {
    existing.push(value);
  }
}
