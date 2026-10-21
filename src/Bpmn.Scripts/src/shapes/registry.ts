import type { dia } from '@joint/core';

import * as constants from '../geometry/constants.js';
import type { FontMetricsProvider } from '../labels/font-metrics.js';
import {
  annotationLabelBox,
  edgeLabelBox,
  externalLabelBox,
  type LabelSide,
  headerLabelBox,
  internalLabelBox,
  isPlausibleExternalLabel,
} from '../labels/default-boxes.js';
import { edgeLabelPosition } from '../labels/edge-label-position.js';
import * as markers from '../markers/connector-markers.js';
import { layoutActivityMarkers, markersFor, type MarkerPlacement } from '../markers/activity-markers.js';
import { ringsFor } from '../markers/event-rings.js';
import { dataMarkerIcon, eventTriggerIcon, gatewayIcon, taskTypeIcon, type BpmnIcon } from '../icons/registry.js';
import type {
  BoundsContract,
  BpmnAssociationContract,
  BpmnDataAssociationContract,
  BpmnDataObjectReferenceContract,
  BpmnDataStoreReferenceContract,
  BpmnFlowNodeContract,
  BpmnGroupContract,
  BpmnMessageFlowContract,
  BpmnSequenceFlowContract,
  BpmnShapeContract,
  BpmnTextAnnotationContract,
  PointContract,
} from '../model/contracts.js';
import {
  eventContextOf,
  isExpandedContainer,
  isSubProcessKind,
  kindOf,
  type BpmnEventContext,
} from '../model/element-kind.js';

import { labelAttrsFor, localBox } from './base.js';
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
import { templateIconOf } from './template-icon.js';

/**
 * Builds JointJS cells from semantic elements and their DI.
 *
 * This module is the one place notation is decided. Every factory reads the semantic
 * element for meaning and the BPMNShape/BPMNEdge for geometry, and never the other way
 * round: DI bounds are used verbatim, never recomputed, so a round trip cannot move a
 * diagram (specification sections 10.6 and 11).
 *
 * Trigger, gateway and task-type glyphs are resolved from the generated icon registry
 * (section 4.1 is explicit that hand-authored paths must not become a second symbol
 * catalog). That registry is not vendored yet, so glyph nodes stay hidden and shapes
 * render border, fill and label only.
 */

export interface CellFactoryContext {
  readonly metrics: FontMetricsProvider;

  /**
   * The bounds of another element on the same plane, when the renderer can supply them.
   *
   * Only a Boundary Event needs this, and only to keep its label off the Activity it is attached
   * to. Passing a lookup rather than the whole plane keeps the factories from being able to reach
   * for anything else about the document.
   */
  readonly boundsOf?: (elementId: string) => BoundsContract | null;
}

const metadata = (
  elementId: string,
  elementType: string,
  containerId: string | null,
  poolId: string | null,
  // The DI entry this cell was built from. An edit reports geometry against the shape, not the
  // semantic element: one element may be drawn on several planes, and it is the shape that moved.
  diShapeId: string | null = null,
  // Only a swimlane declares one. Section 1.5.3 makes auto-routing depend on it, and the routing
  // code has a cell and a graph rather than a document, so the fact has to travel on the cell.
  isHorizontal: boolean | null = null,
  // Whether a Sub-Process is drawn expanded; null for everything that cannot be. Three consumers
  // need it and none of them has the document: what a double-click means, whether a drop may land
  // inside, and what the accessible tree announces.
  isExpanded: boolean | null = null,
  // The element's own name. Read for accessibility, which used to take it from the drawn label —
  // and an expanded Sub-Process no longer draws one, so the name had to stop depending on that.
  name: string | null = null,
  // Where the collapsed "+" is drawn, in the element's own coordinates, or null when there is no
  // "+" on this shape. The expand control is a real HTML button positioned over the glyph, and it
  // reads this rather than recomputing the marker band: section 4.3's layout centres the whole
  // group, so the plus slides left and right as compensation, loop and ad-hoc markers come and go,
  // and a second implementation of that arithmetic would drift from the first.
  expandMarker: MarkerPlacement | null = null,
): Record<string, unknown> => ({
  bpmn: { elementId, elementType, containerId, poolId, isHorizontal, isExpanded, name, expandMarker },
  diShapeId,
});

