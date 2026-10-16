import { describe, expect, it } from 'vitest';
import { dia } from '@joint/core';

import {
  BpmnActivityElement,
  BpmnConnectorLink,
  BpmnDataElement,
  BpmnEventElement,
  BpmnGatewayElement,
  BpmnGroupElement,
  BpmnSwimlaneElement,
  BpmnTextAnnotationElement,
  outlineSelectorOf,
} from '../../src/shapes/elements.js';

/**
 * Which node the selection highlight traces.
 *
 * The highlighter was pointed at the view's `root`, and a group's geometry is the box around
 * everything in it — the label sitting outside an Event, the type icon and bottom markers of a
 * Task. Every selection therefore came out as a rectangle noticeably larger than the shape: a
 * 36px Start Event was highlighted by an 86x105 box.
 *
 * Each family names its own silhouette, and none of those names is shared, so this is a table that
 * has to be right rather than a rule that can be derived. It is asserted against the real cells so
 * a renamed markup selector fails here instead of quietly falling back to `root`.
 */
describe('outlineSelectorOf', () => {
  const cases: readonly (readonly [string, dia.Cell, string])[] = [
    ['Event', new BpmnEventElement(), 'outerRing'],
    ['Activity', new BpmnActivityElement(), 'body'],
    ['Gateway', new BpmnGatewayElement(), 'body'],
    ['Swimlane', new BpmnSwimlaneElement(), 'body'],
    ['Data', new BpmnDataElement(), 'body'],
    ['Group', new BpmnGroupElement(), 'body'],
    ['Text Annotation', new BpmnTextAnnotationElement(), 'bracket'],
    ['Connector', new BpmnConnectorLink(), 'line'],
  ];

  it.each(cases)('traces the %s outline', (_name, cell, expected) => {
    expect(outlineSelectorOf(cell)).toBe(expected);
  });

  it('names a node the cell actually declares', () => {
    // The failure this guards against is silent: a selector that no longer exists makes the
    // highlighter fall back to the whole view, which is the bounding box again.
    for (const [name, cell, expected] of cases) {
      // On the prototype, not in the attributes: `dia.Element.define` puts markup in protoProps.
      const markup = (cell as { markup?: readonly { readonly selector?: string }[] }).markup;
      const selectors = (markup ?? []).map((node) => node.selector);

      expect(selectors, `${name} markup`).toContain(expected);
    }
  });

  it('falls back to the whole view for a cell from elsewhere', () => {
    // A highlight around the wrong box beats no highlight at all for a cell this module did not
    // build — a host's own shape, or a JointJS default.
    expect(outlineSelectorOf(new dia.Element())).toBe('root');
  });
});
