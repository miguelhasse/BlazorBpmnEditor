/** @vitest-environment jsdom */
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { positionToolbarColors } from '../../src/editing/toolbar-colors.js';

describe('positionToolbarColors', () => {
  let root: HTMLElement;
  let popover: HTMLElement;
  let trigger: HTMLButtonElement;
  let release: () => void;
  let opened: boolean;

  beforeEach(() => {
    root = document.createElement('div');
    root.innerHTML = '<div><button popovertarget="colors"></button><div id="colors" class="bpmn-toolbar__color-dropdown" popover="auto"></div></div>';
    document.body.appendChild(root);
    popover = root.querySelector<HTMLElement>('#colors')!;
    trigger = root.querySelector<HTMLButtonElement>('button')!;
    opened = true;
    const matches = popover.matches.bind(popover);
    vi.spyOn(popover, 'matches').mockImplementation(selector => selector === ':popover-open' ? opened : matches(selector));
    vi.spyOn(document.documentElement, 'clientWidth', 'get').mockReturnValue(480);
    vi.spyOn(document.documentElement, 'clientHeight', 'get').mockReturnValue(800);
    vi.spyOn(trigger, 'getBoundingClientRect').mockReturnValue(new DOMRect(100, 100, 32, 32));
    vi.spyOn(popover, 'getBoundingClientRect').mockReturnValue(new DOMRect(0, 0, 200, 80));
    release = positionToolbarColors(root);
  });

  afterEach(() => {
    release();
    root.remove();
    vi.restoreAllMocks();
  });

  it('anchors below the trigger on native toggle', () => {
    popover.dispatchEvent(new Event('toggle'));
    expect(popover.style.left).toBe('100px');
    expect(popover.style.top).toBe('136px');
  });

  it('clamps at the right edge and flips above near the bottom', () => {
    vi.spyOn(trigger, 'getBoundingClientRect').mockReturnValue(new DOMRect(440, 750, 32, 32));
    popover.dispatchEvent(new Event('toggle'));
    expect(popover.style.left).toBe('264px');
    expect(popover.style.top).toBe('666px');
  });

  it('aligns right edges for RTL controls', () => {
    trigger.style.direction = 'rtl';
    vi.spyOn(trigger, 'getBoundingClientRect').mockReturnValue(new DOMRect(300, 100, 32, 32));
    popover.dispatchEvent(new Event('toggle'));
    expect(popover.style.left).toBe('132px');
  });

  it('tracks viewport and scroll changes without touching closed popovers', () => {
    window.dispatchEvent(new Event('resize'));
    expect(popover.style.left).toBe('100px');
    vi.spyOn(trigger, 'getBoundingClientRect').mockReturnValue(new DOMRect(150, 200, 32, 32));
    root.dispatchEvent(new Event('scroll'));
    expect(popover.style.left).toBe('150px');
    expect(popover.style.top).toBe('236px');
    opened = false;
    popover.style.left = '';
    window.dispatchEvent(new Event('resize'));
    expect(popover.style.left).toBe('');
  });

  it('removes listeners on disposal', () => {
    release();
    popover.dispatchEvent(new Event('toggle'));
    window.dispatchEvent(new Event('resize'));
    root.dispatchEvent(new Event('scroll'));
    expect(popover.style.left).toBe('');
  });
});
