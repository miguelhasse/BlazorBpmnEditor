/** Keeps Blazor-owned suggestions in the top layer without moving their DOM or input focus. */
export function floatingCompletions(surface: HTMLElement, input: HTMLElement) {
  if (typeof surface.showPopover !== 'function') {
    throw new Error('FEEL completions require browser Popover API support.');
  }
  let popup: HTMLElement | null = null;
  const owner = surface.ownerDocument;
  const view = owner?.defaultView;
  const listeners = new AbortController();
  const options = { signal: listeners.signal };
  const hide = () => {
    if (popup?.matches(':popover-open')) popup.hidePopover();
  };
  const sync = () => {
    const next = (surface.hasAttribute?.('data-property-input') ? surface : surface.parentElement)
      ?.querySelector<HTMLElement>('[data-feel-completions]') ?? null;
    if (popup !== next) {
      hide();
      popup = next;
    }
    if (!popup) return;
    if (typeof popup.showPopover !== 'function') {
      throw new Error('FEEL completions require browser Popover API support.');
    }
    const anchor = input.getBoundingClientRect();
    const viewport = view?.visualViewport;
    const leftEdge = viewport?.offsetLeft ?? 0;
    const topEdge = viewport?.offsetTop ?? 0;
    const width = viewport?.width ?? owner.documentElement.clientWidth;
    const height = viewport?.height ?? owner.documentElement.clientHeight;
    const rightEdge = leftEdge + width;
    const bottomEdge = topEdge + height;
    if (surface.dataset.completion !== 'true' || surface.dataset.inputReadonly === 'true' ||
        surface.closest('[hidden]') || input.getClientRects().length === 0 ||
        anchor.bottom <= topEdge || anchor.top >= bottomEdge || anchor.right <= leftEdge || anchor.left >= rightEdge) {
      hide();
      return;
    }
    const padding = 8;
    const gap = 4;
    const below = Math.max(0, bottomEdge - padding - anchor.bottom - gap);
    const above = Math.max(0, anchor.top - topEdge - gap - padding);
    const available = Math.max(above, below);
    popup.style.width = `${Math.max(0, Math.min(Math.max(anchor.width, 240), width - padding * 2))}px`;
    popup.style.maxHeight = `${Math.min(256, available)}px`;
    if (!popup.matches(':popover-open')) popup.showPopover();
    const box = popup.getBoundingClientRect();
    const top = box.height <= below || below >= above ? anchor.bottom + gap : anchor.top - gap - box.height;
    const left = view?.getComputedStyle(input).direction === 'rtl' ? anchor.right - box.width : anchor.left;
    popup.style.left = `${Math.max(leftEdge + padding, Math.min(left, rightEdge - box.width - padding))}px`;
    popup.style.top = `${Math.max(topEdge + padding, Math.min(top, bottomEdge - box.height - padding))}px`;
  };
  view?.addEventListener('resize', sync, options);
  view?.addEventListener('scroll', sync, { ...options, capture: true, passive: true });
  view?.visualViewport?.addEventListener('resize', sync, options);
  view?.visualViewport?.addEventListener('scroll', sync, options);
  const observer = new ResizeObserver(sync);
  observer.observe(input);
  return {
    sync,
    dispose() {
      hide();
      listeners.abort();
      observer.disconnect();
    },
  };
}
