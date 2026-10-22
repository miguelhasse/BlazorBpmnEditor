import { dia } from '@joint/core';

import * as constants from '../geometry/constants.js';

/**
 * The JointJS element classes this library registers.
 *
 * Each one extends dia.Element with declarative markup and leaves rendering, attribute
 * binding and view lifecycle entirely to JointJS (implementation plan section 2). What
 * is custom here is only the BPMN notation itself: which nodes exist, and which
 * selectors the factories then drive from the semantic model.
 *
 * Markup is intentionally static — a shape never gains or loses nodes at runtime, it
 * only hides them — so JointJS can reuse views instead of rebuilding DOM on every
 * property change.
 */

const labelNode = { tagName: 'text', selector: 'label', className: 'bpmn-label' } as const;

/**
 * Root attributes shared by every shape a connector may end on.
 *
 * The whole body is one magnet. That is what makes the shape a valid connector *target* at an
 * arbitrary point on its outline, which section 4.6 requires — a port set would quantise the
 * outline into exactly the fixed anchors the specification forbids, and `magnet: false` would make
 * the shape no target at all.
 *
 * It does not make the shape a connector *source*. The paper sets `addLinkFromMagnet: false`, so a
 * pointer-down on the body still starts an element drag rather than a link, and the connector
 * gesture starts from the handle on the selected element instead (see `connect-tool.ts`).
 */
const connectableRoot = { magnet: true } as const;

/** Events: one or two rings plus a centred trigger glyph (section 1.1, section 4.2). */
export const BpmnEventElement = dia.Element.define(
  'bpmn.Event',
  {
    size: { width: constants.EVENT_SIZE, height: constants.EVENT_SIZE },
    attrs: {
      root: connectableRoot,
      outerRing: {
        cx: constants.EVENT_SIZE / 2,
        cy: constants.EVENT_SIZE / 2,
        r: constants.EVENT_OUTER_RADIUS,
        fill: 'var(--bpmn-fill)',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
      },
      innerRing: { fill: 'none', stroke: 'var(--bpmn-stroke)', strokeWidth: constants.STROKE_THIN },
      icon: { display: 'none', fill: 'var(--bpmn-stroke)', stroke: 'none' },
      templateIcon: { display: 'none' },
    },
  },
  {
    markup: [
      { tagName: 'circle', selector: 'outerRing', className: 'bpmn-event-ring' },
      { tagName: 'circle', selector: 'innerRing', className: 'bpmn-event-ring' },
      { tagName: 'path', selector: 'icon', className: 'bpmn-event-icon' },
      // An applied element template's icon replaces the trigger glyph (see template-icon.ts).
      { tagName: 'image', selector: 'templateIcon', className: 'bpmn-template-icon' },
      labelNode,
    ],
  },
);

/**
 * Activities: a rounded rectangle, an optional inset border for Transactions, a
 * top-left type glyph and a bottom-centre marker band (section 1.2, section 4.3).
 */
export const BpmnActivityElement = dia.Element.define(
  'bpmn.Activity',
  {
    size: { width: constants.TASK_WIDTH, height: constants.TASK_HEIGHT },
    attrs: {
      root: connectableRoot,
      body: {
        x: 0,
        y: 0,
        rx: constants.TASK_CORNER_RADIUS,
        ry: constants.TASK_CORNER_RADIUS,
        width: 'calc(w)',
        height: 'calc(h)',
        fill: 'var(--bpmn-fill)',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
      },
      innerBorder: { display: 'none' },
      typeIcon: { display: 'none', fill: 'var(--bpmn-stroke)' },
      templateIcon: { display: 'none' },
      marker0: { display: 'none', fill: 'var(--bpmn-stroke)' },
      marker1: { display: 'none', fill: 'var(--bpmn-stroke)' },
      marker2: { display: 'none', fill: 'var(--bpmn-stroke)' },
      marker3: { display: 'none', fill: 'var(--bpmn-stroke)' },
    },
  },
  {
    markup: [
      { tagName: 'rect', selector: 'body', className: 'bpmn-activity-body' },
      { tagName: 'rect', selector: 'innerBorder', className: 'bpmn-activity-inner-border' },
      { tagName: 'path', selector: 'typeIcon', className: 'bpmn-activity-icon' },
      // An applied element template's icon replaces the type glyph (see template-icon.ts).
      { tagName: 'image', selector: 'templateIcon', className: 'bpmn-template-icon' },
      // Four slots because compensation, one loop/multi-instance marker, the collapsed plus and
      // the ad-hoc tilde can all apply at once (section 4.3).
      { tagName: 'path', selector: 'marker0', className: 'bpmn-activity-marker' },
      { tagName: 'path', selector: 'marker1', className: 'bpmn-activity-marker' },
      { tagName: 'path', selector: 'marker2', className: 'bpmn-activity-marker' },
      { tagName: 'path', selector: 'marker3', className: 'bpmn-activity-marker' },
      labelNode,
    ],
  },
);