/**
 * Adds the semantic facts the connection rules read to a flow node's metadata.
 *
 * Message Flow legality depends on the Event's trigger (MF-007/MF-008) and Sequence Flow legality
 * on compensation and Link definitions, so the cell has to carry them: the connect gesture has a
 * cell, not the document.
 */
const withFlowNodeFacts = (
  attributes: Record<string, unknown>,
  node: BpmnFlowNodeContract,
): Record<string, unknown> => ({
  ...attributes,
  bpmn: {
    ...(attributes.bpmn as Record<string, unknown>),
    eventDefinition: node.eventDefinition,
    isForCompensation: node.isForCompensation,
  },
});

/**
 * Places a manifest glyph into a shape's local coordinates.
 *
 * Glyphs are normalized into a 24-unit box, so they are scaled to the target size and translated
 * into position. The transform goes on the path rather than being baked into the data because the
 * same glyph is reused at several sizes: 18 units inside an Event or a Task corner, 20 inside a
 * Gateway, 14 in the bottom marker band.
 */
const glyphAttrs = (glyph: BpmnIcon, x: number, y: number, size: number): Record<string, unknown> => {
  const scale = size / GLYPH_BOX;
  return {
    display: 'inline',
    d: glyph.pathData,
    transform: `translate(${x.toString()},${y.toString()}) scale(${scale.toString()})`,
  };
};

/** The box every manifest glyph is normalized into. */
const GLYPH_BOX = 24;

/** Class that hides a notation glyph an element-template icon is drawn in place of. */
export const TEMPLATE_REPLACED_CLASS = 'bpmn-template-replaced';

/**
 * Marks a glyph as replaced by a template icon, or restores its plain class.
 *
 * Hidden by CSS rather than by `display: none` so that forced-colors mode, where an authored
 * image cannot follow the system palette, can bring the notation glyph back (BpmnShapes.css).
 * The class is always written so a re-render after the template is removed clears it.
 */
const replacedBy = (
  glyph: Record<string, unknown>,
  icon: string | null,
  baseClass: string,
): Record<string, unknown> => ({
  ...glyph,
  class: icon === null ? baseClass : `${baseClass} ${TEMPLATE_REPLACED_CLASS}`,
});

/** The `<image>` an element-template icon is drawn with, or hidden when there is none. */
const templateIconAttrs = (icon: string | null, x: number, y: number, size: number): Record<string, unknown> =>
  icon === null
    ? { display: 'none', href: null }
    : {
        display: 'inline',
        href: icon,
        x,
        y,
        width: size,
        height: size,
        preserveAspectRatio: 'xMidYMid meet',
        'aria-hidden': 'true',
      };

const positionOf = (bounds: BoundsContract): { x: number; y: number } => ({ x: bounds.x, y: bounds.y });
const sizeOf = (bounds: BoundsContract): { width: number; height: number } => ({
  width: bounds.width,
  height: bounds.height,
});

/**
 * Creates the cell for a flow node.
 *
 * Exhaustive over the notational families rather than over the 23 element types: adding
 * a type to BpmnFlowNodeType forces a decision in kindOf, which then routes here.
 */
export function createFlowNodeCell(
  node: BpmnFlowNodeContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): dia.Element {
  switch (kindOf(node.type)) {
    case 'event':
      return createEventCell(node, shape, context);
    case 'activity':
      return createActivityCell(node, shape, context);
    case 'gateway':
      return createGatewayCell(node, shape, context);
  }
}

