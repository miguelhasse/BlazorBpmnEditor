import type { dia } from '@joint/core';

import { centerOf, normalizedEndpoint, outlineOf, type Outline } from '../geometry/outline.js';
import type { FontMetricsProvider } from '../labels/font-metrics.js';
import type {
  BpmnDocumentContract,
  BpmnEdgeContract,
  BpmnShapeContract,
  PointContract,
} from '../model/contracts.js';
import { BpmnDocumentIndex, BpmnPlaneIndex, resolveDiagram } from '../model/document.js';
import { isCollapsedContainer } from '../model/element-kind.js';
import * as factory from '../shapes/registry.js';

import { buildEmbeddingTree } from './hierarchy.js';

export interface RenderResult {
  readonly diagramId: string;
  readonly shapeCount: number;
  readonly edgeCount: number;
  /** Elements referenced by the document that this plane does not draw. */
  readonly unrenderedElementIds: readonly string[];
}

/**
 * Projects a document onto a JointJS graph.
 *
 * DI drives the pass, not the semantic model: every BPMNShape on the plane becomes
 * exactly one cell, and elements without a shape (a collapsed Sub-Process's children,
 * for instance) are simply not drawn while staying fully present in the model. That is
 * what section 10 means by the SVG being a view over two persisted layers, and it makes
 * collapsed containers fall out of the data instead of needing special-case code.
 *
 * The whole projection runs inside one graph batch so JointJS performs a single layout
 * and render rather than one per cell.
 */