/** Gateways: a diamond plus a centred glyph (section 1.3, section 4.1). */
export const BpmnGatewayElement = dia.Element.define(
  'bpmn.Gateway',
  {
    size: { width: constants.GATEWAY_SIZE, height: constants.GATEWAY_SIZE },
    attrs: {
      root: connectableRoot,
      body: {
        refPoints: '0,0.5 0.5,0 1,0.5 0.5,1',
        fill: 'var(--bpmn-fill)',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
      },
      icon: { display: 'none', fill: 'var(--bpmn-stroke)', stroke: 'none' },
    },
  },
  {
    markup: [
      { tagName: 'polygon', selector: 'body', className: 'bpmn-gateway-body' },
      { tagName: 'path', selector: 'icon', className: 'bpmn-gateway-icon' },
      labelNode,
    ],
  },
);

/**
 * Pools and Lanes: a body rectangle, a header band and a header label that is rotated
 * with the band when the pool is horizontal (section 1.5, section 10.4).
 */
export const BpmnSwimlaneElement = dia.Element.define(
  'bpmn.Swimlane',
  {
    size: { width: constants.POOL_WIDTH, height: constants.POOL_HEIGHT },
    attrs: {
      root: connectableRoot,
      body: {
        x: 0,
        y: 0,
        width: 'calc(w)',
        height: 'calc(h)',
        fill: 'var(--bpmn-fill)',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
      },
      header: {
        x: 0,
        y: 0,
        fill: 'var(--bpmn-header-fill)',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
      },
    },
  },
  {
    markup: [
      { tagName: 'rect', selector: 'body', className: 'bpmn-swimlane-body' },
      { tagName: 'rect', selector: 'header', className: 'bpmn-swimlane-header' },
      labelNode,
    ],
  },
);

/** Data Objects and Data Stores: one free-form outline path plus an external label. */
export const BpmnDataElement = dia.Element.define(
  'bpmn.Data',
  {
    size: { width: constants.DATA_OBJECT_WIDTH, height: constants.DATA_OBJECT_HEIGHT },
    attrs: {
      root: connectableRoot,
      body: {
        fill: 'var(--bpmn-fill)',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
      },
      decoration: { fill: 'none', stroke: 'var(--bpmn-stroke)', strokeWidth: constants.STROKE_THIN },
      marker: { display: 'none', fill: 'var(--bpmn-stroke)' },
    },
  },
  {
    markup: [
      { tagName: 'path', selector: 'body', className: 'bpmn-data-body' },
      { tagName: 'path', selector: 'decoration', className: 'bpmn-data-decoration' },
      { tagName: 'path', selector: 'marker', className: 'bpmn-data-marker' },
      labelNode,
    ],
  },
);

/**
 * Text Annotation: an open left bracket with left-aligned text (section 1.6).
 *
 * The bracket is a thin unfilled stroke and the box is otherwise empty, so grabbing one took a
 * pixel-exact press on the bracket or on a glyph, and a miss panned the view. Like a Group, it takes
 * clicks on a band around its outline instead; the text block is hit as a whole rather than glyph by
 * glyph. The rest of the box stays click-through, so it never hides what lies beside the text.
 */
export const BpmnTextAnnotationElement = dia.Element.define(
  'bpmn.TextAnnotation',
  {
    size: { width: constants.TEXT_ANNOTATION_WIDTH, height: constants.TEXT_ANNOTATION_HEIGHT },
    attrs: {
      root: connectableRoot,
      background: {
        width: 'calc(w)',
        height: 'calc(h)',
        fill: 'none',
        stroke: 'none',
        'pointer-events': 'none',
      },
      hitArea: {
        fill: 'none',
        stroke: 'transparent',
        strokeWidth: constants.OUTLINE_HIT_TOLERANCE * 2,
        'pointer-events': 'stroke',
      },
      textHitArea: {
        ref: 'label',
        refX: 0,
        refY: 0,
        refWidth: '100%',
        refHeight: '100%',
        fill: 'transparent',
        stroke: 'none',
        'pointer-events': 'fill',
      },
      bracket: {
        fill: 'none',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
        'pointer-events': 'none',
      },
    },
  },
  {
    markup: [
      { tagName: 'rect', selector: 'background' },
      { tagName: 'path', selector: 'hitArea', className: 'bpmn-annotation-hit-area' },
      { tagName: 'rect', selector: 'textHitArea', className: 'bpmn-annotation-text-hit-area' },
      { tagName: 'path', selector: 'bracket', className: 'bpmn-annotation-bracket' },
      labelNode,
    ],
  },
);