function createEventCell(
  node: BpmnFlowNodeContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): dia.Element {
  const context_ = eventContextOf(node.type);
  // kindOf already proved this is an event; the null branch is unreachable by
  // construction and only satisfies the type system.
  const rings = ringsFor(context_ ?? 'start', node.isInterrupting);
  const [outer, inner] = rings;

  const center = { cx: shape.bounds.width / 2, cy: shape.bounds.height / 2 };
  const radiusScale = shape.bounds.width / constants.EVENT_SIZE;
  const icon = templateIconOf(node);
  const iconSize = constants.EVENT_TEMPLATE_ICON_BOX * radiusScale;

  const cell = new BpmnEventElement({
    id: node.id,
    position: positionOf(shape.bounds),
    size: sizeOf(shape.bounds),
    ...withFlowNodeFacts(metadata(node.id, node.type, node.containerId, node.poolId, shape.id), node),
    attrs: {
      outerRing: {
        ...center,
        r: (outer?.radius ?? constants.EVENT_OUTER_RADIUS) * radiusScale,
        strokeWidth: outer?.strokeWidth ?? constants.STROKE_THIN,
        strokeDasharray: outer?.dashArray ?? 'none',
      },
      innerRing:
        inner === undefined
          ? { display: 'none' }
          : {
              display: 'inline',
              ...center,
              r: inner.radius * radiusScale,
              strokeWidth: inner.strokeWidth,
              strokeDasharray: inner.dashArray ?? 'none',
              fill: 'none',
            },
      icon: replacedBy(eventGlyphAttrs(node, shape, context_ ?? 'start'), icon, 'bpmn-event-icon'),
      templateIcon: templateIconAttrs(
        icon,
        shape.bounds.width / 2 - iconSize / 2,
        shape.bounds.height / 2 - iconSize / 2,
        iconSize,
      ),
      label: labelAttrsForShape(
        node.name,
        shape,
        externalLabelBox(localBox(shape.bounds), labelSideFor(node, shape, context)),
        context,
      ),
    },
  });

  return cell;
}

/**
 * Which side of a Boundary Event its name goes on: the one facing away from its host.
 *
 * An Event attached to the top edge of a Task has its label below it, which is inside the Task.
 * Section 4.5 puts an Event's name outside the symbol, and a name printed over the Activity it is
 * attached to belongs to neither of them as far as a reader is concerned.
 *
 * Everything that is not a Boundary Event keeps the default, because nothing else has a shape it
 * is required to overlap.
 */
function labelSideFor(
  node: BpmnFlowNodeContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): LabelSide {
  if (node.type !== 'boundaryEvent' || node.attachedToRef === null) return 'bottom';

  const host = context.boundsOf?.(node.attachedToRef);
  if (host === null || host === undefined) return 'bottom';

  const eventX = shape.bounds.x + (shape.bounds.width / 2);
  const eventY = shape.bounds.y + (shape.bounds.height / 2);
  const hostX = host.x + (host.width / 2);
  const hostY = host.y + (host.height / 2);

  // Whichever axis the Event is further out on decides the side, so an Event on a corner leans the
  // way it is most clearly outside.
  const dx = eventX - hostX;
  const dy = eventY - hostY;

  if (Math.abs(dx) / Math.max(host.width, 1) > Math.abs(dy) / Math.max(host.height, 1)) {
    return dx >= 0 ? 'right' : 'left';
  }

  return dy >= 0 ? 'bottom' : 'top';
}

