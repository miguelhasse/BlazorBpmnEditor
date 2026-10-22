/**
 * The BPMN semantic + DI model as it crosses interop.
 *
 * Mirrors specification section 6 ("Editor Data Model") field for field. Two deliberate
 * shape deviations, both structural rather than semantic:
 *
 *  - DI is nested (diagram -> plane -> shapes/edges) instead of section 6's parallel
 *    shapeIds/edgeIds arrays, because every consumer walks it that way and the id
 *    indirection buys nothing over interop.
 *  - Flow elements are held in flat per-document collections and located by their
 *    containerId, exactly as section 6 models containment. Nested sub-process content is
 *    therefore preserved in place without a recursive payload.
 *
 * The C# Hasseware.AspNetCore.Components.Bpmn.Model types are the authoritative
 * definition of this shape; a golden-JSON fixture is asserted against both sides.
 */

/** Section 6 flowNode.type. */
export type BpmnFlowNodeType =
  | 'startEvent'
  | 'endEvent'
  | 'intermediateCatchEvent'
  | 'intermediateThrowEvent'
  | 'boundaryEvent'
  | 'task'
  | 'userTask'
  | 'serviceTask'
  | 'scriptTask'
  | 'manualTask'
  | 'businessRuleTask'
  | 'sendTask'
  | 'receiveTask'
  | 'subProcess'
  | 'eventSubProcess'
  | 'transaction'
  | 'adHocSubProcess'
  | 'callActivity'
  | 'exclusiveGateway'
  | 'inclusiveGateway'
  | 'parallelGateway'
  | 'complexGateway'
  | 'eventBasedGateway';

/** Section 6 flowNode.eventDefinition / section 1.1 trigger types. */
export type BpmnEventDefinitionType =
  | 'none'
  | 'message'
  | 'timer'
  | 'error'
  | 'escalation'
  | 'cancel'
  | 'compensation'
  | 'conditional'
  | 'link'
  | 'signal'
  | 'multiple'
  | 'parallelMultiple'
  | 'terminate';

/** Section 6 flowNode.loopCharacteristics. */
export type BpmnLoopCharacteristics =
  | 'none'
  | 'standard'
  | 'multiInstanceSequential'
  | 'multiInstanceParallel';

/** Section 6 association.direction. */
export type BpmnAssociationDirection = 'none' | 'one' | 'both';

/** Section 6 dataAssociation.kind. */
export type BpmnDataAssociationKind = 'input' | 'output';

