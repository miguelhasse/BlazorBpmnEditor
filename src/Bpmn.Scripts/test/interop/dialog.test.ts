// @vitest-environment jsdom
import { afterEach, describe, expect, it, vi } from 'vitest';
import { closeDialog, showDialog } from '../../src/interop/dialog.js';

function setup(dismissOnBackdrop = false): {
  element: HTMLDialogElement;
  owner: { invokeMethodAsync: ReturnType<typeof vi.fn<() => Promise<void>>> };
  show: ReturnType<typeof vi.fn<() => void>>;
} {
  const element = document.createElement('dialog');
  document.body.append(element);
  const show = vi.fn(() => { element.open = true; });
  element.showModal = show;
  element.close = vi.fn(() => { element.open = false; });
  element.getBoundingClientRect = () => new DOMRect(10, 10, 100, 100);
  const owner = { invokeMethodAsync: vi.fn(() => Promise.resolve()) };
  showDialog(element, owner, dismissOnBackdrop);
  return { element, owner, show };
}

afterEach(() => { document.body.replaceChildren(); });

describe('native BPMN modal lifecycle', () => {
  it('allows cleanup after Blazor has removed the host', () => {
    expect(() => { closeDialog(null); }).not.toThrow();
  });

  it('opens once and releases before reporting Escape dismissal once', () => {
    const { element, owner, show } = setup();
    showDialog(element, owner, false);
    expect(show).toHaveBeenCalledTimes(1);
    const cancel = new Event('cancel', { cancelable: true });
    element.dispatchEvent(cancel);
    element.dispatchEvent(new Event('cancel'));
    element.dispatchEvent(new Event('close'));
    expect(cancel.defaultPrevented).toBe(true);
    expect(element.open).toBe(false);
    expect(owner.invokeMethodAsync).toHaveBeenCalledExactlyOnceWith('DismissAsync');
  });

  it('programmatic close and stale events do not dismiss a reopened dialog', () => {
    const { element, owner } = setup();
    closeDialog(element);
    element.dispatchEvent(new Event('close'));
    expect(owner.invokeMethodAsync).not.toHaveBeenCalled();
    showDialog(element, owner, false);
    element.dispatchEvent(new Event('close'));
    expect(element.open).toBe(true);
    expect(owner.invokeMethodAsync).not.toHaveBeenCalled();
  });

  it('reports external native closure', () => {
    const { element, owner } = setup();
    element.close();
    element.dispatchEvent(new Event('close'));
    expect(owner.invokeMethodAsync).toHaveBeenCalledOnce();
  });

  it('does not handle ordinary keys as cancellation', () => {
    const { element, owner } = setup();
    element.dispatchEvent(new KeyboardEvent('keydown', { key: 'a' }));
    expect(element.open).toBe(true);
    expect(owner.invokeMethodAsync).not.toHaveBeenCalled();
  });

  it.each([false, true])('preserves backdrop dismissal policy %s', (enabled) => {
    const { element, owner } = setup(enabled);
    element.dispatchEvent(new MouseEvent('pointerdown', { clientX: 0, clientY: 0 }));
    element.dispatchEvent(new MouseEvent('click', { clientX: 0, clientY: 0 }));
    expect(owner.invokeMethodAsync).toHaveBeenCalledTimes(enabled ? 1 : 0);
  });

  it('does not dismiss on padding clicks or a drag beginning inside', () => {
    const { element, owner } = setup(true);
    element.dispatchEvent(new MouseEvent('pointerdown', { clientX: 20, clientY: 20 }));
    element.dispatchEvent(new MouseEvent('click', { clientX: 20, clientY: 20 }));
    element.dispatchEvent(new MouseEvent('click', { clientX: 0, clientY: 0 }));
    expect(owner.invokeMethodAsync).not.toHaveBeenCalled();
  });
});