function createActivityCell(
  node: BpmnFlowNodeContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): dia.Element {
  // Event Sub-Processes are drawn with a dashed border and Call Activities with a thick
  // one; both are notational, not decorative (section 1.2).
  const isEventSubProcess = node.type === 'eventSubProcess';
  const isCallActivity = node.type === 'callActivity';
  const isTransaction = node.type === 'transaction';

  // Laid out once and read twice: the marker attributes draw the glyphs, and the metadata tells
  // the expand control where the plus among them landed.
  const placements = layoutActivityMarkers(markersFor(node, shape.isExpanded), shape.bounds);
  const icon = templateIconOf(node);

  const cell = new BpmnActivityElement({
    id: node.id,
    position: positionOf(shape.bounds),
    size: sizeOf(shape.bounds),
    ...withFlowNodeFacts(metadata(
      node.id,
      node.type,
      node.containerId,
      node.poolId,
      shape.id,
      null,
      isSubProcessKind(node.type) ? shape.isExpanded === true : null,
      node.name,
      placements.find((placement) => placement.marker === 'collapsed') ?? null,
    ), node),
    attrs: {
      body: {
        strokeDasharray: isEventSubProcess ? constants.DASH_EVENT_SUBPROCESS : 'none',
        strokeWidth: isCallActivity ? constants.STROKE_THICK : constants.STROKE_THIN,
      },
      innerBorder: isTransaction
        ? {
            display: 'inline',
            x: constants.TRANSACTION_BORDER_INSET,
            y: constants.TRANSACTION_BORDER_INSET,
            width: shape.bounds.width - constants.TRANSACTION_BORDER_INSET * 2,
            height: shape.bounds.height - constants.TRANSACTION_BORDER_INSET * 2,
            rx: constants.TASK_CORNER_RADIUS - constants.TRANSACTION_BORDER_INSET,
            ry: constants.TASK_CORNER_RADIUS - constants.TRANSACTION_BORDER_INSET,
            fill: 'none',
            stroke: 'var(--bpmn-stroke)',
            strokeWidth: constants.STROKE_THIN,
          }
        : { display: 'none' },
      typeIcon: replacedBy(taskGlyphAttrs(node), icon, 'bpmn-activity-icon'),
      templateIcon: templateIconAttrs(icon, constants.TASK_ICON_INSET, constants.TASK_ICON_INSET, constants.TASK_ICON_BOX),
      ...activityMarkerAttrs(placements),
      /*
       * An expanded Sub-Process draws no name at all.
       *
       * Not a header band: that is a Pool's notation, and lending it to a Sub-Process tells a
       * reader the wrong thing about what they are looking at. Not the centre either, because the
       * centre is where the contents are. The name is still in the model, the property panel, the
       * accessible tree and the label editor — it is only not drawn, because an expanded
       * container's interior belongs to its children.
       */
      label: isExpandedContainer(node, shape.isExpanded)
        ? { ...labelAttrsForShape(node.name, shape, internalLabelBox(localBox(shape.bounds)), context), display: 'none' }
        : labelAttrsForShape(node.name, shape, internalLabelBox(localBox(shape.bounds)), context),
    },
  });

  return cell;
}

function createGatewayCell(
  node: BpmnFlowNodeContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): dia.Element {
  return new BpmnGatewayElement({
    id: node.id,
    position: positionOf(shape.bounds),
    size: sizeOf(shape.bounds),
    ...withFlowNodeFacts(metadata(node.id, node.type, node.containerId, node.poolId, shape.id), node),
    attrs: {
      icon: gatewayGlyphAttrs(node, shape),
      label: labelAttrsForShape(node.name, shape, externalLabelBox(localBox(shape.bounds)), context),
    },
  });
}

/**
 * The collection marker, centred on a Data Object's bottom edge (section 1.6).
 *
 * A collection Data Object is drawn with the same three-bar symbol as parallel multi-instance,
 * which is why the manifest maps both onto one glyph rather than carrying it twice.
 */
function collectionMarkerAttrs(isCollection: boolean, shape: BpmnShapeContract): Record<string, unknown> {
  if (!isCollection) return { display: 'none' };

  const glyph = dataMarkerIcon('collection');
  if (glyph === undefined) return { display: 'none' };

  const size = constants.DATA_MARKER_BOX;
  return glyphAttrs(
    glyph,
    shape.bounds.width / 2 - size / 2,
    shape.bounds.height - size - constants.DATA_MARKER_INSET,
    size,
  );
}

/** The type glyph in the Activity's top-left corner (section 4.4). */
function taskGlyphAttrs(node: BpmnFlowNodeContract): Record<string, unknown> {
  const glyph = taskTypeIcon(node.type);
  if (glyph === undefined) return { display: 'none' };

  return glyphAttrs(glyph, constants.TASK_ICON_INSET, constants.TASK_ICON_INSET, constants.TASK_ICON_BOX);
}

/**
 * The bottom-centre marker band (section 4.3).
 *
 * Returns one attribute entry per marker slot. Slots beyond the markers this Activity actually
 * carries are hidden rather than removed, because the markup is static: JointJS reuses a view
 * instead of rebuilding its DOM when an attribute changes.
 */
