import type {
  BpmnDocumentContract,
  BpmnFlowNodeContract,
  BpmnFlowNodeType,
  BpmnShapeContract,
} from '../../src/model/contracts.js';

/**
 * Hand-built documents for tests.
 *
 * Deliberately built here rather than parsed from a .bpmn file: these exercise the renderer, and
 * a fixture that has to survive the XML reader first would fail for two different reasons.
 */

export function flowNode(
  id: string,
  type: BpmnFlowNodeType,
  overrides: Partial<BpmnFlowNodeContract> = {},
): BpmnFlowNodeContract {
  return {
    id,
    name: null,
    type,
    eventDefinition: 'none',
    isInterrupting: true,
    attachedToRef: null,
    containerId: 'Process_1',
    poolId: null,
    laneId: null,
    isForCompensation: false,
    loopCharacteristics: 'none',
    instantiate: false,
    calledElementRef: null,
    flowElementsOwned: false,
    ...overrides,
  };
}

export function shape(
  bpmnElement: string,
  x: number,
  y: number,
  width: number,
  height: number,
  overrides: Partial<BpmnShapeContract> = {},
): BpmnShapeContract {
  return {
    id: `${bpmnElement}_di`,
    bpmnElement,
    bounds: { x, y, width, height },
    isHorizontal: null,
    isExpanded: null,
    isMarkerVisible: null,
    label: null,
    ...overrides,
  };
}

export function emptyDocument(overrides: Partial<BpmnDocumentContract> = {}): BpmnDocumentContract {
  return {
    definitions: {
      id: 'Definitions_1',
      targetNamespace: 'http://example.test/bpmn',
      exporter: null,
      exporterVersion: null,
    },
    processes: [{ id: 'Process_1', name: 'Test process', isExecutable: true }],
    collaboration: null,
    laneSets: [],
    lanes: [],
    flowNodes: [],
    sequenceFlows: [],
    messageFlows: [],
    associations: [],
    dataAssociations: [],
    textAnnotations: [],
    groups: [],
    dataObjectReferences: [],
    dataStoreReferences: [],
    diagrams: [
      {
        id: 'Diagram_1',
        name: null,
        plane: { id: 'Plane_1', bpmnElement: 'Process_1', shapes: [], edges: [] },
      },
    ],
    ...overrides,
  };
}

/** Start Event -> Task -> End Event, with DI for all three and both flows. */
export function linearProcess(): BpmnDocumentContract {
  return emptyDocument({
    flowNodes: [
      flowNode('StartEvent_1', 'startEvent', { name: 'Order received' }),
      flowNode('Task_1', 'task', { name: 'Check stock' }),
      flowNode('EndEvent_1', 'endEvent', { name: 'Order shipped' }),
    ],
    sequenceFlows: [
      {
        id: 'Flow_1',
        name: null,
        sourceRef: 'StartEvent_1',
        targetRef: 'Task_1',
        containerId: 'Process_1',
        conditionExpression: null,
        isDefault: false,
      },
      {
        id: 'Flow_2',
        name: null,
        sourceRef: 'Task_1',
        targetRef: 'EndEvent_1',
        containerId: 'Process_1',
        conditionExpression: null,
        isDefault: false,
      },
    ],
    diagrams: [
      {
        id: 'Diagram_1',
        name: null,
        plane: {
          id: 'Plane_1',
          bpmnElement: 'Process_1',
          shapes: [
            shape('StartEvent_1', 100, 100, 36, 36),
            shape('Task_1', 200, 80, 100, 80),
            shape('EndEvent_1', 360, 100, 36, 36),
          ],
          edges: [
            {
              id: 'Flow_1_di',
              bpmnElement: 'Flow_1',
              waypoints: [
                { x: 136, y: 118 },
                { x: 200, y: 118 },
              ],
              label: null,
            },
            {
              id: 'Flow_2_di',
              bpmnElement: 'Flow_2',
              waypoints: [
                { x: 300, y: 118 },
                { x: 360, y: 118 },
              ],
              label: null,
            },
          ],
        },
      },
    ],
  });
}

/** One Pool with two Lanes, a Task in each, to exercise the containment tree. */
export function pooledProcess(): BpmnDocumentContract {
  return emptyDocument({
    collaboration: {
      id: 'Collaboration_1',
      name: null,
      participants: [{ id: 'Participant_1', name: 'Warehouse', processRef: 'Process_1' }],
    },
    laneSets: [{ id: 'LaneSet_1', containerId: 'Process_1', laneIds: ['Lane_1', 'Lane_2'] }],
    lanes: [
      { id: 'Lane_1', name: 'Picking', laneSetId: 'LaneSet_1', flowNodeRefs: ['Task_1'], childLaneSetId: null },
      { id: 'Lane_2', name: 'Packing', laneSetId: 'LaneSet_1', flowNodeRefs: ['Task_2'], childLaneSetId: null },
    ],
    flowNodes: [
      flowNode('Task_1', 'task', { name: 'Pick items', poolId: 'Participant_1', laneId: 'Lane_1' }),
      flowNode('Task_2', 'task', { name: 'Pack items', poolId: 'Participant_1', laneId: 'Lane_2' }),
    ],
    diagrams: [
      {
        id: 'Diagram_1',
        name: null,
        plane: {
          id: 'Plane_1',
          bpmnElement: 'Collaboration_1',
          shapes: [
            shape('Participant_1', 0, 0, 600, 250, { isHorizontal: true }),
            shape('Lane_1', 30, 0, 570, 125, { isHorizontal: true }),
            shape('Lane_2', 30, 125, 570, 125, { isHorizontal: true }),
            shape('Task_1', 120, 25, 100, 80),
            shape('Task_2', 120, 150, 100, 80),
          ],
          edges: [],
        },
      },
    ],
  });
}
