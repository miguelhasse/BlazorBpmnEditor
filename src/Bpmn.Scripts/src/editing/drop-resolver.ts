import type { dia } from '@joint/core';

import * as constants from '../geometry/constants.js';
import { findCell, findElement } from '../graph/cells.js';
import type { GridOptionsContract } from '../interop/contracts.js';
import type { DropTarget } from './connect-controller.js';

/**
 * Turns a stencil drop into paper geometry and an owning container.
 *
 * The host knows which tool was dragged; only the canvas knows where the pointer was in paper
 * coordinates, what the current zoom is, and which container the point fell inside. Resolving that
 * here and letting Blazor build the command keeps creation a single semantic transaction owned by
 * the model side.
 */

/** The notation default size for a type, so a dropped element looks right before it is resized. */
export function defaultSizeOf(elementType: string): { width: number; height: number } {
  switch (elementType) {
    case 'startEvent':
    case 'endEvent':
    case 'intermediateCatchEvent':
    case 'intermediateThrowEvent':
    case 'boundaryEvent':
      return { width: constants.EVENT_SIZE, height: constants.EVENT_SIZE };

    case 'exclusiveGateway':
    case 'inclusiveGateway':
    case 'parallelGateway':
    case 'complexGateway':
    case 'eventBasedGateway':
      return { width: constants.GATEWAY_SIZE, height: constants.GATEWAY_SIZE };

    case 'subProcess':
    case 'eventSubProcess':
    case 'transaction':
    case 'adHocSubProcess':
      return { width: constants.SUBPROCESS_WIDTH, height: constants.SUBPROCESS_HEIGHT };

    /*
     * A Pool is dropped at its notation size; a Lane is not dropped at all — its geometry comes
     * from re-tiling the stack it joins (section 1.5.3) — but the drop still has to report a
     * plausible rectangle so the container under the pointer resolves the same way it does for
     * everything else.
     */
    case 'participant':
      return { width: constants.POOL_WIDTH, height: constants.POOL_HEIGHT };

    case 'poolOrLane':
      return { width: constants.POOL_WIDTH, height: constants.POOL_HEIGHT };

    case 'lane':
      return { width: constants.POOL_WIDTH, height: constants.POOL_HEIGHT / 2 };

    /*
     * Artifacts and data references are not Flow Nodes and none of them is Task-shaped: a Data
     * Object is the tall narrow page, a Group is the large dashed frame things are dropped inside.
     * Falling through to the default gave all four a 100x80 rectangle, which is not cosmetic — the
     * size decides what the drop lands in as well as how the shape is drawn.
     */
    case 'dataObjectReference':
      return { width: constants.DATA_OBJECT_WIDTH, height: constants.DATA_OBJECT_HEIGHT };

    case 'dataStoreReference':
      return { width: constants.DATA_STORE_WIDTH, height: constants.DATA_STORE_HEIGHT };

    case 'textAnnotation':
      return { width: constants.TEXT_ANNOTATION_WIDTH, height: constants.TEXT_ANNOTATION_HEIGHT };

    case 'group':
      return { width: constants.GROUP_WIDTH, height: constants.GROUP_HEIGHT };

    default:
      return { width: constants.TASK_WIDTH, height: constants.TASK_HEIGHT };
  }
}

/**
 * Resolves a drop.
 *
 * The pointer marks the element's **centre**, not its corner: a drop feels like placing the thing
 * you are holding, and offering its top-left would put a 350-wide Sub-Process almost entirely
 * below and right of where the user aimed.
 *
 * Grid snapping is applied to the resulting position when enabled, before anything else looks at
 * it, which is the same ordering section 4.6 requires for connector endpoints.
 */
export function resolveDrop(
  paper: dia.Paper,
  clientX: number,
  clientY: number,
  elementType: string,
  grid: GridOptionsContract,
): DropTarget {
  return withProcessBounds(paper, targetAt(paper.model, paper.clientToLocalPoint({ x: clientX, y: clientY }), elementType, grid), elementType);
}

