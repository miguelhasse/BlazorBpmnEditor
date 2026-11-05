import type { BpmnLaneContract, BpmnLaneSetContract } from '../../src/model/contracts.js';
import { emptyDocument, flowNode, shape } from './documents.js';

export type LaneLayout = 'none' | 'root' | 'sets' | 'nested';

/** Bounded-depth lanes with one task each; construction is outside measured work. */
export function laneHeavyDocument(count: number, layout: LaneLayout) {
  const lanes: BpmnLaneContract[] = [];
  const laneSets: BpmnLaneSetContract[] = [];
  const nodes = [];
  const shapes = [shape('Pool', 0, 0, 1000, Math.max(120, count * 120))];
  for (let index = 0; index < count; index++) {
    const id = `Lane_${String(index)}`;
    const task = `Task_${String(index)}`;
    const set = layout === 'root' ? 'Root' : `Set_${String(index)}`;
    if (layout !== 'none') {
      lanes.push({
        id, name: null, laneSetId: set, flowNodeRefs: [task],
        childLaneSetId: layout === 'nested' && index % 3 !== 2 && index + 1 < count
          ? `Set_${String(index + 1)}` : null,
      });
      if (layout !== 'root') laneSets.push({ id: set, containerId: 'Process_1', laneIds: [id] });
      shapes.push(shape(id, 30, index * 120, 970, 120, { isHorizontal: true }));
    }
    nodes.push(flowNode(task, 'task'));
    shapes.push(shape(task, 100, index * 120 + 20, 100, 80));
  }
  if (layout === 'root') laneSets.push({ id: 'Root', containerId: 'Process_1', laneIds: lanes.map((lane) => lane.id) });
  return emptyDocument({
    collaboration: { id: 'Collaboration', name: null, participants: [{ id: 'Pool', name: null, processRef: 'Process_1' }] },
    lanes, laneSets, flowNodes: nodes,
    diagrams: [{ id: 'Diagram', name: null, plane: { id: 'Plane', bpmnElement: 'Collaboration', shapes, edges: [] } }],
  });
}
