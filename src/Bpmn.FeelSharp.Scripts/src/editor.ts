import { floatingCompletions } from './completions';

interface Receiver {
  invokeMethodAsync<T = void>(method: string, ...args: unknown[]): Promise<T>;
}

interface Integration {
  sync(): void;
  dispose(): void;
  deferBlurCommit?(): boolean;
}

const dialogFocus = new WeakMap<HTMLDialogElement, { element: HTMLElement | null; start?: number; end?: number }>();

export function focusTab(id: string) {
  document.getElementById(id)?.focus();
}

export function showDialog(dialog: HTMLDialogElement) {
  const active = dialog.ownerDocument.activeElement as HTMLElement | null;
  const input = active instanceof HTMLTextAreaElement ? active : active?.shadowRoot?.querySelector('textarea');
  dialogFocus.set(dialog, { element: input ?? active, start: input?.selectionStart, end: input?.selectionEnd });
  dialog.showModal();
}

export function closeDialog(dialog: HTMLDialogElement) {
  dialog.close();
  const focus = dialogFocus.get(dialog);
  if (focus?.element?.isConnected) {
    focus.element.focus();
    if (focus.element instanceof HTMLTextAreaElement && focus.start !== undefined && focus.end !== undefined) {
      focus.element.setSelectionRange(focus.start, focus.end);
    }
  }
  dialogFocus.delete(dialog);
}

