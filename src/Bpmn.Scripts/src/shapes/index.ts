import {
  BpmnActivityElement,
  BpmnConnectorLink,
  BpmnDataElement,
  BpmnEventElement,
  BpmnGatewayElement,
  BpmnGroupElement,
  BpmnSwimlaneElement,
  BpmnTextAnnotationElement,
} from './elements.js';

/**
 * The cell namespace handed to dia.Graph and dia.Paper.
 *
 * Passed explicitly rather than registered on the global joint.shapes object: a global
 * registration leaks across every paper on the page and makes two editor instances with
 * different configurations impossible to reason about.
 *
 * Keys must match the type strings the element classes declare (bpmn.Event resolves to
 * bpmn.Event here).
 */
export const bpmnCellNamespace = {
  bpmn: {
    Event: BpmnEventElement,
    Activity: BpmnActivityElement,
    Gateway: BpmnGatewayElement,
    Swimlane: BpmnSwimlaneElement,
    Data: BpmnDataElement,
    TextAnnotation: BpmnTextAnnotationElement,
    Group: BpmnGroupElement,
    Connector: BpmnConnectorLink,
  },
} as const;

export * from './elements.js';
export * from './registry.js';