/**
 * Group: a dashed rounded rectangle that is visual only.
 *
 * A Group never becomes a containment parent — moving, resizing or deleting it must not
 * touch the cells it visually encloses (specification section 2.8.1, CT-003). Keeping it
 * as an ordinary non-embedding element is how that invariant is enforced structurally
 * rather than by convention.
 */
export const BpmnGroupElement = dia.Element.define(
  'bpmn.Group',
  {
    size: { width: constants.GROUP_WIDTH, height: constants.GROUP_HEIGHT },
    attrs: {
      // A Group is visual only (CT-003): it never becomes a containment parent or connector
      // target. Only a band around its outline receives pointer events, so it can be selected
      // without blocking clicks on what it encloses.
      root: { magnet: false },
      hitArea: {
        x: 0,
        y: 0,
        rx: constants.TASK_CORNER_RADIUS,
        ry: constants.TASK_CORNER_RADIUS,
        width: 'calc(w)',
        height: 'calc(h)',
        fill: 'none',
        // Undashed and wide: the visible outline is thin and dashed, so hitting it took a
        // pixel-exact click on a dash.
        stroke: 'transparent',
        strokeWidth: constants.OUTLINE_HIT_TOLERANCE * 2,
        'pointer-events': 'stroke',
      },
      body: {
        x: 0,
        y: 0,
        rx: constants.TASK_CORNER_RADIUS,
        ry: constants.TASK_CORNER_RADIUS,
        width: 'calc(w)',
        height: 'calc(h)',
        fill: 'none',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
        strokeDasharray: constants.DASH_GROUP,
        'pointer-events': 'none',
      },
    },
  },
  {
    markup: [
      { tagName: 'rect', selector: 'hitArea', className: 'bpmn-group-hit-area' },
      { tagName: 'rect', selector: 'body', className: 'bpmn-group-body' },
      labelNode,
    ],
  },
);

/** One link class covers every connector; markers and dashes differentiate them. */
/**
 * The node that draws a cell's own silhouette, per shape family.
 *
 * Kept beside the markup it names, because it is a fact about the markup: an Event's outline is its
 * outer ring, a Gateway's is its diamond, a Text Annotation's is its bracket, and a connector's is
 * the line. None of them is the view's root — that group also holds the label, the type icon and
 * the bottom markers, so anything derived from it is a bounding box around all of them rather than
 * the shape's own border.
 *
 * @param cell The cell to find the outline node of.
 * @returns The markup selector to trace, or `root` for a cell this module did not build.
 */
export function outlineSelectorOf(cell: dia.Cell): string {
  switch (cell.get('type') as string) {
    case 'bpmn.Event':
      return 'outerRing';

    case 'bpmn.TextAnnotation':
      return 'bracket';

    case 'bpmn.Connector':
      return 'line';

    case 'bpmn.Activity':
    case 'bpmn.Gateway':
    case 'bpmn.Swimlane':
    case 'bpmn.Data':
    case 'bpmn.Group':
      return 'body';

    default:
      // A cell from outside this module still has to highlight as something.
      return 'root';
  }
}

export const BpmnConnectorLink = dia.Link.define(
  'bpmn.Connector',
  {
    connector: { name: 'bpmnRounded', args: { radius: constants.CONNECTOR_CORNER_RADIUS } },
    attrs: {
      line: {
        connection: true,
        fill: 'none',
        stroke: 'var(--bpmn-stroke)',
        strokeWidth: constants.STROKE_THIN,
      },
      wrapper: {
        connection: true,
        strokeWidth: 10,
        stroke: 'transparent',
        fill: 'none',
        strokeLinecap: 'round',
      },
      defaultMarker: { display: 'none' },
    },
  },
  {
    markup: [
      { tagName: 'path', selector: 'wrapper', attributes: { 'pointer-events': 'stroke' } },
      { tagName: 'path', selector: 'line', className: 'bpmn-connector-line' },
      { tagName: 'path', selector: 'defaultMarker', className: 'bpmn-connector-default-marker' },
    ],
  },
);
