import type { BpmnFlowNodeContract, BpmnFlowNodeType } from './contracts.js';

/** The three notational families of specification section 1 (Flow Objects). */
export type BpmnElementKind = 'event' | 'activity' | 'gateway';

/**
 * Where an Event sits in the flow, which together with its trigger decides ring count,
 * ring weight, dash pattern and glyph fill (specification section 1.1 / section 4.2).
 */
export type BpmnEventContext = 'start' | 'intermediateCatch' | 'intermediateThrow' | 'boundary' | 'end';

/**
 * Classifies a flow node type.
 *
 * Every switch in this module enumerates all element types and has no default clause, so
 * adding a value to BpmnFlowNodeType is a compile error everywhere a notational decision
 * must be made — rather than silently falling through to a plausible-looking wrong shape.
 */
export function kindOf(type: BpmnFlowNodeType): BpmnElementKind {
  switch (type) {
    case 'startEvent':
    case 'endEvent':
    case 'intermediateCatchEvent':
    case 'intermediateThrowEvent':
    case 'boundaryEvent':
      return 'event';

    case 'task':
    case 'userTask':
    case 'serviceTask':
    case 'scriptTask':
    case 'manualTask':
    case 'businessRuleTask':
    case 'sendTask':
    case 'receiveTask':
    case 'subProcess':
    case 'eventSubProcess':
    case 'transaction':
    case 'adHocSubProcess':
    case 'callActivity':
      return 'activity';

    case 'exclusiveGateway':
    case 'inclusiveGateway':
    case 'parallelGateway':
    case 'complexGateway':
    case 'eventBasedGateway':
      return 'gateway';
  }
}

export const isEvent = (type: BpmnFlowNodeType): boolean => kindOf(type) === 'event';
export const isActivity = (type: BpmnFlowNodeType): boolean => kindOf(type) === 'activity';
export const isGateway = (type: BpmnFlowNodeType): boolean => kindOf(type) === 'gateway';

/**
 * True for the Activity kinds that own a nested flowElements container (section 2.7).
 *
 * A Call Activity is deliberately excluded: it references a separate global Process and
 * never owns local content, which section 6 encodes as calledElementRef and
 * flowElementsOwned being mutually exclusive.
 */
export function isSubProcessKind(type: BpmnFlowNodeType): boolean {
  switch (type) {
    case 'subProcess':
    case 'eventSubProcess':
    case 'transaction':
    case 'adHocSubProcess':
      return true;

    case 'startEvent':
    case 'endEvent':
    case 'intermediateCatchEvent':
    case 'intermediateThrowEvent':
    case 'boundaryEvent':
    case 'task':
    case 'userTask':
    case 'serviceTask':
    case 'scriptTask':
    case 'manualTask':
    case 'businessRuleTask':
    case 'sendTask':
    case 'receiveTask':
    case 'callActivity':
    case 'exclusiveGateway':
    case 'inclusiveGateway':
    case 'parallelGateway':
    case 'complexGateway':
    case 'eventBasedGateway':
      return false;
  }
}

/** Resolves an Event's flow context. Returns null for non-events. */
export function eventContextOf(type: BpmnFlowNodeType): BpmnEventContext | null {
  switch (type) {
    case 'startEvent':
      return 'start';
    case 'endEvent':
      return 'end';
    case 'intermediateCatchEvent':
      return 'intermediateCatch';
    case 'intermediateThrowEvent':
      return 'intermediateThrow';
    case 'boundaryEvent':
      return 'boundary';

    case 'task':
    case 'userTask':
    case 'serviceTask':
    case 'scriptTask':
    case 'manualTask':
    case 'businessRuleTask':
    case 'sendTask':
    case 'receiveTask':
    case 'subProcess':
    case 'eventSubProcess':
    case 'transaction':
    case 'adHocSubProcess':
    case 'callActivity':
    case 'exclusiveGateway':
    case 'inclusiveGateway':
    case 'parallelGateway':
    case 'complexGateway':
    case 'eventBasedGateway':
      return null;
  }
}

/**
 * A collapsed Sub-Process renders as an opaque leaf carrying the plus marker; its
 * children stay in the model but are not materialised on the active plane (section 2.7).
 * BPMNShape.isExpanded is authoritative when present; absent, a Sub-Process is treated as
 * collapsed, which matches how exporters omit child DI for collapsed containers.
 *
 * This and {@link isExpandedContainer} are the only two places the question is answered.
 * It was answered inline in three of them — the marker layer, the shape factory and the
 * embedding — and a fourth, C#-side, answered it the other way round.
 */
export function isCollapsedContainer(node: BpmnFlowNodeContract, isExpanded: boolean | null): boolean {
  if (!isSubProcessKind(node.type)) return false;
  return isExpanded !== true;
}

/**
 * The positive form: a Sub-Process whose content is drawn on this plane.
 *
 * Not simply `!isCollapsedContainer(...)`, because that is also true of every Task, Event
 * and Gateway — the callers that ask this want "is this a container I should draw inside",
 * and a Task is not a collapsed container, it is not a container at all.
 */
export function isExpandedContainer(node: BpmnFlowNodeContract, isExpanded: boolean | null): boolean {
  return isSubProcessKind(node.type) && isExpanded === true;
}
