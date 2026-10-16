import { dia, g } from '@joint/core';

import type { AcceptedConfig } from '../config/accepted-config.js';
import { endpointStrategy } from '../editing/endpoint-anchor.js';
import { isLane } from './lane.js';
import { connectorNamespace } from '../routing/connector.js';
import { routerNamespace } from '../routing/live-router.js';
import { bpmnCellNamespace } from '../shapes/index.js';

/**
 * Thin adapter over the two JointJS objects this library builds on.
 *
 * Deliberately shallow: `dia.Graph` and `dia.Paper` own the model/view lifecycle, SVG
 * attribute binding, batching and event plumbing, and nothing here re-implements any of
 * that (implementation plan §2, "JointJS OSS reuse policy"). Its whole job is to derive
 * paper options from the accepted configuration — never from local defaults — so the
 * paper is correct on its first frame instead of being reconfigured after a flash.
 */
/**
 * The connection behaviour the paper needs, supplied by whoever owns the BPMN rules.
 *
 * The paper has to know how to judge a proposed connection and what provisional link to draw
 * while one is being dragged; it must not know anything about BPMN to do either.
 */
export interface ConnectionPolicy {
  readonly validate: (
    sourceView: dia.CellView,
    sourceMagnet: SVGElement | null,
    targetView: dia.CellView,
    targetMagnet: SVGElement | null,
  ) => boolean;
  /**
   * The provisional link a gesture draws.
   *
   * Takes the source view because the policy needs to know which shape the drag started from —
   * it is the one shape that must not be dimmed as an illegal target.
   */
  readonly defaultLink: (sourceView: dia.CellView, sourceMagnet: SVGElement | null) => dia.Link;
}

export class PaperHost {
  public readonly graph: dia.Graph;
  public readonly paper: dia.Paper;

  /** The element Blazor rendered for the paper, which destroying the paper takes away. */
  readonly #host: HTMLElement;

  public constructor(hostElement: HTMLElement, config: AcceptedConfig, connect?: ConnectionPolicy) {
    this.graph = new dia.Graph({}, { cellNamespace: bpmnCellNamespace });

    this.#host = hostElement;

    this.paper = new dia.Paper({
      el: hostElement,
      model: this.graph,
      cellViewNamespace: bpmnCellNamespace,
      routerNamespace,
      connectorNamespace,
      width: '100%',
      height: '100%',
      // JointJS snaps every element drag to `gridSize`, so the drawn grid keeps its own spacing and
      // dragging only steps by it when snapping is on.
      gridSize: config.grid.snapEnabled ? config.grid.size : 1,
      drawGridSize: config.grid.size,
      drawGrid: config.grid.isVisible ? { name: 'dot', args: [{ color: 'var(--bpmn-grid-color)' }] } : false,
      background: { color: config.background },
      /*
       * Element dragging, bends and arrowheads are JointJS's.
       *
       * Only the refusals are listed. `can()` treats an unnamed feature as allowed, so arrowhead
       * dragging, vertex add/move/remove and the link tools are on without being named — naming
       * them would suggest they are configurable here when the typings do not admit them.
       *
       * `linkMove` stays off: dragging a whole connector sideways moves both ends without
       * touching either shape, which BPMNDI can express but no BPMN reader would draw the same
       * way. Label dragging waits for the label command.
       */
      interactive: config.isReadOnly
        ? false
        : (view: dia.CellView) => ({
          /*
           * A Lane fills its Pool, so it is never moved on its own: only its Pool moves it, and the
           * embedded Lanes go along with that. JointJS asks with the view alone and reads the
           * answer as a per-feature object.
           */
          elementMove: !isLane(view.model),
          addLinkFromMagnet: false,
          linkMove: false,
          labelMove: false,
        }),

      /*
       * Connector creation is started by a tool on the selected element, not by magnets on the
       * shape bodies: a body-wide magnet would consume the pointer-down that begins an element
       * drag. Where a connector *attaches* is still arbitrary along the outline, which is what
       * section 4.6 requires — snapLinks handles the anchor, and no fixed ports are introduced.
       */
      ...(connect === undefined ? {} : {
        validateConnection: connect.validate,
        defaultLink: connect.defaultLink,
        // A loose end would be an edge with one endpoint, which BPMN cannot represent.
        linkPinning: false,
        // Greys out every shape the rules reject while a connector is dragged, so an illegal
        // target never looks droppable (specification section 7).
        markAvailable: true,
        snapLinks: { radius: 20 },
        // The end being dragged follows the shape's outline and snaps to its magnets.
        connectionStrategy: endpointStrategy(config.grid),
      }),
      async: true,
      sorting: dia.Paper.sorting.APPROX,

      /*
       * Virtualization: only what is on screen, plus a screen's worth of margin, is in the DOM.
       *
       * On the six-hundred-shape benchmark the whole diagram is eight and a half thousand SVG
       * nodes, and it is the browser's layout and paint over that tree — not JointJS — that made
       * roughly half the frames of a zoom miss sixty a second. Nothing that is off screen needs to
       * exist for the person looking at it.
       *
       * Safe only because the accessible representation is no longer the SVG. A screen reader
       * walks the tree built from the model, so culling shapes takes nothing away from it; doing
       * this while the drawn cells were still the accessible representation would have hidden most
       * of the diagram from anyone using one.
       *
       * The margin is the visible area again in each direction, so an ordinary pan moves into
       * shapes that are already mounted rather than waiting for them to be built.
       */
      viewport: (view, _isMounted, paper) => isWithinViewport(view as { readonly model?: unknown }, paper),
      preventDefaultBlankAction: false,
      preventDefaultViewAction: false,
    });

    if (connect !== undefined) this.#preferInnermostTarget();
  }