export function renderDocument(
  graph: dia.Graph,
  document: BpmnDocumentContract,
  diagramId: string | null,
  metrics: FontMetricsProvider,
): RenderResult {
  const diagram = resolveDiagram(document, diagramId);
  if (diagram === undefined) {
    graph.clear();
    return { diagramId: '', shapeCount: 0, edgeCount: 0, unrenderedElementIds: [] };
  }

  const index = new BpmnDocumentIndex(document);
  const plane = new BpmnPlaneIndex(diagram);
  /*
   * A lookup rather than the plane itself: only a Boundary Event needs another element's geometry,
   * and only to keep its label off the Activity it is attached to.
   */
  const shapesByElement = new Map(plane.shapes.map((shape) => [shape.bpmnElement, shape.bounds]));
  const context: factory.CellFactoryContext = {
    metrics,
    boundsOf: (elementId) => shapesByElement.get(elementId) ?? null,
  };

  const cells: dia.Cell[] = [];
  const renderedIds = new Set<string>();
  const unrendered: string[] = [];

  // --- shapes --------------------------------------------------------------
  const closed = new Map<string, boolean>();

  for (const shape of plane.shapes) {
    /*
     * A collapsed Sub-Process is opaque (section 2.8.1): nothing inside it is drawn, whatever DI
     * the document happens to carry for it.
     *
     * That was already true in practice, because exporters omit child DI for a collapsed
     * container — which made a notational rule depend on other people's tidiness. A hand-edited
     * file, or a collapse that flipped isExpanded before it moved the children, would paint them
     * over the closed box, parented to the Pool rather than to the container they belong to.
     */
    if (isInsideClosedContainer(index, plane, shape.bpmnElement, closed)) {
      unrendered.push(shape.bpmnElement);
      continue;
    }

    const cell = createCellForShape(index, shape, context);
    if (cell === null) {
      unrendered.push(shape.bpmnElement);
      continue;
    }
    cells.push(cell);
    renderedIds.add(shape.bpmnElement);
  }

  // --- links ---------------------------------------------------------------
  // The outline each drawn element has, so a connector's ends can be put on it.
  const outlines = new Map<string, Outline>();

  for (const cell of cells) {
    const type = (cell.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType;
    const bounds = shapesByElement.get(String(cell.id));
    if (type !== undefined && bounds !== undefined) outlines.set(String(cell.id), outlineOf(type, bounds));
  }

  // Only connectors whose endpoints are both drawn on this plane: a dangling link would
  // otherwise be silently re-anchored to the paper origin.
  for (const flow of document.sequenceFlows) {
    if (!renderedIds.has(flow.sourceRef) || !renderedIds.has(flow.targetRef)) continue;
    const edge = plane.edgeFor(flow.id);
    cells.push(factory.createSequenceFlowLink(
      flow,
      edge?.waypoints ?? [],
      endpointsOf(outlines, edge, flow.sourceRef, flow.targetRef),
      flow.name,
      context,
      edge?.label?.bounds ?? null,
    ));
  }

  for (const flow of document.messageFlows) {
    if (!renderedIds.has(flow.sourceRef) || !renderedIds.has(flow.targetRef)) continue;
    const edge = plane.edgeFor(flow.id);
    cells.push(factory.createMessageFlowLink(
      flow,
      edge?.waypoints ?? [],
      endpointsOf(outlines, edge, flow.sourceRef, flow.targetRef),
      flow.name,
      context,
      edge?.label?.bounds ?? null,
    ));
  }

  for (const association of document.associations) {
    if (!renderedIds.has(association.sourceRef) || !renderedIds.has(association.targetRef)) continue;
    const edge = plane.edgeFor(association.id);
    cells.push(factory.createAssociationLink(
      association,
      edge?.waypoints ?? [],
      endpointsOf(outlines, edge, association.sourceRef, association.targetRef),
      association.direction !== 'none',
    ));
  }

  for (const association of document.dataAssociations) {
    const source = association.sourceRefs[0];
    if (source === undefined || !renderedIds.has(source) || !renderedIds.has(association.targetRef)) continue;
    const edge = plane.edgeFor(association.id);
    // Data Associations are always directed (section 2.9).
    cells.push(factory.createAssociationLink(
      association,
      edge?.waypoints ?? [],
      endpointsOf(outlines, edge, source, association.targetRef),
      true,
    ));
  }

  // --- commit --------------------------------------------------------------
  graph.startBatch('render');
  try {
    graph.resetCells(cells);
    applyEmbedding(graph, buildEmbeddingTree(index, plane, renderedIds));
  } finally {
    graph.stopBatch('render');
  }

  return {
    diagramId: diagram.id,
    shapeCount: renderedIds.size,
    edgeCount: cells.length - renderedIds.size,
    unrenderedElementIds: unrendered,
  };
}

function createCellForShape(
  index: BpmnDocumentIndex,
  shape: BpmnShapeContract,
  context: factory.CellFactoryContext,
): dia.Cell | null {
  const element = index.find(shape.bpmnElement);
  if (element === undefined) return null;

  switch (element.kind) {
    case 'flowNode':
      return factory.createFlowNodeCell(element.value, shape, context);

    case 'participant':
      return factory.createSwimlaneCell(
        element.value.id,
        element.value.name,
        shape,
        // isHorizontal defaults to true when a document omits it (section 10.4).
        shape.isHorizontal ?? true,
        'pool',
        context,
      );

    case 'lane':
      return factory.createSwimlaneCell(
        element.value.id,
        element.value.name,
        shape,
        shape.isHorizontal ?? true,
        'lane',
        context,
      );

    case 'textAnnotation':
      return factory.createTextAnnotationCell(element.value, shape, context);

    case 'group':
      return factory.createGroupCell(element.value, shape, context);

    case 'dataObjectReference':
      return factory.createDataObjectCell(element.value, shape, context);

    case 'dataStoreReference':
      return factory.createDataStoreCell(element.value, shape, context);
  }
}

/**
 * Applies the derived containment tree through JointJS embedding.
 *
 * Parents are embedded before their own children so a deep chain (Pool -> Lane ->
 * Sub-Process -> Task) is built top-down and no intermediate state has a cell embedded
 * in something not yet embedded itself.
 *
 * Connectors stay at the graph root for now. Scoping them to their lowest common
 * container (CT-006) only changes behaviour once containers can be dragged, which
 * arrives with the M4 interaction work.
 */
function applyEmbedding(graph: dia.Graph, tree: ReadonlyMap<string, string>): void {
  const depths = new Map<string, number>();
  const visiting = new Set<string>();
  const depthOf = (id: string): number => {
    const known = depths.get(id);
    if (known !== undefined) return known;

    const parent = tree.get(id);
    // A cyclic tree stops at the repeat instead of recursing forever.
    let depth = 0;
    if (parent !== undefined && !visiting.has(id)) {
      visiting.add(id);
      depth = depthOf(parent) + 1;
      visiting.delete(id);
    }
    depths.set(id, depth);
    return depth;
  };

  const ordered = [...tree].sort(([a], [b]) => depthOf(a) - depthOf(b));

  for (const [childId, parentId] of ordered) {
    const child = cellOrUndefined(graph, childId);
    const parent = cellOrUndefined(graph, parentId);
    if (child === undefined || parent === undefined) continue;
    parent.embed(child);
  }
}

/**
 * getCell is typed as always returning a cell, but returns undefined for an id the graph
 * does not hold. The embedding tree is derived from the document rather than from what
 * was committed, so that case is reachable and must be a real union.
 */
function cellOrUndefined(graph: dia.Graph, id: string): dia.Cell | undefined {
  return graph.getCell(id);
}

/**
 * Converts a connector's persisted endpoint waypoints into shape-relative offsets.
 *
 * Without this, JointJS anchors a link to its endpoints' centres and derives a boundary
 * intersection, which visibly reroutes an imported diagram. The persisted first and last waypoints
 * decide where the line meets each shape, but only as far as the outline: an end is always drawn on
 * the boundary of the shape it belongs to (`normalizedEndpoint`), whatever the file said, so a
 * document that was not normalised on import still looks right.
 *
 * An edge with no DI, or one whose endpoint shapes are not on this plane, falls back to
 * JointJS's own anchoring — the only sensible behaviour when the document did not say.
 */
function endpointsOf(
  outlines: ReadonlyMap<string, Outline>,
  edge: BpmnEdgeContract | undefined,
  sourceRef: string,
  targetRef: string,
): factory.EdgeEndpoints {
  if (edge === undefined || edge.waypoints.length < 2) return factory.noEndpoints;

  const source = outlines.get(sourceRef);
  const target = outlines.get(targetRef);

  return {
    source: offsetOnOutline(source, target, edge.waypoints[0]),
    target: offsetOnOutline(target, source, edge.waypoints.at(-1)),
  };
}

function offsetOnOutline(
  outline: Outline | undefined,
  other: Outline | undefined,
  waypoint: PointContract | undefined,
): PointContract | null {
  if (outline === undefined || other === undefined || waypoint === undefined) return null;

  const point = normalizedEndpoint(waypoint, outline, centerOf(other.bounds));

  return { x: point.x - outline.bounds.x, y: point.y - outline.bounds.y };
}

/**
 * Whether this plane draws a closed container somewhere above the element.
 *
 * Walks outward through the containment chain looking for a Sub-Process the plane draws collapsed.
 * Finding one hides everything beneath it; running out of containers means nothing does.
 *
 * It does not stop at the first container it finds drawn, and that is the part worth stating: an
 * expanded Sub-Process can itself be inside a collapsed one, so stopping there would draw the
 * grandchildren of a closed container while hiding its children.
 *
 * Containers the plane does not draw are stepped over rather than answered for — a drilled-in
 * plane draws its own Sub-Process's children without drawing the Sub-Process, and that is not a
 * closed container, it is the inside of one.
 *
 * Memoised per pass, so a deep chain is walked once rather than once per descendant.
 */
function isInsideClosedContainer(
  index: BpmnDocumentIndex,
  plane: BpmnPlaneIndex,
  elementId: string,
  memo: Map<string, boolean>,
): boolean {
  const seen: string[] = [];
  let current = containerOf(index, elementId);
  let answer = false;

  while (current !== undefined) {
    const known = memo.get(current);
    if (known !== undefined) {
      answer = known;
      break;
    }

    seen.push(current);

    const node = index.flowNode(current);
    const shape = plane.shapeFor(current);

    if (node !== undefined && shape !== undefined && isCollapsedContainer(node, shape.isExpanded ?? null)) {
      answer = true;
      break;
    }

    // A cycle would be a malformed document, and an infinite loop is a worse way to report one.
    if (seen.length > 64) break;

    current = containerOf(index, current);
  }

  for (const id of seen) memo.set(id, answer);

  return answer;
}

/** What holds an element, for every kind of thing a plane can draw. */
function containerOf(index: BpmnDocumentIndex, elementId: string): string | undefined {
  const found = index.find(elementId);

  switch (found?.kind) {
    case 'flowNode':
    case 'textAnnotation':
    case 'group':
    case 'dataObjectReference':
    case 'dataStoreReference':
      return found.value.containerId;

    // A Participant is the outermost thing a plane draws, and a Lane classifies without owning
    // (section 2.8) — neither is a container an element is inside for this purpose.
    case 'participant':
    case 'lane':
    case undefined:
      return undefined;
  }
}
