const DROPDOWN = '.bpmn-toolbar__color-dropdown[popover]';

/** Positions native color popovers without requiring CSS anchor positioning support. */
export function positionToolbarColors(root: HTMLElement): () => void {
  const abort = new AbortController();

  const position = (popover: HTMLElement): void => {
    if (!popover.matches(':popover-open')) return;
    const trigger = popover.parentElement?.querySelector<HTMLButtonElement>('button[popovertarget]');
    if (trigger?.getAttribute('popovertarget') !== popover.id) return;

    const anchor = trigger.getBoundingClientRect();
    const box = popover.getBoundingClientRect();
    const padding = 16;
    const gap = 4;
    const width = document.documentElement.clientWidth;
    const height = document.documentElement.clientHeight;
    const below = anchor.bottom + gap;
    const top = below + box.height <= height - padding ? below : anchor.top - gap - box.height;
    const left = getComputedStyle(trigger).direction === 'rtl' ? anchor.right - box.width : anchor.left;
    popover.style.left = `${String(Math.max(padding, Math.min(left, width - box.width - padding)))}px`;
    popover.style.top = `${String(Math.max(padding, Math.min(top, height - box.height - padding)))}px`;
  };
  const reposition = (): void => {
    root.querySelectorAll<HTMLElement>(DROPDOWN).forEach(position);
  };

  root.addEventListener('toggle', (event) => {
    if (event.target instanceof HTMLElement && event.target.matches(DROPDOWN)) position(event.target);
  }, { capture: true, signal: abort.signal });
  window.addEventListener('resize', reposition, { signal: abort.signal });
  window.addEventListener('scroll', reposition, { capture: true, passive: true, signal: abort.signal });

  return () => { abort.abort(); };
}