  /**
   * Makes a shape inside a container win the drop over the container itself.
   *
   * JointJS ranks the magnets near the pointer by distance, and a pointer that is inside a magnet
   * counts as distance zero for every one it is inside. A Task in a Pool is such a pointer: the
   * Task's body and the Pool's body tie, and the tie goes to whichever is visited first, which is
   * the Pool. So sliding a connector's end along its own shape — or dropping one on any shape in a
   * Pool — reconnected it to the Pool, and the rules then refused it.
   *
   * The innermost element the pointer is on or within snapping distance of is the one it means;
   * the containers around it are taken out of the running. A container is still reachable by
   * pointing at part of it with nothing inside.
   */
  #preferInnermostTarget(): void {
    const lookup = this.paper.findClosestMagnetToPoint.bind(this.paper);

    this.paper.findClosestMagnetToPoint = (point, options) => {
      const radius = options?.radius ?? 0;
      const here = new g.Point(point.x, point.y);

      let inner: dia.Element | undefined;
      let bestDepth = -1;
      let bestNear = Infinity;

      for (const element of this.graph.getElements()) {
        const box = element.getBBox();
        const near = box.containsPoint(here) ? 0 : box.pointNearestToPoint(here).distance(here);
        if (near > radius) continue;

        const depth = element.getAncestors().length;
        if (depth > bestDepth || (depth === bestDepth && near < bestNear)) {
          inner = element;
          bestDepth = depth;
          bestNear = near;
        }
      }

      if (inner === undefined) return lookup(point, options);

      const containers = new Set(inner.getAncestors().map((ancestor) => String(ancestor.id)));
      const inherited = options?.filter;

      return lookup(point, {
        ...options,
        filter: (view, magnet) => !containers.has(String(view.model.id)) && (inherited?.(view, magnet) ?? true),
      });
    };
  }

  /** Releases the paper's DOM/listeners and empties the graph. Safe to call twice. */
  public destroy(): void {
    this.paper.off();
    this.graph.off();

    /*
     * `paper.remove()` is Backbone's, and it ends in `this.$el.remove()` — it takes the element
     * the paper was mounted on out of the DOM. That element is the one Blazor rendered, and Blazor
     * still holds a reference to it.
     *
     * Nothing noticed while a canvas was only ever destroyed on the way out. A canvas rebuilt in
     * place — which is what a configuration change needs — came back mounted on a detached node
     * and drew nothing at all.
     *
     * So the full teardown still runs, layer views and tools included, and the element is put back
     * exactly where it was. Mounting the paper on a div of the library's own would be tidier, and
     * was tried: it puts a node between `.bpmn-canvas__paper` and the SVG, which the stylesheet
     * and a host's own CSS both reach past with a direct-child selector.
     */
    const parent = this.#host.parentNode;
    const next = this.#host.nextSibling;

    this.paper.remove();
    this.graph.clear();

    parent?.insertBefore(this.#host, next);
  }
}

/**
 * Whether a cell is near enough to the visible area to be worth drawing.
 *
 * Measured against the model's own bounds rather than the rendered view: the view is what this is
 * deciding whether to build, so asking it where it is would be circular.
 */
function isWithinViewport(view: { readonly model?: unknown }, paper: dia.Paper): boolean {
  const cell = view.model;
  if (!(cell instanceof dia.Cell)) return true;

  const area = paper.getArea();
  const margin = area.inflate(area.width, area.height);

  if (cell.isElement()) return margin.intersect(cell.getBBox()) !== null;

  /*
   * A connector is judged by its endpoints, not by its own bounding box.
   *
   * `getBBox()` on a link computes its route, and on a diagram with six hundred of them that is
   * the expensive thing in the pass — the whole point of which is to be cheaper than drawing. The
   * box spanning its two ends contains an orthogonal route between them, give or take the
   * clearance the router takes when it steps around something, so it is inflated by enough to
   * cover that.
   */
  const link = cell as dia.Link;
  const source = endOf(link, 'source');
  const target = endOf(link, 'target');

  if (source === null || target === null) return true;

  return margin.intersect(source.union(target).inflate(ROUTE_CLEARANCE, ROUTE_CLEARANCE)) !== null;
}

/** How far outside its endpoints an orthogonal route may bulge when it steps around something. */
const ROUTE_CLEARANCE = 60;

/** One end of a link as a box, or null when it is not attached to a cell. */
function endOf(link: dia.Link, end: 'source' | 'target'): g.Rect | null {
  const attached = link.get(end) as { readonly id?: string } | undefined;
  if (attached?.id === undefined) return null;

  // A link the paper has just removed — the provisional one a refused or finished gesture leaves —
  // has no graph by the time the viewport pass looks at it, whatever the typings promise.
  const graph = link.graph as dia.Graph | null;
  const cell = graph?.getCell(attached.id);
  return cell === undefined ? null : cell.getBBox();
}
