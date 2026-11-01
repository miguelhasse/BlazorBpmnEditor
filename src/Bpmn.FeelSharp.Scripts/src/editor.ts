interface Receiver {
  invokeMethodAsync<T = void>(method: string, ...args: unknown[]): Promise<T>;
}

interface Integration {
  sync(): void;
  dispose(): void;
}

export function attach(surface: HTMLElement, input: HTMLTextAreaElement, receiver: Receiver, generation: number,
    integration?: Integration) {
  const highlight = surface.querySelector<HTMLElement>('.bpmn-feelsharp-highlight');
  let sequence = 0;
  let composing = false;
  let disposed = false;
  let completionAllowed = true;
  let pendingCommit = false;
  let queue = Promise.resolve();
  const listeners = new AbortController();
  const options = { signal: listeners.signal };
  const active = () => !disposed && !input.readOnly && surface.dataset.generation === String(generation);
  const scroll = () => {
    integration?.sync();
    if (highlight) {
      highlight.scrollTop = input.scrollTop;
      highlight.scrollLeft = input.scrollLeft;
    }
  };
  const snapshot = (key?: 'commit') => {
    if (!active()) return;
    if (key === 'commit') pendingCommit = true;
    const version = ++sequence;
    const text = input.value;
    const start = input.selectionStart;
    const end = input.selectionEnd;
    const isComposing = composing;
    const root = input.getRootNode?.() as Document | ShadowRoot | undefined;
    const focused = (root?.activeElement ?? document.activeElement) === input && completionAllowed;
    queue = queue.then(async () => {
      if (!active() || version !== sequence) return;
      await receiver.invokeMethodAsync('SnapshotAsync', text, start, end, version, generation, isComposing, focused);
      if (pendingCommit && active() && version === sequence && !isComposing) {
        pendingCommit = false;
        try {
          await receiver.invokeMethodAsync('NativeKeyAsync', 'commit', version, generation);
        } catch (error) {
          pendingCommit = true;
          throw error;
        }
      }
      scroll();
    }).catch(error => {
      // A later native event can resynchronize after a transient connection failure.
      console.error('FeelSharp editor callback failed', error);
    });
  };
  input.wrap = 'off';
  input.addEventListener('scroll', scroll, options);
  input.addEventListener('input', () => { completionAllowed = true; scroll(); snapshot(); }, options);
  input.addEventListener('select', () => snapshot(), options);
  input.addEventListener('click', () => { completionAllowed = true; snapshot(); }, options);
  input.addEventListener('keyup', event => {
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) snapshot();
  }, options);
  input.addEventListener('compositionstart', () => { composing = true; snapshot(); }, options);
  input.addEventListener('compositionend', () => { composing = false; snapshot(); }, options);
  // focusout bubbles through the native input before Blazor's delegated focus handling.
  input.addEventListener('focusout', () => snapshot('commit'), options);
  input.addEventListener('keydown', event => {
    if (!active() || composing || event.isComposing) return;
    const completion = surface.dataset.completion === 'true';
    if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      snapshot('commit');
    } else if (completion && ['ArrowDown', 'ArrowUp', 'Enter', 'Tab', 'Escape'].includes(event.key)) {
      event.preventDefault();
      // Navigation uses the acknowledged snapshot, not a new snapshot that resets the selected option.
      const version = sequence;
      queue = queue.then(async () => {
        if (active() && version === sequence) {
          await receiver.invokeMethodAsync('NativeKeyAsync', event.key, version, generation);
        }
      }).catch(error => { console.error('FeelSharp editor key failed', error); });
    }
  }, options);
  const observer = new MutationObserver(scroll);
  observer.observe(surface, { childList: true, subtree: true });
  function dispose() {
    if (disposed) return;
    disposed = true;
    listeners.abort();
    observer.disconnect();
    integration?.dispose();
    surface.dataset.enhanced = 'false';
  }

  scroll();
  return {
    replace(expected: string, start: number, length: number, text: string, version: number, revision: number) {
      if (!active() || composing || revision !== generation || version !== sequence || input.value !== expected ||
          start < 0 || length < 0 || start + length > input.value.length) return false;
      input.focus();
      input.setSelectionRange(start, start + length);
      // Native insertion retains browser undo history; setRangeText covers browsers without insertText.
      if (!document.execCommand('insertText', false, text)) {
        input.setRangeText(text, start, start + length, 'end');
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      completionAllowed = false;
      snapshot();
      return true;
    },
    dispose,
  };
}

