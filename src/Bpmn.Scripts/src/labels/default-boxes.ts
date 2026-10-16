import * as constants from '../geometry/constants.js';
import type { BoundsContract } from '../model/contracts.js';

import { defaultLabelStyle, type LabelStyle } from './label-layout.js';

/**
 * Where a label sits when the document supplies no explicit BPMNLabel.Bounds.
 *
 * Section 4.5 calls for one deterministic default box per element kind. Keeping that
 * table here — rather than inside each shape — is what lets the inline editor in M4
 * position itself over exactly the same rectangle the renderer used.
 */
export type LabelPlacement =
  /** Inside the shape: Activities, Text Annotations, expanded Sub-Process headers. */
  | 'internal'
  /** Below the shape: Events, Gateways, Data Objects and Stores. */
  | 'external'
  /** Inside the Pool/Lane header band, rotated with it when the pool is horizontal. */
  | 'header'
  /** At the midpoint of a connector route. */
  | 'edge';

export interface DefaultLabelBox {
  readonly box: BoundsContract;
  readonly style: LabelStyle;
  readonly placement: LabelPlacement;
}

export function internalLabelBox(shape: BoundsContract): DefaultLabelBox {
  return {
    box: shape,
    style: { ...defaultLabelStyle, horizontalAlign: 'middle', verticalAlign: 'middle' },
    placement: 'internal',
  };
}

/** Centred under the shape, the conventional placement for Events and Gateways. */
/** Which side of its shape an external label sits on. */
export type LabelSide = 'top' | 'bottom' | 'left' | 'right';

/**
 * The box for a label drawn outside its shape.
 *
 * Below the shape unless told otherwise, which is where BPMN puts an Event's name. A Boundary Event
 * is the exception that needs the rest: it sits on its host's border, so "below" lands the label on
 * top of the host whenever the Event is anywhere but the bottom edge — the name then reads as part
 * of the Activity it is attached to, over its fill and often over its own text.
 *
 * @param shape The shape's box, in the coordinates the label will be placed in.
 * @param side Which way to put the label; the caller decides what "away" means.
 */
export function externalLabelBox(shape: BoundsContract, side: LabelSide = 'bottom'): DefaultLabelBox {
  const height = constants.LABEL_FONT_SIZE * constants.LABEL_LINE_HEIGHT;
  const gap = constants.EXTERNAL_LABEL_GAP;

  if (side === 'left' || side === 'right') {
    const width = constants.EXTERNAL_LABEL_WIDTH;

    return {
      box: {
        x: side === 'left' ? shape.x - gap - width : shape.x + shape.width + gap,
        y: shape.y + (shape.height / 2) - (height / 2),
        width,
        height,
      },
      // Aligned towards the shape, so the text starts beside it rather than a label-width away.
      style: {
        ...defaultLabelStyle,
        horizontalAlign: side === 'left' ? 'end' : 'start',
        verticalAlign: 'middle',
      },
      placement: 'external',
    };
  }

  const width = Math.max(constants.EXTERNAL_LABEL_WIDTH, shape.width);

  return {
    box: {
      x: shape.x + shape.width / 2 - width / 2,
      y: side === 'top' ? shape.y - gap - height : shape.y + shape.height + gap,
      width,
      height,
    },
    style: {
      ...defaultLabelStyle,
      horizontalAlign: 'middle',
      verticalAlign: side === 'top' ? 'bottom' : 'top',
    },
    placement: 'external',
  };
}

/**
 * Whether an imported box for an external label is close enough to its shape to be believed.
 *
 * @param box The imported label box.
 * @param shape The shape it labels, in the same coordinates.
 * @returns `false` when the gap between the two rectangles exceeds the plausibility limit.
 */
export function isPlausibleExternalLabel(box: BoundsContract, shape: BoundsContract): boolean {
  const dx = Math.max(0, shape.x - (box.x + box.width), box.x - (shape.x + shape.width));
  const dy = Math.max(0, shape.y - (box.y + box.height), box.y - (shape.y + shape.height));

  return Math.hypot(dx, dy) <= constants.EXTERNAL_LABEL_MAX_DISTANCE;
}

/**
 * The Pool/Lane header band. Text always wraps in the band's unrotated local coordinate
 * system and the whole band is rotated afterwards, so both orientations use one
 * algorithm and produce the same line breaks (section 4.5).
 */
export function headerLabelBox(shape: BoundsContract, isHorizontal: boolean): DefaultLabelBox {
  const box = isHorizontal
    ? { x: 0, y: 0, width: shape.height, height: constants.POOL_HEADER_SIZE }
    : { x: 0, y: 0, width: shape.width, height: constants.POOL_HEADER_SIZE };

  return {
    box,
    style: { ...defaultLabelStyle, horizontalAlign: 'middle', verticalAlign: 'middle' },
    placement: 'header',
  };
}

/** Left-aligned inside the bracket, per the Text Annotation notation (section 1.6). */
export function annotationLabelBox(shape: BoundsContract): DefaultLabelBox {
  return {
    box: {
      x: shape.x + constants.LABEL_PADDING_X * 2,
      y: shape.y,
      width: shape.width - constants.LABEL_PADDING_X * 3,
      height: shape.height,
    },
    style: { ...defaultLabelStyle, horizontalAlign: 'start', verticalAlign: 'top' },
    placement: 'internal',
  };
}

/** A small box at the route midpoint; the edge's waypoints are never changed by it. */
export function edgeLabelBox(): DefaultLabelBox {
  return {
    box: {
      x: 0,
      y: 0,
      width: constants.EXTERNAL_LABEL_WIDTH,
      height: constants.LABEL_FONT_SIZE * constants.LABEL_LINE_HEIGHT,
    },
    style: { ...defaultLabelStyle, horizontalAlign: 'middle', verticalAlign: 'middle' },
    placement: 'edge',
  };
}