export interface BoundsContract {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

export interface PointContract {
  readonly x: number;
  readonly y: number;
}

/**
 * Section 6 bpmnLabel: where a label is drawn and how it is styled.
 *
 * Carries no text. In BPMNDI the string always comes from the semantic element's name, so a
 * text field here could only ever shadow it with an empty value.
 */
export interface BpmnLabelContract {
  readonly color?: string | null;
  readonly id?: string | null;
  readonly bounds: BoundsContract | null;
  readonly styleId?: string | null;
}

/**
 * Section 6 bpmnShape. isHorizontal is present only on Pool/Lane shapes
 * (section 6 note, section 10.4) and is null everywhere else.
 */
export interface BpmnShapeContract {
  readonly backgroundColor?: string | null;
  readonly borderColor?: string | null;
  readonly id: string;
  readonly bpmnElement: string;
  readonly bounds: BoundsContract;
  readonly isHorizontal: boolean | null;
  readonly isExpanded: boolean | null;
  readonly isMarkerVisible?: boolean | null;
  readonly label: BpmnLabelContract | null;
}

export interface BpmnEdgeContract {
  readonly borderColor?: string | null;
  readonly id: string;
  readonly bpmnElement: string;
  readonly waypoints: readonly PointContract[];
  readonly label: BpmnLabelContract | null;
}

/** Section 6 bpmnPlane, with its shapes and edges nested (see file header). */
export interface BpmnPlaneContract {
  readonly id: string;
  readonly bpmnElement: string | null;
  readonly shapes: readonly BpmnShapeContract[];
  readonly edges: readonly BpmnEdgeContract[];
}

/** Section 6 bpmnDiagram. */
export interface BpmnDiagramContract {
  readonly id: string;
  readonly name: string | null;
  readonly plane: BpmnPlaneContract;
}

/** Section 6.1 — unknown vendor content, preserved verbatim and never interpreted. */
export interface BpmnExtensionElementContract {
  readonly namespace: string;
  readonly localName: string;
  readonly attributes: Readonly<Record<string, string>>;
  readonly children: readonly BpmnExtensionElementContract[];
  readonly text: string | null;
}

/**
 * Section 10.5 — the content an element carries without the editor interpreting it.
 *
 * `foreignAttributes` is keyed `{namespaceUri}localName`, exactly as the C# `BpmnElementExtras`
 * holds it: dictionary keys are not camel-cased on the way across.
 */
export interface BpmnElementExtrasContract {
  readonly documentation?: string | null;
  readonly foreignAttributes: Readonly<Record<string, string>>;
  readonly extensions?: readonly BpmnExtensionElementContract[];
}

/** Section 6 flowNode. */
export interface BpmnFlowNodeContract {
  readonly id: string;
  readonly name: string | null;
  readonly type: BpmnFlowNodeType;
  readonly eventDefinition: BpmnEventDefinitionType;
  readonly isInterrupting: boolean;
  readonly attachedToRef: string | null;
  readonly containerId: string;
  readonly poolId: string | null;
  readonly laneId: string | null;
  readonly isForCompensation: boolean;
  readonly loopCharacteristics: BpmnLoopCharacteristics;
  readonly instantiate?: boolean;
  readonly calledElementRef: string | null;
  readonly flowElementsOwned: boolean;
  /** Preserved vendor content; optional because hand-built documents may omit it. */
  readonly extras?: BpmnElementExtrasContract;
}

export interface BpmnSequenceFlowContract {
  readonly id: string;
  readonly name: string | null;
  readonly sourceRef: string;
  readonly targetRef: string;
  readonly containerId: string;
  readonly conditionExpression: string | null;
  readonly isDefault: boolean;
}

export interface BpmnMessageFlowContract {
  readonly id: string;
  readonly name: string | null;
  readonly sourceRef: string;
  readonly targetRef: string;
  readonly messageRef?: string | null;
}

export interface BpmnAssociationContract {
  readonly id: string;
  readonly sourceRef: string;
  readonly targetRef: string;
  readonly containerId: string;
  readonly direction: BpmnAssociationDirection;
}

export interface BpmnDataAssociationContract {
  readonly id: string;
  readonly kind: BpmnDataAssociationKind;
  readonly sourceRefs: readonly string[];
  readonly targetRef: string;
  readonly containerId: string;
  readonly transformation?: string | null;
}

/** Section 1.6 artifacts and section 1.4 data — none of which are flow nodes. */
export interface BpmnTextAnnotationContract {
  readonly id: string;
  readonly text: string;
  readonly containerId: string;
}

export interface BpmnGroupContract {
  readonly id: string;
  readonly name: string | null;
  readonly categoryValueRef?: string | null;
  readonly containerId: string;
}

export interface BpmnDataObjectReferenceContract {
  readonly id: string;
  readonly name: string | null;
  readonly dataObjectRef: string | null;
  readonly state: string | null;
  readonly isCollection: boolean;
  readonly containerId: string;
}

export interface BpmnDataStoreReferenceContract {
  readonly id: string;
  readonly name: string | null;
  readonly dataStoreRef: string | null;
  readonly containerId: string;
}

export interface BpmnParticipantContract {
  readonly id: string;
  readonly name: string | null;
  readonly processRef: string | null;
}

export interface BpmnLaneContract {
  readonly id: string;
  readonly name: string | null;
  readonly laneSetId: string;
  readonly flowNodeRefs: readonly string[];
  readonly childLaneSetId: string | null;
}

export interface BpmnLaneSetContract {
  readonly id: string;
  /** The Process or Sub-Process that owns this lane set. */
  readonly containerId: string;
  readonly laneIds: readonly string[];
}

export interface BpmnProcessContract {
  readonly id: string;
  readonly name: string | null;
  readonly isExecutable: boolean | null;
}

export interface BpmnCollaborationContract {
  readonly id: string;
  readonly name: string | null;
  readonly participants: readonly BpmnParticipantContract[];
}

export interface BpmnDefinitionsContract {
  readonly id: string;
  readonly targetNamespace: string;
  readonly exporter: string | null;
  readonly exporterVersion: string | null;
}

export interface BpmnDocumentContract {
  readonly definitions: BpmnDefinitionsContract;
  readonly processes?: readonly BpmnProcessContract[];
  readonly collaboration: BpmnCollaborationContract | null;
  readonly laneSets: readonly BpmnLaneSetContract[];
  readonly lanes: readonly BpmnLaneContract[];
  readonly flowNodes: readonly BpmnFlowNodeContract[];
  readonly sequenceFlows: readonly BpmnSequenceFlowContract[];
  readonly messageFlows: readonly BpmnMessageFlowContract[];
  readonly associations: readonly BpmnAssociationContract[];
  readonly dataAssociations: readonly BpmnDataAssociationContract[];
  readonly textAnnotations: readonly BpmnTextAnnotationContract[];
  readonly groups: readonly BpmnGroupContract[];
  readonly dataObjectReferences: readonly BpmnDataObjectReferenceContract[];
  readonly dataStoreReferences: readonly BpmnDataStoreReferenceContract[];
  readonly diagrams: readonly BpmnDiagramContract[];
}