/**
 * Where a keyboard-armed tool should go.
 *
 * The visible centre, which is the only position the canvas can infer without a pointer.
 */
export function resolveCreationPoint(
  paper: dia.Paper,
  elementType: string,
  grid: GridOptionsContract,
): DropTarget {
  const area = paper.getArea();

  if (elementType === 'poolOrLane') {
    const centre = { x: area.x + (area.width / 2), y: area.y + (area.height / 2) };

    if (poolAt(paper.model, centre)) {
      return withProcessBounds(paper, targetAt(paper.model, centre, 'lane', grid), elementType);
    }

    const size = defaultSizeOf('participant');
    return withProcessBounds(paper, creationPointIn(paper.model, belowTheLowestPool(paper.model, size), 'participant', grid), elementType);
  }

  return withProcessBounds(paper, creationPointIn(
    paper.model,
    elementType === 'participant'
      ? belowTheLowestPool(paper.model, defaultSizeOf(elementType))
      : { x: area.x + (area.width / 2), y: area.y + (area.height / 2) },
    elementType,
    grid,
  ), elementType);
}

/** Includes rendered external labels, even when no BPMNLabel bounds were persisted. */
function withProcessBounds(paper: dia.Paper, target: DropTarget, elementType: string): DropTarget {
  if (elementType !== 'participant' && elementType !== 'poolOrLane' && elementType !== 'lane') return target;
  const bounds = new Map<string, ReturnType<dia.CellView['getNodeBBox']>>();
  paper.updateViews();
  for (const cell of paper.model.getCells()) {
    const metadata = cell.get('bpmn') as { containerId?: string; elementType?: string } | undefined;
    if (metadata?.elementType === 'pool' || metadata?.elementType === 'lane') continue;
    let container = metadata?.containerId;
    const visited = new Set<string>();
    while (container !== undefined && !visited.has(container)) {
      visited.add(container);
      const parent = findCell(paper.model, container);
      if (parent === undefined) break;
      container = (parent.get('bpmn') as { containerId?: string } | undefined)?.containerId;
    }
    const view = paper.findViewByModel(cell) as dia.CellView | undefined;
    if (container === undefined || view === undefined) continue;
    const box = view.getNodeBBox(view.el);
    bounds.set(container, bounds.get(container)?.union(box) ?? box);
  }
  return { ...target, processBounds: Object.fromEntries(bounds) };
}

/**
 * Where a new Pool goes when there is no pointer to say.
 *
 * Under the ones already there, left-aligned with them. The visible centre is the right answer for
 * a Task and the wrong one for a Pool: Pools stack, and the centre of the viewport is almost always
 * inside one of them — which the document then refuses, correctly, as an overlap. The nudge in
 * `creationPointIn` cannot rescue that either, since it steps clear of collisions and a Pool is a
 * container it deliberately ignores.
 */