export async function attachFluent(surface: HTMLElement, host: HTMLElement, receiver: Receiver, generation: number) {
  if (!host?.localName) throw new Error('Fluent textarea element missing.');
  let timeout: ReturnType<typeof setTimeout> | undefined;
  try {
    await Promise.race([
      customElements.whenDefined(host.localName),
      new Promise<never>((_, reject) => { timeout = setTimeout(() => reject(new Error('Fluent textarea did not initialize.')), 5000); }),
    ]);
    await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
  } finally {
    clearTimeout(timeout);
  }
  const input = host.shadowRoot?.querySelector('textarea');
  const highlight = surface.querySelector<HTMLElement>('.bpmn-feelsharp-highlight');
  if (!input || !highlight) throw new Error('Fluent textarea native control unavailable.');
  if (!host.isConnected || surface.dataset.generation !== String(generation)) {
    return { dispose() {}, replace() { return false; } };
  }
  const style = document.createElement('style');
  style.textContent = `textarea.control:not([readonly]) { color: transparent !important; caret-color: var(--colorNeutralForeground1, CanvasText); }
    @media (forced-colors: active) { textarea.control:not([readonly]) { color: CanvasText !important; } }`;
  host.shadowRoot!.append(style);
  const attributes = ['aria-label', 'aria-invalid', 'aria-describedby', 'aria-autocomplete',
    'aria-controls', 'aria-expanded', 'aria-activedescendant'];
  const attribute = (name: string) => surface.hasAttribute('data-input-label')
    ? surface.getAttribute(`data-input-${name.slice(5)}`) : host.getAttribute(name);
  const sync = () => {
    const rect = input.getBoundingClientRect();
    const container = surface.getBoundingClientRect();
    const computed = getComputedStyle(input);
    Object.assign(highlight.style, {
      left: `${rect.left - container.left + (parseFloat(computed.borderLeftWidth) || 0)}px`,
      top: `${rect.top - container.top + (parseFloat(computed.borderTopWidth) || 0)}px`,
      width: `${input.clientWidth}px`, height: `${input.clientHeight}px`,
      font: computed.font, letterSpacing: computed.letterSpacing, tabSize: computed.tabSize,
      padding: computed.padding, whiteSpace: 'pre', overflowWrap: 'normal',
      color: 'var(--colorNeutralForeground1, CanvasText)',
    });
    for (const name of attributes) {
      const value = attribute(name);
      if (value === null) input.removeAttribute(name);
      else input.setAttribute(name, value);
    }
    // ID references cannot cross a shadow boundary; direct references can.
    const elements = (name: string) => (attribute(name) ?? '').split(/\s+/)
      .map(id => host.ownerDocument.getElementById(id)).filter((element): element is HTMLElement => element !== null);
    input.ariaDescribedByElements = elements('aria-describedby');
    input.ariaControlsElements = elements('aria-controls');
    input.ariaActiveDescendantElement = elements('aria-activedescendant')[0] ?? null;
  };
  const resize = new ResizeObserver(sync);
  resize.observe(input);
  const attributesObserver = new MutationObserver(sync);
  attributesObserver.observe(surface, { attributes: true, attributeFilter: attributes.map(name => `data-input-${name.slice(5)}`) });
  const shadowObserver = new MutationObserver(sync);
  shadowObserver.observe(host.shadowRoot!, { childList: true, subtree: true, characterData: true });
  return attach(surface, input, receiver, generation, {
    sync,
    dispose() { resize.disconnect(); attributesObserver.disconnect(); shadowObserver.disconnect(); style.remove(); },
  });
}
