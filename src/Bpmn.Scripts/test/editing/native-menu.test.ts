/**
 * @vitest-environment jsdom
 */
import { afterEach, describe, expect, it } from 'vitest';

import { suppressNativeMenu } from '../../src/editing/native-menu.js';

/**
 * The editor is an application surface: the browser's own menu and native drags have no place on
 * it, except in a field made for typing (cut and paste) and for the palette's own drag.
 */
describe('suppressNativeMenu', () => {
  let root: HTMLElement;
  let release: () => void = () => undefined;

  function setup(): void {
    root = document.createElement('div');
    root.innerHTML = '<div id="chrome"></div><input id="field"><div class="bpmn-stencil__tool"><span id="tool"></span></div>';
    document.body.appendChild(root);
    release = suppressNativeMenu(root);
  }

  function fire(type: string, selector: string): Event {
    const event = new Event(type, { bubbles: true, cancelable: true });
    root.querySelector(selector)?.dispatchEvent(event);
    return event;
  }

  afterEach(() => {
    release();
    root.remove();
  });

  it('suppresses the browser menu on the chrome', () => {
    setup();
    expect(fire('contextmenu', '#chrome').defaultPrevented).toBe(true);
  });

  it('leaves the menu in an editable field', () => {
    setup();
    expect(fire('contextmenu', '#field').defaultPrevented).toBe(false);
  });

  it('cancels native drags except a stencil tool\'s own', () => {
    setup();
    expect(fire('dragstart', '#chrome').defaultPrevented).toBe(true);
    expect(fire('dragstart', '#tool').defaultPrevented).toBe(false);
  });

  it('stops suppressing once released', () => {
    setup();
    release();
    expect(fire('contextmenu', '#chrome').defaultPrevented).toBe(false);
  });
});
