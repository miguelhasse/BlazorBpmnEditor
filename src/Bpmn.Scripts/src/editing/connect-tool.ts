import { dia, elementTools } from '@joint/core';

/*
 * Destructured deliberately. `dia.ToolsView` is declared as both a class and a namespace, and
 * reading it through the namespace makes the lint rule resolve `dia` as a type-only import — which
 * then fails to compile, because the construction below needs the value.
 */
const { ToolsView } = dia;

/**
 * The connector-initiation handle shown on a selected element.
 *
 * `elementTools.Connect` is JointJS's own: dragging it starts a link through the paper's
 * `defaultLink` and `validateConnection`, so the BPMN rules judge the gesture without this module
 * knowing how a drag works.
 *
 * A handle rather than magnets on the shape bodies. A body-wide magnet would consume the
 * pointer-down that begins an element drag, and the two gestures would fight. Where a connector
 * *attaches* is still arbitrary along the outline — section 4.6's requirement is about the
 * attachment point, not about where the drag starts, and no fixed ports are introduced either way.
 */
export function connectTool(elementType?: string): dia.ToolsView {
  const annotation = elementType === 'textAnnotation';
  return new ToolsView({
    name: 'bpmn-connect',
    tools: [
      new elementTools.Connect({
        // An annotation's notation is its left bracket, not its otherwise empty right edge.
        x: annotation ? 0 : '100%',
        y: '50%',
        offset: { x: annotation ? -12 : 12, y: 0 },
        ...(annotation ? { useModelGeometry: true } : {}),
        magnet: 'root',
        markup: [
          {
            tagName: 'circle',
            selector: 'button',
            attributes: {
              'r': 7,
              'fill': 'var(--bpmn-selection-color)',
              'stroke': 'var(--bpmn-fill)',
              'stroke-width': 1.5,
              'cursor': 'crosshair',
              'class': 'bpmn-connect-handle',
            },
          },
          {
            tagName: 'path',
            selector: 'icon',
            attributes: {
              'd': 'M -3 0 L 3 0 M 0 -3 L 0 3',
              'stroke': 'var(--bpmn-fill)',
              'stroke-width': 1.5,
              'pointer-events': 'none',
            },
          },
        ],
      }),
    ],
  });
}