function activityMarkerAttrs(
  placements: readonly MarkerPlacement[],
): Record<string, Record<string, unknown>> {
  const attrs: Record<string, Record<string, unknown>> = {};
  for (let slot = 0; slot < ACTIVITY_MARKER_SLOTS; slot += 1) {
    const placement = placements[slot];
    attrs[`marker${slot.toString()}`] =
      placement === undefined
        ? { display: 'none' }
        : {
            display: 'inline',
            d: placement.pathData,
            transform:
              `translate(${placement.x.toString()},${placement.y.toString()}) `
              + `scale(${(placement.size / GLYPH_BOX).toString()})`,
          };
  }
  return attrs;
}

/** Slots declared in the Activity markup; section 4.3 allows at most four markers at once. */
const ACTIVITY_MARKER_SLOTS = 4;

/** The trigger glyph, centred in an 18-unit box inside the rings (section 4.2). */
function eventGlyphAttrs(
  node: BpmnFlowNodeContract,
  shape: BpmnShapeContract,
  eventContext: BpmnEventContext,
): Record<string, unknown> {
  const glyph = eventTriggerIcon(eventContext, node.eventDefinition, node.isInterrupting);
  if (glyph === undefined) return { display: 'none' };

  // Scale the glyph box with the shape so an imported Event drawn larger than 36 units keeps its
  // proportions.
  const size = constants.EVENT_ICON_BOX * (shape.bounds.width / constants.EVENT_SIZE);
  return glyphAttrs(glyph, shape.bounds.width / 2 - size / 2, shape.bounds.height / 2 - size / 2, size);
}

/** The type glyph, centred in a 20-unit box inside the diamond (section 4.1). */
function gatewayGlyphAttrs(node: BpmnFlowNodeContract, shape: BpmnShapeContract): Record<string, unknown> {
  const glyph = gatewayIcon(node.type);
  if (glyph === undefined) return { display: 'none' };

  const size = constants.GATEWAY_ICON_BOX * (shape.bounds.width / constants.GATEWAY_SIZE);
  return glyphAttrs(glyph, shape.bounds.width / 2 - size / 2, shape.bounds.height / 2 - size / 2, size);
}

/**
 * Pools and Lanes.
 *
 * isHorizontal decides which edge carries the header band and whether its label is
 * rotated; a Lane always inherits the value from its Pool rather than storing an
 * independent one (section 10.4, DI-006/DI-007).
 */
export function createSwimlaneCell(
  elementId: string,
  name: string | null,
  shape: BpmnShapeContract,
  isHorizontal: boolean,
  role: 'pool' | 'lane',
  context: CellFactoryContext,
): dia.Element {
  const header = isHorizontal
    ? { x: 0, y: 0, width: constants.POOL_HEADER_SIZE, height: shape.bounds.height }
    : { x: 0, y: 0, width: shape.bounds.width, height: constants.POOL_HEADER_SIZE };

  const box = headerLabelBox(shape.bounds, isHorizontal);
  const labelAttrs = labelAttrsFor(name ?? '', box.box, box.style, context.metrics);

  // The band's text is laid out unrotated and the whole label is then rotated with the
  // band, so both orientations break lines identically (section 4.5).
  const transform = isHorizontal
    ? `rotate(-90, ${(constants.POOL_HEADER_SIZE / 2).toString()}, ${(shape.bounds.height / 2).toString()})`
    : '';

  return new BpmnSwimlaneElement({
    id: elementId,
    position: positionOf(shape.bounds),
    size: sizeOf(shape.bounds),
    ...metadata(elementId, role, null, role === 'pool' ? elementId : null, shape.id, isHorizontal),
    attrs: {
      body: { fill: role === 'pool' ? 'var(--bpmn-fill)' : 'none' },
      header: header,
      label: {
        ...labelAttrs,
        x: isHorizontal ? constants.POOL_HEADER_SIZE / 2 : shape.bounds.width / 2,
        y: isHorizontal ? shape.bounds.height / 2 : constants.POOL_HEADER_SIZE / 2,
        textAnchor: 'middle',
        textVerticalAnchor: 'middle',
        transform,
      },
    },
  });
}