export function attach(surface: HTMLElement, input: HTMLTextAreaElement | HTMLInputElement, receiver: Receiver, generation: number,
    integration?: Integration) {
  const highlight = surface.querySelector<HTMLElement>('.bpmn-feelsharp-highlight');
  let sequence = 0;
  let composing = false;
  let disposed = false;
  let completionAllowed = true;
  let pendingCommit = false;
  let pendingCompletion = false;
  let previousSnapshot: { text: string; start: number; end: number; composing: boolean; focused: boolean } | undefined;
  let queue = Promise.resolve();
  const listeners = new AbortController();
  const options = { signal: listeners.signal };
  const floating = floatingCompletions(surface, input);
  const active = () => !disposed && !input.readOnly && surface.dataset.generation === String(generation);
  const scroll = () => {
    integration?.sync();
    floating.sync();
    if (highlight) {
      highlight.scrollTop = input.scrollTop;
      highlight.scrollLeft = input.scrollLeft;
    }
    const list = (surface.hasAttribute?.('data-property-input') ? surface : surface.parentElement)
      ?.querySelector<HTMLElement>('[role="listbox"]');
    const selected = list?.querySelector<HTMLElement>('[aria-selected="true"]');
    if (list && selected) {
      const viewport = list.getBoundingClientRect();
      const option = selected.getBoundingClientRect();
      if (option.top < viewport.top) list.scrollTop -= viewport.top - option.top;
      else if (option.bottom > viewport.bottom) list.scrollTop += option.bottom - viewport.bottom;
    }
  };
  const snapshot = (key?: 'commit' | 'complete') => {
    if (!active()) return;
    if (key === 'commit') {
      pendingCommit = true;
      pendingCompletion = false;
    }
    if (key === 'complete') pendingCompletion = true;
    const text = input.value;
    const start = input.selectionStart ?? 0;
    const end = input.selectionEnd ?? 0;
    const isComposing = composing;
    const root = input.getRootNode?.() as Document | ShadowRoot | undefined;
    const focused = (root?.activeElement ?? document.activeElement) === input && completionAllowed;
    if (!key && !pendingCommit && !pendingCompletion && previousSnapshot?.text === text &&
        previousSnapshot.start === start && previousSnapshot.end === end &&
        previousSnapshot.composing === isComposing && previousSnapshot.focused === focused) return;
    previousSnapshot = { text, start, end, composing: isComposing, focused };
    const version = ++sequence;
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
      if (pendingCompletion && active() && version === sequence && !isComposing) {
        pendingCompletion = false;
        try {
          await receiver.invokeMethodAsync('NativeKeyAsync', 'complete', version, generation);
        } catch (error) {
          pendingCompletion = true;
          throw error;
        }
      }
      try {
        scroll();
      } catch (error) {
        dispose();
        throw error;
      }
    }).catch(error => {
      if (version === sequence) previousSnapshot = undefined;
      // A later native event can resynchronize after a transient connection failure.
      console.error('FeelSharp editor callback failed', error);
    });
  };
  if ('wrap' in input) input.wrap = 'off';
  input.addEventListener('scroll', scroll, options);
  input.addEventListener('input', () => { completionAllowed = true; scroll(); snapshot(); }, options);
  input.addEventListener('select', () => snapshot(), options);
  input.addEventListener('click', () => { completionAllowed = true; snapshot(); }, options);
  const keyboardTarget: HTMLElement = input;
  keyboardTarget.addEventListener('keyup', event => {
    if (['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) snapshot();
  }, options);
  input.addEventListener('compositionstart', () => { composing = true; snapshot(); }, options);
  input.addEventListener('compositionend', () => { composing = false; snapshot(); }, options);
  // focusout bubbles through the native input before Blazor's delegated focus handling.
  input.addEventListener('focusout', event => {
    const related = (event as FocusEvent).relatedTarget;
    const element = related && 'closest' in related ? related as Element : null;
    const root = element?.getRootNode?.() as ShadowRoot | undefined;
    const target = element?.closest('[data-property-action="manage"]') ??
      root?.host?.closest('[data-property-action="manage"]');
    // Manager owns an isolated draft. Moving directly to its toolbar action must not race a blur commit.
    snapshot(surface.hasAttribute?.('data-property-input') && target || integration?.deferBlurCommit?.() ? undefined : 'commit');
  }, options);
  keyboardTarget.addEventListener('keydown', event => {
    if (!active() || composing || event.isComposing) return;
    const completion = surface.dataset.completion === 'true';
    if (event.ctrlKey && !event.metaKey && !event.altKey && !event.shiftKey && event.code === 'Space') {
      event.preventDefault();
      completionAllowed = true;
      snapshot('complete');
    } else if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') {
      event.preventDefault();
      snapshot('commit');
    } else if (completion && ['ArrowDown', 'ArrowUp', 'Enter', 'Tab', 'Escape'].includes(event.key) &&
        !(event.key === 'Enter' && surface.dataset.acceptEnter === 'false') &&
        !(event.key === 'Tab' && surface.dataset.acceptTab === 'false')) {
      event.preventDefault();
      if (event.key === 'Escape') {
        event.stopPropagation();
        previousSnapshot = undefined;
      }
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
  observer.observe(surface.parentElement ?? surface, { childList: true, subtree: true, attributes: true,
    attributeFilter: ['aria-selected', 'aria-activedescendant', 'data-input-activedescendant',
      'data-completion', 'data-input-readonly', 'hidden'] });
  function dispose() {
    if (disposed) return;
    disposed = true;
    listeners.abort();
    observer.disconnect();
    floating.dispose();
    integration?.dispose();
    surface.dataset.enhanced = 'false';
  }

  scroll();
  return {
    read() {
      if (!active() || composing) throw new Error('Native FEEL draft is unavailable during composition or reset.');
      return { text: input.value, start: input.selectionStart, end: input.selectionEnd };
    },
    complete(revision: number) {
      if (!active() || composing || revision !== generation) return;
      input.focus();
      completionAllowed = true;
      snapshot('complete');
    },
    replace(expected: string, start: number, length: number, text: string, version: number, revision: number,
        placeholderStart?: number | null, placeholderLength = 0) {
      if (!active() || composing || revision !== generation || version !== sequence || input.value !== expected ||
          start < 0 || length < 0 || start + length > input.value.length ||
          (placeholderStart != null && (placeholderStart < 0 || placeholderLength < 0 ||
            placeholderStart + placeholderLength > text.length))) return false;
      input.focus();
      input.setSelectionRange(start, start + length);
      // Native insertion retains browser undo history; setRangeText covers browsers without insertText.
      if (!document.execCommand('insertText', false, text)) {
        input.setRangeText(text, start, start + length, 'end');
        input.dispatchEvent(new Event('input', { bubbles: true }));
      }
      completionAllowed = false;
      if (placeholderStart != null) input.setSelectionRange(start + placeholderStart, start + placeholderStart + placeholderLength);
      snapshot();
      return true;
    },
    dispose,
  };
}

// Enhance the registered property control in place. No mirror or transparent-input overlay.
export async function attachProperty(surface: HTMLElement, _: HTMLElement, receiver: Receiver, generation: number) {
  const selector = 'textarea, input[type="text"], input:not([type])';
  let input = surface.querySelector<HTMLTextAreaElement | HTMLInputElement>(selector);
  let host: HTMLElement | undefined;
  if (!input) {
    host = Array.from(surface.querySelectorAll<HTMLElement>('*'))
      .find(element => element.localName.includes('-') && element.localName.includes('text') && !element.localName.includes('label'));
    if (host && !host.shadowRoot?.querySelector(selector)) {
      let timeout: ReturnType<typeof setTimeout> | undefined;
      try {
        await Promise.race([
          customElements.whenDefined(host.localName),
          new Promise<never>((_, reject) => {
            timeout = setTimeout(() => reject(new Error('Property text control did not initialize.')), 5000);
          }),
        ]);
        await new Promise<void>(resolve => requestAnimationFrame(() => requestAnimationFrame(() => resolve())));
      } finally {
        clearTimeout(timeout);
      }
    }
    input = host?.shadowRoot?.querySelector<HTMLTextAreaElement | HTMLInputElement>(selector) ?? null;
  }
  if (!input) throw new Error('Registered FEEL property renderer must contain a text input or textarea.');
  const control = input;
  const ownerDocument = surface.ownerDocument ?? control.ownerDocument;
  let managerTransfer = false;
  const pointerDown = (event: Event) => {
    managerTransfer = event.composedPath().some(target => 'getAttribute' in target &&
      typeof target.getAttribute === 'function' && target.getAttribute('data-property-action') === 'manage');
  };
  const pointerComplete = () => { managerTransfer = false; };
  // Fluent shadow inputs can report a null relatedTarget when a toolbar custom element receives a pointer.
  ownerDocument?.addEventListener?.('pointerdown', pointerDown, true);
  ownerDocument?.addEventListener?.('click', pointerComplete, true);
  const original = new Map<string, string | null>();
  const attributes = ['aria-invalid', 'aria-describedby', 'aria-autocomplete', 'aria-controls',
    'aria-expanded', 'aria-activedescendant'];
  for (const name of attributes) original.set(name, control.getAttribute(name));
  const originalReadOnly = control.readOnly;
  const sync = () => {
    control.readOnly = surface.dataset.inputReadonly === 'true';
    for (const name of attributes) {
      const configured = surface.getAttribute(`data-input-${name.slice(5)}`);
      const value = name === 'aria-autocomplete' ? 'list' : configured ?? original.get(name) ?? null;
      if (control.getAttribute(name) !== value) {
        if (value === null) control.removeAttribute(name);
        else control.setAttribute(name, value);
      }
    }
    if (host) {
      // Fluent resynchronizes value from this attribute when modal focus changes other host attributes.
      if (host.getAttribute('value') !== control.value) host.setAttribute('value', control.value);
      const elements = (name: string) => (control.getAttribute(name) ?? '').split(/\s+/)
        .map(id => surface.ownerDocument.getElementById(id)).filter((element): element is HTMLElement => element !== null);
      if (['ariaDescribedByElements', 'ariaControlsElements', 'ariaActiveDescendantElement'].every(name => Reflect.has(control, name))) {
        control.ariaDescribedByElements = elements('aria-describedby');
        control.ariaControlsElements = elements('aria-controls');
        control.ariaActiveDescendantElement = elements('aria-activedescendant')[0] ?? null;
      } else {
        control.setAttribute('aria-description', elements('aria-describedby').map(element => element.textContent).join(' '));
        for (const name of ['aria-describedby', 'aria-controls', 'aria-activedescendant']) control.removeAttribute(name);
      }
    }
  };
  const observer = new MutationObserver(sync);
  observer.observe(surface, { attributes: true });
  const activate = () => {
    receiver.invokeMethodAsync('ActivatePropertyAsync').catch(error => console.error('FEEL property activation failed', error));
  };
  control.addEventListener('focusin', activate);
  const forwarding: Receiver = {
    invokeMethodAsync<T>(method: string, ...args: unknown[]) {
      return receiver.invokeMethodAsync<T>(method === 'SnapshotAsync' ? 'PropertySnapshotAsync' :
        method === 'NativeKeyAsync' ? 'PropertyKeyAsync' : method, ...args);
    },
  };
  const bridge = attach(surface, control, forwarding, generation, {
    sync,
    deferBlurCommit: () => managerTransfer,
    dispose() {
      ownerDocument?.removeEventListener?.('pointerdown', pointerDown, true);
      ownerDocument?.removeEventListener?.('click', pointerComplete, true);
      observer.disconnect();
      control.removeEventListener('focusin', activate);
      control.readOnly = originalReadOnly;
      for (const [name, value] of original) {
        if (value === null) control.removeAttribute(name);
        else control.setAttribute(name, value);
      }
    },
  });
  const prefix = () => surface.dataset.prefixInput === 'true' && control.value.startsWith('=') ? 1 : 0;
  return {
    ...bridge,
    read() {
      const draft = bridge.read();
      const offset = prefix();
      return { text: draft.text.slice(offset), start: Math.max(0, (draft.start ?? 0) - offset),
        end: Math.max(0, (draft.end ?? 0) - offset) };
    },
    replace(expected: string, start: number, length: number, text: string, version: number, revision: number,
        placeholderStart?: number | null, placeholderLength = 0) {
      const offset = prefix();
      return bridge.replace((offset ? '=' : '') + expected, start + offset, length, text, version, revision,
        placeholderStart, placeholderLength);
    },
  };
}

export async function attachFluent(surface: HTMLElement, host: HTMLElement, receiver: Receiver, generation: number) {
  if (!host?.localName) throw new Error('Fluent textarea element missing.');
  if (!host.shadowRoot?.querySelector('textarea')) {
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
  }
  const input = host.shadowRoot?.querySelector('textarea');
  const highlight = surface.querySelector<HTMLElement>('.bpmn-feelsharp-highlight');
  if (!input || !highlight) throw new Error('Fluent textarea native control unavailable.');
  if (!host.isConnected || surface.dataset.generation !== String(generation)) {
    return { dispose() {}, complete() {}, replace() { return false; } };
  }
  const style = document.createElement('style');
  style.textContent = `textarea.control { padding: var(--bpmn-feel-padding, 8px); }
    textarea.control:not([readonly]) { color: transparent !important; caret-color: var(--colorNeutralForeground1, CanvasText); }
    @media (forced-colors: active) { textarea.control:not([readonly]) { color: CanvasText !important; } }`;
  host.shadowRoot!.append(style);
  const attributes = ['aria-label', 'aria-invalid', 'aria-describedby', 'aria-autocomplete',
    'aria-controls', 'aria-expanded', 'aria-activedescendant'];
  const attribute = (name: string) => surface.hasAttribute('data-input-label')
    ? surface.getAttribute(`data-input-${name.slice(5)}`) : host.getAttribute(name);
  const view = host.ownerDocument.defaultView;
  let geometryFrame: number | undefined;
  let aligned = false;
  const align = () => {
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
  };
  const sync = () => {
    if (surface.hasAttribute('data-input-readonly')) {
      input.readOnly = surface.dataset.inputReadonly === 'true';
    }
    if (!aligned || !view?.requestAnimationFrame) {
      aligned = true;
      align();
    } else if (geometryFrame === undefined) {
      geometryFrame = view.requestAnimationFrame(() => { geometryFrame = undefined; align(); });
    }
    for (const name of attributes) {
      const value = attribute(name);
      if (input.getAttribute(name) !== value) {
        if (value === null) input.removeAttribute(name);
        else input.setAttribute(name, value);
      }
    }
    // ID references cannot cross a shadow boundary; direct references can.
    const elements = (name: string) => (attribute(name) ?? '').split(/\s+/)
      .map(id => host.ownerDocument.getElementById(id)).filter((element): element is HTMLElement => element !== null);
    if (['ariaDescribedByElements', 'ariaControlsElements', 'ariaActiveDescendantElement'].every(name => Reflect.has(input, name))) {
      input.ariaDescribedByElements = elements('aria-describedby');
      input.ariaControlsElements = elements('aria-controls');
      input.ariaActiveDescendantElement = elements('aria-activedescendant')[0] ?? null;
    } else {
      // Older browsers cannot resolve light-DOM IDs from the shadow input.
      for (const name of ['aria-describedby', 'aria-controls', 'aria-activedescendant']) input.removeAttribute(name);
      input.setAttribute('aria-description', elements('aria-describedby').map(element => element.textContent).join(' '));
    }
  };
  const resize = new ResizeObserver(sync);
  resize.observe(input);
  const attributesObserver = new MutationObserver(sync);
  attributesObserver.observe(surface, { attributes: true,
    attributeFilter: [...attributes.map(name => `data-input-${name.slice(5)}`), 'data-input-readonly'] });
  const shadowObserver = new MutationObserver(sync);
  shadowObserver.observe(host.shadowRoot!, { childList: true, subtree: true, characterData: true });
  return attach(surface, input, receiver, generation, {
    sync,
    dispose() {
      if (geometryFrame !== undefined) view?.cancelAnimationFrame(geometryFrame);
      resize.disconnect();
      attributesObserver.disconnect();
      shadowObserver.disconnect();
      style.remove();
    },
  });
}
