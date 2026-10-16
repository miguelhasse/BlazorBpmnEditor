import type { BpmnDocumentIndex, BpmnPlaneIndex } from '../model/document.js';
import { isExpandedContainer } from '../model/element-kind.js';

/**
 * Reconstructs the visual containment tree (specification section 2.8.1).
 *
 * The tree is derived from BPMN semantics and DI, never stored: plane root -> Pool ->
 * nested Lane(s) -> expanded embedded Sub-Process -> content, plus Boundary Event ->
 * attached Activity. JointJS embedding then carries it, which is what makes dragging a
 * Pool move its contents without a bespoke movement engine.
 *
 * Two rules are load-bearing and easy to get wrong:
 *  - A Group is never a parent (CT-003). Elements that merely sit inside its rectangle
 *    keep their own parent, so moving or deleting the Group leaves them untouched.
 *  - A node inside an expanded Sub-Process is parented to that Sub-Process even when the
 *    Sub-Process itself belongs to a Lane, preserving the whole ancestor chain.
 */
export type EmbeddingTree = ReadonlyMap<string, string>;

export function buildEmbeddingTree(
  index: BpmnDocumentIndex,
  plane: BpmnPlaneIndex,
  renderedIds: ReadonlySet<string>,
): EmbeddingTree {
  const parents = new Map<string, string>();
  const isRendered = (id: string | null | undefined): id is string =>
    id !== null && id !== undefined && renderedIds.has(id);

  for (const shape of plane.shapes) {
    const element = index.find(shape.bpmnElement);
    if (element === undefined) continue;

    switch (element.kind) {
      case 'participant':
        // Pools are plane-root children.
        break;

      case 'lane': {
        // A nested Lane sits inside the Lane that owns its lane set; a top-level Lane
        // sits inside its Pool.
        const owner = laneOwner(index, element.value.laneSetId);
        if (isRendered(owner)) parents.set(element.value.id, owner);
        break;
      }

      case 'flowNode': {
        const node = element.value;

        // A Boundary Event's parent is always its host, regardless of lane membership
        // (CT-005); its position is meaningful only relative to that host's outline.
        if (isRendered(node.attachedToRef)) {
          parents.set(node.id, node.attachedToRef);
          break;
        }

        const parent = nearestVisualParent(index, plane, renderedIds, node.containerId, node.id);
        if (parent !== null) parents.set(node.id, parent);
        break;
      }

      case 'textAnnotation':
      case 'dataObjectReference':
      case 'dataStoreReference': {
        const parent = nearestVisualParent(index, plane, renderedIds, element.value.containerId, element.value.id);
        if (parent !== null) parents.set(element.value.id, parent);
        break;
      }

      case 'group':
        // Groups are visual categorisation only: they have no children and are not
        // themselves embedded, so a containing Pool never drags them around either.
        break;
    }
  }

  return parents;
}

/**
 * The container a flow element should be embedded in on this plane.
 *
 * Despite the name it does not walk: it asks about the element's own immediate container, then
 * its Lane, then its Pool. Nesting is handled by repetition across elements rather than recursion
 * within one call — every drawn element is asked about separately, and `applyEmbedding` sorts by
 * depth so the chain is built outermost first. Climbing further would only change the answer for
 * an element whose container is not drawn expanded here, and that is a malformed document the
 * renderer now declines to draw at all rather than re-parenting to the Pool.
 */
function nearestVisualParent(
  index: BpmnDocumentIndex,
  plane: BpmnPlaneIndex,
  renderedIds: ReadonlySet<string>,
  containerId: string,
  elementId: string,
): string | null {
  // An expanded Sub-Process is the innermost container and wins over lane membership.
  const container = index.find(containerId);
  if (container?.kind === 'flowNode') {
    const shape = plane.shapeFor(containerId);
    if (isExpandedContainer(container.value, shape?.isExpanded ?? null) && renderedIds.has(containerId)) {
      return containerId;
    }
  }

  // Otherwise the innermost Lane the element belongs to, if that Lane is drawn.
  const laneId = index.laneOf(elementId);
  if (laneId !== undefined && renderedIds.has(laneId)) return laneId;

  // Otherwise the Pool backing the owning Process.
  const participantId = index.participantOfProcess(containerId);
  if (participantId !== undefined && renderedIds.has(participantId)) return participantId;

  return null;
}

function laneOwner(index: BpmnDocumentIndex, laneSetId: string): string | undefined {
  const laneSet = index.document.laneSets.find((candidate) => candidate.id === laneSetId);
  if (laneSet === undefined) return undefined;

  // A child lane set is owned by the Lane that declares it; a root lane set by the
  // Process, which in a collaboration is drawn as its Participant's Pool.
  const parentLane = index.document.lanes.find((lane) => lane.childLaneSetId === laneSetId);
  if (parentLane !== undefined) return parentLane.id;

  return index.participantOfProcess(laneSet.containerId);
}