export function createDataObjectCell(
  reference: BpmnDataObjectReferenceContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): dia.Element {
  const { width, height } = shape.bounds;
  const fold = Math.min(width, height) * 0.3;

  return new BpmnDataElement({
    id: reference.id,
    position: positionOf(shape.bounds),
    size: sizeOf(shape.bounds),
    ...metadata(reference.id, 'dataObjectReference', reference.containerId, null, shape.id),
    attrs: {
      // Document outline with the conventional folded top-right corner.
      body: {
        d: `M 0 0 H ${(width - fold).toString()} L ${width.toString()} ${fold.toString()} V ${height.toString()} H 0 Z`,
      },
      decoration: {
        d: `M ${(width - fold).toString()} 0 V ${fold.toString()} H ${width.toString()}`,
      },
      marker: collectionMarkerAttrs(reference.isCollection, shape),
      label: labelAttrsForShape(reference.name, shape, externalLabelBox(localBox(shape.bounds)), context),
    },
  });
}

export function createDataStoreCell(
  reference: BpmnDataStoreReferenceContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): dia.Element {
  const { width, height } = shape.bounds;
  const rimHeight = Math.min(height * 0.18, 10);
  const rx = width / 2;

  return new BpmnDataElement({
    id: reference.id,
    position: positionOf(shape.bounds),
    size: sizeOf(shape.bounds),
    ...metadata(reference.id, 'dataStoreReference', reference.containerId, null, shape.id),
    attrs: {
      // Cylinder: elliptical top rim, straight sides, elliptical base.
      body: {
        d:
          `M 0 ${rimHeight.toString()} A ${rx.toString()} ${rimHeight.toString()} 0 0 1 ${width.toString()} ${rimHeight.toString()}` +
          ` V ${(height - rimHeight).toString()} A ${rx.toString()} ${rimHeight.toString()} 0 0 1 0 ${(height - rimHeight).toString()} Z`,
      },
      decoration: {
        d: `M 0 ${rimHeight.toString()} A ${rx.toString()} ${rimHeight.toString()} 0 0 0 ${width.toString()} ${rimHeight.toString()}`,
      },
      label: labelAttrsForShape(reference.name, shape, externalLabelBox(localBox(shape.bounds)), context),
    },
  });
}

export function createTextAnnotationCell(
  annotation: BpmnTextAnnotationContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): dia.Element {
  const { width, height } = shape.bounds;
  const bracketWidth = Math.min(10, width * 0.2);
  const box = annotationLabelBox(localBox(shape.bounds));
  const bracket = `M ${bracketWidth.toString()} 0 H 0 V ${height.toString()} H ${bracketWidth.toString()}`;

  return new BpmnTextAnnotationElement({
    id: annotation.id,
    position: positionOf(shape.bounds),
    size: sizeOf(shape.bounds),
    ...metadata(annotation.id, 'textAnnotation', annotation.containerId, null, shape.id),
    attrs: {
      hitArea: { d: bracket },
      bracket: { d: bracket },
      label: labelAttrsFor(annotation.text, box.box, box.style, context.metrics),
    },
  });
}

export function createGroupCell(
  group: BpmnGroupContract,
  shape: BpmnShapeContract,
  context: CellFactoryContext,
): dia.Element {
  const box = localBox(shape.bounds);

  return new BpmnGroupElement({
    id: group.id,
    position: positionOf(shape.bounds),
    size: sizeOf(shape.bounds),
    ...metadata(group.id, 'group', group.containerId, null, shape.id),
    attrs: {
      label: labelAttrsFor(
        group.name ?? '',
        { x: box.x, y: box.y, width: box.width, height: constants.LABEL_FONT_SIZE * 2 },
        { ...internalLabelBox(box).style, verticalAlign: 'top' },
        context.metrics,
      ),
    },
  });
}

/**
 * Where a connector meets its endpoint shapes, as offsets from each shape's top-left.
 *
 * Derived from the persisted BPMNDI endpoint waypoints. Imported geometry is used as stored, and
 * already on the shapes' outlines (section 4.6; the renderer puts an end there if the document did
 * not). JointJS would otherwise anchor the line to the shape centres and
 * re-derive a boundary intersection — visibly rerouting an imported diagram.
 */