function belowTheLowestPool(
  graph: dia.Graph,
  size: { width: number; height: number },
): { x: number; y: number } {
  const pools = graph.getElements()
    .filter((element) => (element.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType === 'pool')
    .map((element) => element.getBBox());

  if (pools.length === 0) return { x: size.width / 2, y: size.height / 2 };

  const left = Math.min(...pools.map((box) => box.x));
  const bottom = Math.max(...pools.map((box) => box.y + box.height));

  return { x: left + (size.width / 2), y: bottom + POOL_GAP + (size.height / 2) };
}

/** The room left between stacked Pools. */
const POOL_GAP = 30;

/**
 * Where an element appended to another one should go.
 *
 * To its right, vertically centred on it, one gap away — the direction BPMN diagrams read in, and
 * the same placement the contextual "append" affordance implies visually. Nudged clear of whatever
 * is already there, because the obvious spot is exactly where the last append put something.
 *
 * Returns `null` for a source that is not drawn: an append from an element the canvas cannot see
 * is a race with a reload, not a position to guess at.
 */
export function resolveAppendPoint(
  paper: dia.Paper,
  sourceId: string,
  elementType: string,
  grid: GridOptionsContract,
): DropTarget | null {
  const source = findElement(paper.model, sourceId);
  if (source === undefined) return null;

  const box = source.getBBox();
  const size = defaultSizeOf(elementType);

  return creationPointIn(
    paper.model,
    {
      x: box.x + box.width + APPEND_GAP + (size.width / 2),
      y: box.y + (box.height / 2),
    },
    elementType,
    grid,
  );
}

/** The room left between an element and the one appended to it. */
const APPEND_GAP = 50;

/*
 * The two functions below take a graph and a point in *local* coordinates. The paper is needed for
 * exactly one thing — converting client coordinates and reporting the visible area — so the
 * geometry that matters is separated from it, which also makes it testable without a DOM.
 */

/**
 * Resolves a target at a local point.
 */
export function targetAt(
  graph: dia.Graph,
  point: { x: number; y: number },
  elementType: string,
  grid: GridOptionsContract,
): DropTarget {
  const resolvedType = elementType === 'poolOrLane'
    ? poolAt(graph, point) ? 'lane' : 'participant'
    : elementType;
  const size = defaultSizeOf(resolvedType);

  const centreX = grid.snapEnabled ? snap(point.x, grid.size) : point.x;
  const centreY = grid.snapEnabled ? snap(point.y, grid.size) : point.y;

  const bounds = {
    x: centreX - (size.width / 2),
    y: centreY - (size.height / 2),
    width: size.width,
    height: size.height,
  };

  const owner = containerUnder(graph, point, size);

  return { bounds, containerId: owner.containerId, poolId: owner.poolId };
}

/** Whether the pointer is inside any Pool, including its embedded shapes. */
function poolAt(graph: dia.Graph, point: { x: number; y: number }): boolean {
  return graph.getElements().some((element) => {
    const bpmn = element.get('bpmn') as { readonly elementType?: string } | undefined;
    return bpmn?.elementType === 'pool' && element.getBBox().containsPoint(point);
  });
}

/**
 * A target at a preferred point, nudged clear of whatever is already there.
 *
 * Without the nudge, arming the same tool twice would put the second element exactly on top of the
 * first and DI-011 would refuse it — technically correct and useless, since a keyboard user has no
 * way to aim somewhere else.
 *
 * Overlap with a *container* is not a collision: dropping into a Pool, a Lane or a Sub-Process is
 * how those get filled. That is the same exemption the document editor applies to DI-011.
 */
export function creationPointIn(
  graph: dia.Graph,
  preferred: { x: number; y: number },
  elementType: string,
  grid: GridOptionsContract,
): DropTarget {
  const size = defaultSizeOf(elementType);
  const step = grid.snapEnabled && grid.size > 0 ? grid.size : NUDGE_STEP;

  const point = { x: preferred.x, y: preferred.y };

  for (let attempt = 0; attempt < MAX_NUDGES && collides(graph, point, size); attempt += 1) {
    point.x += step;
    point.y += step;
  }

  return targetAt(graph, point, elementType, grid);
}

/** How far to step when the preferred point is already occupied, absent a grid to follow. */
const NUDGE_STEP = 20;

/** Bounded so a densely packed diagram cannot turn one keypress into an unbounded search. */
const MAX_NUDGES = 40;

/** Whether a candidate rectangle centred on a point would land on top of a non-container shape. */
function collides(
  graph: dia.Graph,
  centre: { x: number; y: number },
  size: { width: number; height: number },
): boolean {
  const left = centre.x - (size.width / 2);
  const top = centre.y - (size.height / 2);

  return graph.getElements().some((element) => {
    if (isContainer(element)) return false;

    const box = element.getBBox();
    return box.x < left + size.width
      && left < box.x + box.width
      && box.y < top + size.height
      && top < box.y + box.height;
  });
}

const snap = (value: number, step: number): number => (step <= 0 ? value : Math.round(value / step) * step);

/**
 * Which container the drop landed in.
 *
 * The deepest container wins, because a Sub-Process inside a Lane inside a Pool is all three at
 * once and only the innermost one owns the new element's `containerId`. The Pool is reported
 * separately, since Message Flow legality follows the Pool while Sequence Flow legality follows
 * the container — the specification is explicit that the two must never be conflated.
 */
function containerUnder(
  graph: dia.Graph,
  point: { x: number; y: number },
  size: { width: number; height: number },
): { containerId: string | null; poolId: string | null } {
  const { containerId, poolId } = ownerUnder(graph, point, size, null);
  return { containerId, poolId };
}

/**
 * What is under a point, including the Lane.
 *
 * The same search as a drop, with two additions a reparent needs: the Lane, because Lane
 * membership is recorded separately from ownership (section 2.8.1), and the ability to ignore one
 * element — the thing being dragged, which is otherwise underneath its own centre and would report
 * itself as its own container.
 */
export function ownerUnder(
  graph: dia.Graph,
  point: { x: number; y: number },
  size: { width: number; height: number },
  ignoreId: string | null,
): { containerId: string | null; poolId: string | null; laneId: string | null } {
  let containerId: string | null = null;
  let poolId: string | null = null;
  let laneId: string | null = null;
  let depth = -1;
  let laneDepth = -1;

  const ignored = ignoreId === null ? null : findElement(graph, ignoreId);
  const ignoredIds = new Set(
    ignored === undefined || ignored === null
      ? []
      : [String(ignored.id), ...ignored.getEmbeddedCells({ deep: true }).map((cell) => String(cell.id))],
  );

  for (const element of graph.getElements()) {
    if (ignoredIds.has(String(element.id))) continue;
    const bpmn = element.get('bpmn') as
      { readonly elementType?: string; readonly containerId?: string | null } | undefined;

    const type = bpmn?.elementType;
    if (type === undefined) continue;

    const box = element.getBBox();
    if (!box.containsPoint(point)) continue;

    if (type === 'pool') {
      poolId = String(element.id);
      continue;
    }

    if (type === 'lane') {
      /*
       * A Lane is organisational metadata, not a flow-elements container: it never becomes the
       * new element's containerId. Its Process does. It is still reported, because Lane
       * membership is a separate fact the reparent command records in flowNodeRefs.
       */
      const ancestors = element.getAncestors().length;

      if (ancestors > laneDepth) {
        laneId = String(element.id);
        laneDepth = ancestors;
      }

      continue;
    }

    if (!isContainer(element)) continue;

    // Deepest wins, and a container smaller than the thing being dropped cannot hold it.
    const ancestors = element.getAncestors().length;
    if (ancestors > depth && box.width >= size.width && box.height >= size.height) {
      containerId = String(element.id);
      depth = ancestors;
    }
  }

  return { containerId, poolId, laneId };
}

/**
 * Whether a cell holds other elements.
 *
 * Both the container search and the collision check need the same answer, and they must agree: a
 * shape that counts as a container for containment but not for overlap would let an element be
 * placed inside a Pool and then be refused for overlapping it.
 */
function isContainer(element: dia.Element): boolean {
  const facts = element.get('bpmn') as
    { readonly elementType?: string; readonly isExpanded?: boolean | null } | undefined;

  const type = facts?.elementType;

  /*
   * A collapsed Sub-Process is opaque, so it is not somewhere anything can be dropped: its
   * contents are not on the canvas, and reporting it as the owner moves an element somewhere the
   * user cannot see it.
   *
   * It used to count as a container whatever its state, and the only thing standing in the way was
   * the size guard further up — a collapsed Sub-Process is 100x80, which is exactly big enough to
   * hold a Task, so dropping a Task on one already reparented it into the invisible container.
   */
  if (type === 'subProcess' || type === 'eventSubProcess'
    || type === 'transaction' || type === 'adHocSubProcess') {
    return facts?.isExpanded === true;
  }

  // A Boundary Event is embedded in its host so that it travels with it, which does not make the host a place to drop things: the document would
  // refuse the element for a container that is not one.
  const holdsElements = element.getEmbeddedCells().some((child) => (child.get('bpmn') as { readonly elementType?: string } | undefined)?.elementType !== 'boundaryEvent');
  return holdsElements || type === 'pool' || type === 'lane';
}
