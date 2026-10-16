/**
 * @vitest-environment jsdom
 *
 * A real DOM, because every clause of this predicate is a statement about where an event came from.
 */
import { dia } from '@joint/core';
import { beforeEach, describe, expect, it } from 'vitest';

import { collapseTargetOf } from '../../src/editing/expand-target.js';
import { PROVISIONAL_FLAG } from '../../src/editing/provisional.js';

/**
 * What a double-click on the canvas means (specification section 1.2.1).
 *
 * Two gestures share the event — collapsing an expanded Sub-Process, and editing a label — so the
 * question is answered once and both controllers ask it. That makes this the single place the
 * exclusion list in section 1.2.1 line 97 is enforced, and each of its cases is reachable for a
 * different reason:
 *
 * - a nested child has a view of its own and JointJS reports the deepest one, so the test is
 *   really "is the cell I was handed an expanded Sub-Process";
 * - a resize or connect handle lives in the tools layer, outside the cell's element, but carries
 *   the cell's `model-id`, so only `view.el.contains` tells it from the shape;
 * - the markers, the type glyph and the label are inside the element and need the selector test.
 */
let element: dia.Element;
let view: { model: dia.Cell; el: HTMLElement };

/** A stand-in for the cell's rendered element, with the nodes the real markup carries. */
function render(): HTMLElement {
  const root = document.createElement('div');

  for (const selector of ['body', 'innerBorder', 'typeIcon', 'marker0', 'label']) {
    const node = document.createElement('div');
    node.setAttribute('joint-selector', selector);
    root.append(node);
  }

  document.body.append(root);
  return root;
}

const at = (node: Element | null): dia.Event => ({ target: node }) as unknown as dia.Event;

const nodeFor = (selector: string): Element => {
  const node = view.el.querySelector(`[joint-selector="${selector}"]`);
  if (node === null) throw new Error(`no ${selector}`);
  return node;
};

beforeEach(() => {
  document.body.replaceChildren();

  element = new dia.Element({ id: 'Sub_1', bpmn: { isExpanded: true } });
  view = { model: element, el: render() };
});

const ask = (node: Element | null): string | null =>
  collapseTargetOf(view as unknown as dia.CellView, at(node));

describe('an expanded Sub-Process', () => {
  it('collapses on a double-click on its body', () => {
    expect(ask(nodeFor('body'))).toBe('Sub_1');
  });

  it('and on its inner border, which is a Transaction shape', () => {
    expect(ask(nodeFor('innerBorder'))).toBe('Sub_1');
  });

  it('and on the element itself, which is the border between the named nodes', () => {
    expect(ask(view.el)).toBe('Sub_1');
  });
});

describe('but not', () => {
  it('on a collapsed one', () => {
    element.set('bpmn', { isExpanded: false });

    expect(ask(nodeFor('body'))).toBeNull();
  });

  it('on a Sub-Process whose shape says nothing', () => {
    element.set('bpmn', { isExpanded: null });

    expect(ask(nodeFor('body'))).toBeNull();
  });

  it('on a Task, however large', () => {
    element.set('bpmn', { elementType: 'task', isExpanded: null });

    expect(ask(nodeFor('body'))).toBeNull();
  });

  it.each(['typeIcon', 'marker0', 'label'])('on its own %s', (selector) => {
    expect(ask(nodeFor(selector))).toBeNull();
  });

  it('on a node the markup gains later', () => {
    // An allow-list, not a deny-list: a decoration added to the markup after this was written is
    // excluded by default rather than silently becoming a collapse target.
    const added = document.createElement('div');
    added.setAttribute('joint-selector', 'phaseOverlay');
    view.el.append(added);

    expect(ask(added)).toBeNull();
  });

  it('on a tool that carries this cell id but lives outside its element', () => {
    /*
     * The case that is a live defect without the `contains` test. JointJS mounts resize and
     * connect handles in the paper's tools layer and stamps them with the related cell's
     * `model-id`, so `findView` resolves a double-click on a resize handle to this very cell.
     */
    const handle = document.createElement('div');
    handle.setAttribute('joint-selector', 'body');
    handle.setAttribute('model-id', 'Sub_1');
    document.body.append(handle);

    expect(ask(handle)).toBeNull();
  });

  it('on a Blazor overlay drawn over the canvas', () => {
    const editor = document.createElement('textarea');
    document.body.append(editor);

    expect(ask(editor)).toBeNull();
  });

  it('on the provisional connector', () => {
    element.set(PROVISIONAL_FLAG, true);

    expect(ask(nodeFor('body'))).toBeNull();
  });

  it('on a link', () => {
    const link = new dia.Link({ id: 'Flow_1', bpmn: { isExpanded: true } });
    const asked = collapseTargetOf(
      { model: link, el: view.el } as unknown as dia.CellView,
      at(nodeFor('body')),
    );

    expect(asked).toBeNull();
  });

  it('when there is no target at all', () => {
    expect(ask(null)).toBeNull();
  });
});