export interface EdgeEndpoints {
  readonly source: PointContract | null;
  readonly target: PointContract | null;
  readonly sourceLinkRatio?: number;
  readonly targetLinkRatio?: number;
}

export const noEndpoints: EdgeEndpoints = { source: null, target: null };

/**
 * Builds a link end that lands exactly on the given offset from the shape's top-left.
 *
 * connectionPoint 'anchor' is what stops JointJS shortening the line back to the shape outline
 * once the anchor has been placed.
 */
function linkEnd(elementId: string, offset: PointContract | null, linkRatio?: number): dia.Link.EndJSON {
  if (linkRatio !== undefined) {
    return { id: elementId, anchor: { name: 'connectionRatio', args: { ratio: linkRatio } }, connectionPoint: { name: 'anchor' } };
  }
  if (offset === null) return { id: elementId };

  return {
    id: elementId,
    // useModelGeometry is essential: without it the offset is measured from the rendered
    // bounding box, which for an externally-labelled shape includes the label and is both
    // larger than the shape and dependent on the text. The model's position and size are what
    // the DI waypoint was expressed against.
    anchor: { name: 'topLeft', args: { dx: offset.x, dy: offset.y, useModelGeometry: true } },
    connectionPoint: { name: 'anchor' },
  };
}

/** Sequence Flow: solid line, filled arrowhead, optional condition diamond or default tick. */
export function createSequenceFlowLink(
  flow: BpmnSequenceFlowContract,
  waypoints: readonly PointContract[],
  endpoints: EdgeEndpoints,
  label: string | null,
  context: CellFactoryContext,
  labelBounds: BoundsContract | null = null,
): dia.Link {
  return new BpmnConnectorLink({
    id: flow.id,
    source: linkEnd(flow.sourceRef, endpoints.source),
    target: linkEnd(flow.targetRef, endpoints.target),
    vertices: interiorVertices(waypoints),
    ...metadata(flow.id, 'sequenceFlow', flow.containerId, null),
    attrs: {
      line: {
        strokeDasharray: 'none',
        targetMarker: markers.sequenceFlowArrow,
        sourceMarker: flow.conditionExpression === null ? null : markers.conditionalDiamond,
      },
      defaultMarker: flow.isDefault
        ? {
            display: 'inline',
            ...markers.defaultFlowSlash,
            // Without a position the tick renders at the link's own origin rather than on the
            // route, which leaves a default flow indistinguishable from a plain one.
            // atConnectionLength places it a fixed distance from the source and rotates it with
            // the line, so the slash stays at 45 degrees to the flow (section 4).
            atConnectionLength: constants.DEFAULT_FLOW_TICK_OFFSET,
          }
        : { display: 'none' },
    },
    labels: edgeLabels(label, context, waypoints, labelBounds),
  });
}

/** Message Flow: dashed line, hollow source circle, open arrowhead (section 4). */
export function createMessageFlowLink(
  flow: BpmnMessageFlowContract,
  waypoints: readonly PointContract[],
  endpoints: EdgeEndpoints,
  label: string | null,
  context: CellFactoryContext,
  labelBounds: BoundsContract | null = null,
): dia.Link {
  return new BpmnConnectorLink({
    id: flow.id,
    source: linkEnd(flow.sourceRef, endpoints.source),
    target: linkEnd(flow.targetRef, endpoints.target),
    vertices: interiorVertices(waypoints),
    ...metadata(flow.id, 'messageFlow', null, null),
    attrs: {
      line: {
        strokeDasharray: constants.DASH_MESSAGE_FLOW,
        targetMarker: markers.messageFlowArrow,
        sourceMarker: markers.messageFlowCircle,
      },
    },
    labels: edgeLabels(label, context, waypoints, labelBounds),
  });
}

