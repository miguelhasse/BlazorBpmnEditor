import { g, util, type dia } from '@joint/core';

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
import { CONNECTOR_CORNER_RADIUS } from '../geometry/constants.js';
import { roundedPath } from '../routing/rounded-path.js';
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
 * Removes a deletion closure from the resident document and its graph without rebuilding every
 * cell. Lane, Pool, and Process deletion still require the full renderer because they change the
 * geometry or ownership of surviving cells.
 */
export function removeDocumentElements(
  graph: dia.Graph,
  document: BpmnDocumentContract,
  elementIds: readonly string[],
): BpmnDocumentContract | null {
  const removed = new Set(elementIds);
  if (removed.size === 0
    || document.lanes.some((lane) => removed.has(lane.id))
    || document.processes?.some((process) => removed.has(process.id)) === true
    || document.collaboration?.participants.some((participant) => removed.has(participant.id)) === true) {
    return null;
  }

  const cells = graph.getCells().filter((cell) => removed.has(String(cell.id)));
  for (const cell of cells) {
    if (cell.isElement() && cell.getEmbeddedCells().some((child) => !removed.has(String(child.id)))) {
      return null;
    }
  }
  for (const link of graph.getLinks()) {
    const sourceId = (link.get('source') as { readonly id?: string } | undefined)?.id;
    const targetId = (link.get('target') as { readonly id?: string } | undefined)?.id;
    if ((sourceId !== undefined && removed.has(sourceId) || targetId !== undefined && removed.has(targetId))
      && !removed.has(String(link.id))) {
      return null;
    }
  }

  graph.startBatch('render');
  try {
    graph.removeCells(cells);
  } finally {
    graph.stopBatch('render');
  }

  const retain = <T extends { readonly id: string }>(items: readonly T[]): readonly T[] =>
    items.filter((item) => !removed.has(item.id));

  return {
    ...document,
    flowNodes: retain(document.flowNodes),
    sequenceFlows: retain(document.sequenceFlows),
    messageFlows: retain(document.messageFlows),
    associations: retain(document.associations),
    dataAssociations: retain(document.dataAssociations),
    textAnnotations: retain(document.textAnnotations),
    groups: retain(document.groups),
    dataObjectReferences: retain(document.dataObjectReferences),
    dataStoreReferences: retain(document.dataStoreReferences),
    lanes: document.lanes.map((lane) => ({
      ...lane,
      flowNodeRefs: lane.flowNodeRefs.filter((reference) => !removed.has(reference)),
    })),
    diagrams: document.diagrams.map((diagram) => ({
      ...diagram,
      plane: {
        ...diagram.plane,
        shapes: diagram.plane.shapes.filter((shape) => !removed.has(shape.bpmnElement)),
        edges: diagram.plane.edges.filter((edge) => !removed.has(edge.bpmnElement)),
      },
    })),
  };
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

  const drawn = new Set(cells.map((cell) => String(cell.id)));
  for (const association of document.associations) {
    if (!drawn.has(association.sourceRef) || !drawn.has(association.targetRef)) continue;
    const edge = plane.edgeFor(association.id);
    const endpoints = endpointsOf(outlines, edge, association.sourceRef, association.targetRef);
    const lineRatio = (id: string, point: PointContract | undefined): number | undefined => {
      const flow = cells.find((cell) => String(cell.id) === id);
      const waypoints = plane.edgeFor(id)?.waypoints;
      if (flow?.isLink() !== true || waypoints === undefined || waypoints.length < 2 || point === undefined) return undefined;
      return new g.Path(roundedPath(waypoints, CONNECTOR_CORNER_RADIUS)).closestPointNormalizedLength(point);
    };
    const sourceLinkRatio = lineRatio(association.sourceRef, edge?.waypoints[0]);
    const targetLinkRatio = lineRatio(association.targetRef, edge?.waypoints.at(-1));
    cells.push(factory.createAssociationLink(
      association,
      edge?.waypoints ?? [],
      {
        ...endpoints,
        ...(sourceLinkRatio === undefined ? {} : { sourceLinkRatio }),
        ...(targetLinkRatio === undefined ? {} : { targetLinkRatio }),
      },
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
  // Paint order is the order of the list: shapes first, then connectors over them. It is written down, with gaps, so that a cell added
  // later can take a place between two neighbours without renumbering the rest.
  cells.forEach((cell, position) => cell.set('z', (position + 1) * Z_STEP));

  graph.startBatch('render');
  try {
    reconcileCells(graph, cells);
    reconcileEmbedding(graph, buildEmbeddingTree(index, plane, renderedIds));
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
 * Attributes JointJS or the embedding pass owns, which a freshly built cell does not carry and a drawn one always does.
 */
const RUNTIME_ATTRIBUTES: ReadonlySet<string> = new Set(['parent', 'embeds', 'z']);

/** The gap left between the paint order of neighbouring cells. */
const Z_STEP = 1000;

/**
 * Makes the graph hold the cells a render produced, touching only what differs from what is drawn.
 *
 * A load used to `resetCells`, which destroys every cell view and builds them again; an edit changes a handful of cells and paid
 * for all of them (about a second at the default zoom on the stress diagram, five when zoomed out, where every cell is mounted).
 * The cells are still built from the document exactly as before, so what ends up in the graph is what a reset would have produced:
 * a cell that is the same keeps its view, one that differs is updated in place, a new one is added, one the document no longer
 * has is removed. A cell of another kind is replaced, because its view class differs. Attributes other code wrote onto a drawn cell and
 * the document does not describe are cleared, as a reset cleared them.
 */
function reconcileCells(graph: dia.Graph, cells: readonly dia.Cell[]): void {
  const current = graph.getCells();

  // Nothing drawn yet: a reset is cheaper than looking.
  if (current.length === 0) {
    graph.resetCells(cells as dia.Cell[]);
    return;
  }

  const drawn = new Map(current.map((cell) => [String(cell.id), cell]));
  const wanted = new Set(cells.map((cell) => String(cell.id)));

  const additions: dia.Cell[] = [];
  const updates: { readonly drawn: dia.Cell; readonly next: dia.Cell }[] = [];
  const removals: dia.Cell[] = [];

  for (const cell of current) {
    if (!wanted.has(String(cell.id))) removals.push(cell);
  }

  for (const next of cells) {
    const existing = drawn.get(String(next.id));
    if (existing === undefined) {
      additions.push(next);
    } else if (Object.getPrototypeOf(existing) !== Object.getPrototypeOf(next)) {
      removals.push(existing);
      additions.push(next);
    } else {
      updates.push({ drawn: existing, next });
    }
  }

  // A different diagram, or an unrelated document, has nothing in common with what is drawn: one reset is cheaper than removing and adding cell by cell.
  if (updates.length === 0) {
    graph.resetCells(cells as dia.Cell[]);
    return;
  }

  // Removing an element removes the connectors attached to it, so one that is kept has to be put back.
  const removedIds = new Set(removals.map((cell) => String(cell.id)));
  const attached = (link: dia.Cell): boolean => ['source', 'target'].some((end) => {
    const id = (link.get(end) as { readonly id?: string } | undefined)?.id;
    return id !== undefined && removedIds.has(id);
  });
  const surviving = updates.filter(({ next }) => !(next.isLink() && attached(next)));
  for (const { next } of updates) {
    if (next.isLink() && attached(next)) additions.push(next);
  }

  if (removals.length > 0) {
    // Joint removes whatever is embedded in a cell it removes, and has no option to spare it. A child the document keeps (the tasks of a deleted
    // lane or sub-process) is taken out of its parent first, so that the removal reaches nothing that survives; it is embedded again below.
    for (const removed of removals) {
      for (const child of removed.getEmbeddedCells()) {
        if (!removedIds.has(String(child.id))) removed.unembed(child);
      }
    }

    graph.removeCells(removals);
  }

  for (const { drawn: existing, next } of surviving) updateCell(existing, next);

  if (additions.length > 0) {
    placeAdditions(cells, new Set(additions.map((cell) => String(cell.id))), drawn);
    graph.addCells(additions);
  }

  ensurePaintOrder(graph, cells);
}

/**
 * Renumbers the paint order when it no longer follows the list.
 *
 * Kept cells keep the order they have, and new ones are placed between their neighbours, which is right while the drawn order is the
 * list's. It stops being right if the document reorders what it draws, if a cell was brought to the front, or if repeated insertions
 * between the same two neighbours have halved the gap until two cells share a value. Then every cell is numbered again, which is what a
 * full render would have done.
 */
function ensurePaintOrder(graph: dia.Graph, cells: readonly dia.Cell[]): void {
  // A cell the render wanted but could not place is not in the graph; paint order is about the ones that are.
  const live = cells.map((cell) => graph.getCell(String(cell.id)) as dia.Cell | undefined).filter((cell): cell is dia.Cell => cell !== undefined);

  let previous = Number.NEGATIVE_INFINITY;
  let ordered = true;
  for (const cell of live) {
    const order = cell.get('z');
    if (typeof order !== 'number' || order <= previous) {
      ordered = false;
      break;
    }
    previous = order;
  }

  if (ordered) return;

  live.forEach((cell, position) => {
    const order = (position + 1) * Z_STEP;
    if (cell.get('z') !== order) cell.set('z', order);
  });
}

/**
 * Gives each new cell the paint order it would have had in a full render: between the cells that come before and after it in the list.
 * Cells that are already drawn keep the order they have; only the new ones are placed.
 */
function placeAdditions(cells: readonly dia.Cell[], added: ReadonlySet<string>, drawn: ReadonlyMap<string, dia.Cell>): void {
  const known = cells.map((cell) => (added.has(String(cell.id)) ? undefined : (drawn.get(String(cell.id))?.get('z'))));

  // The next cell with a known order after each position.
  const after: (number | undefined)[] = new Array<number | undefined>(cells.length);
  let ahead: number | undefined;
  for (let position = cells.length - 1; position >= 0; position--) {
    after[position] = ahead;
    ahead = known[position] ?? ahead;
  }

  let before: number | undefined;
  cells.forEach((cell, position) => {
    const order = known[position];
    if (order !== undefined) {
      before = order;
      return;
    }
    if (!added.has(String(cell.id))) return;

    const next = after[position];
    const placed = before === undefined
      ? (next === undefined ? (position + 1) * Z_STEP : next - Z_STEP)
      : next === undefined ? before + Z_STEP : (before + next) / 2;

    cell.set('z', placed);
    before = placed;
  });
}

/** Brings one drawn cell to the attributes of the cell the document now describes, writing only what changed. */
function updateCell(existing: dia.Cell, next: dia.Cell): void {
  const changes: Record<string, unknown> = {};
  const nextAttributes = next.attributes as Record<string, unknown>;
  const existingAttributes = existing.attributes as Record<string, unknown>;

  for (const key of Object.keys(nextAttributes)) {
    if (RUNTIME_ATTRIBUTES.has(key)) continue;
    if (!util.isEqual(existingAttributes[key], nextAttributes[key])) changes[key] = nextAttributes[key];
  }

  // Marked as a render's own: what the document says is the truth, so nothing that reacts to a user moving a cell (snapping to peers, pinning a
  // boundary event to its host) should answer it.
  if (Object.keys(changes).length > 0) existing.set(changes, { reconcile: true });

  for (const key of Object.keys(existingAttributes)) {
    if (!RUNTIME_ATTRIBUTES.has(key) && !(key in nextAttributes)) existing.unset(key);
  }
}

/**
 * Makes the graph's embedding match the derived containment tree.
 *
 * Parents are embedded before their own children so a deep chain (Pool -> Lane -> Sub-Process -> Task) is built top-down and no
 * intermediate state has a cell embedded in something not yet embedded itself. A child already in the right parent is left alone,
 * one in the wrong parent is moved, and one the tree no longer contains is taken out.
 *
 * Connectors stay at the graph root for now. Scoping them to their lowest common container (CT-006) only changes behaviour once
 * containers can be dragged, which arrives with the M4 interaction work.
 */
function reconcileEmbedding(graph: dia.Graph, tree: ReadonlyMap<string, string>): void {
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

  // Out first: whatever is embedded but no longer belongs, or belongs somewhere else, leaves its parent.
  for (const cell of graph.getCells()) {
    const parentId = cell.get('parent');
    if (parentId === undefined || tree.get(String(cell.id)) === parentId) continue;

    const oldParent = cellOrUndefined(graph, parentId);
    if (oldParent === undefined) cell.unset('parent');
    else oldParent.unembed(cell);
  }

  const ordered = [...tree].sort(([a], [b]) => depthOf(a) - depthOf(b));

  for (const [childId, parentId] of ordered) {
    const child = cellOrUndefined(graph, childId);
    const parent = cellOrUndefined(graph, parentId);
    if (child === undefined || parent === undefined) continue;
    if (child.get('parent') === parentId) continue;

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
  if (outline === undefined || waypoint === undefined) return null;

  const point = normalizedEndpoint(waypoint, outline, other === undefined ? waypoint : centerOf(other.bounds));

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