/** Association and Data Association: dotted line, open arrow when directed. */
export function createAssociationLink(
  association: BpmnAssociationContract | BpmnDataAssociationContract,
  waypoints: readonly PointContract[],
  endpoints: EdgeEndpoints,
  isDirected: boolean,
): dia.Link {
  const isDataAssociation = 'kind' in association;
  const sourceRef = isDataAssociation ? (association.sourceRefs[0] ?? '') : association.sourceRef;

  return new BpmnConnectorLink({
    id: association.id,
    source: linkEnd(sourceRef, endpoints.source, endpoints.sourceLinkRatio),
    target: linkEnd(association.targetRef, endpoints.target, endpoints.targetLinkRatio),
    vertices: interiorVertices(waypoints),
    ...metadata(
      association.id,
      isDataAssociation ? 'dataAssociation' : 'association',
      association.containerId,
      null,
    ),
    attrs: {
      line: {
        strokeDasharray: constants.DASH_ASSOCIATION,
        targetMarker: isDirected ? markers.associationArrow : null,
        sourceMarker: null,
      },
    },
  });
}

// --- helpers ---------------------------------------------------------------

function labelAttrsForShape(
  text: string | null,
  shape: BpmnShapeContract,
  fallback: ReturnType<typeof internalLabelBox>,
  context: CellFactoryContext,
): Record<string, unknown> {
  // The semantic name is the only source of label text; DI supplies geometry and style.
  const content = text ?? '';

  // Explicit BPMNLabel.Bounds win, converted to element-local coordinates; a fixed
  // imported box clips rather than growing (section 4.5).
  const imported = content === '' ? undefined : shape.label?.bounds;
  const local = imported
    ? {
        x: imported.x - shape.bounds.x,
        y: imported.y - shape.bounds.y,
        width: imported.width,
        height: imported.height,
      }
    : undefined;

  // An external label nowhere near its shape is bad data, not intent: use the default position.
  const explicit =
    local && (fallback.placement !== 'external' || isPlausibleExternalLabel(local, localBox(shape.bounds)))
      ? local
      : undefined;
  const box = explicit ?? fallback.box;

  // A box shorter than one line (exporters write heights like 5) cannot clip without hiding the text.
  const clips = explicit !== undefined && explicit.height >= constants.LABEL_FONT_SIZE * constants.LABEL_LINE_HEIGHT;
  const style = clips ? { ...fallback.style, clipOverflow: true } : fallback.style;
  return { display: content === '' ? 'none' : 'inline', ...labelAttrsFor(content, box, style, context.metrics) };
}

function edgeLabels(
  text: string | null,
  context: CellFactoryContext,
  waypoints: readonly PointContract[],
  imported: BoundsContract | null,
): readonly unknown[] {
  if (text === null || text === '') return [];

  const defaults = edgeLabelBox();
  // An imported box places the label beside its connector; without one it sits on the midpoint.
  const placed = imported ? edgeLabelPosition(waypoints, imported) : null;
  const box = imported && placed
    ? { x: 0, y: 0, width: imported.width, height: Math.max(imported.height, defaults.box.height) }
    : defaults.box;
  const attrs = labelAttrsFor(text, box, defaults.style, context.metrics);

  return [
    {
      position: placed
        ? {
            distance: placed.distance,
            offset: placed.offset,
            // Pixels along the route and a plain x/y shift, not a ratio and an offset rotated with the segment.
            args: { absoluteDistance: true, absoluteOffset: true },
          }
        : { distance: 0.5 },
      attrs: {
        text: { ...attrs, x: 0, y: 0 },
        rect: { fill: 'var(--bpmn-fill)', stroke: 'none' },
      },
      markup: [
        { tagName: 'rect', selector: 'rect' },
        { tagName: 'text', selector: 'text' },
      ],
    },
  ];
}

/**
 * BPMNDI waypoints include both endpoints; JointJS derives those from the connected
 * cells and wants only the interior bends as vertices. The endpoints are therefore
 * dropped here rather than re-anchored, so an imported route keeps its exact bends
 * (sections 10.6 and 11).
 *
 * For the same reason no link built here declares a router. What the editor draws must be what the
 * document states, so a connector is drawn through its own waypoints and nothing recomputes them.
 * Routing is a command-path concern — see `routing/` — which computes a route once, when the user
 * draws a connector, and stores the result. A router here would re-shape every imported diagram on
 * load instead.
 */
function interiorVertices(waypoints: readonly PointContract[]): readonly PointContract[] {
  return waypoints.length <= 2 ? [] : waypoints.slice(1, -1);
}
