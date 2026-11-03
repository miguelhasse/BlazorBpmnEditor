import assert from 'node:assert/strict';
import { test } from 'node:test';
import { attach, attachFluent, attachProperty } from '../dist/feelsharp-editor.js';

const settle = () => new Promise(resolve => setTimeout(resolve, 10));

async function setupProperty(prefix = false, fluent = false) {
  const state = fluent ? setupFluent() : setup();
  state.bridge.dispose();
  const { surface, input, calls } = state;
  const attributes = state.nativeAttributes ?? new Map();
  input.getAttribute = name => attributes.get(name) ?? null;
  input.setAttribute = (name, value) => attributes.set(name, value);
  input.removeAttribute = name => attributes.delete(name);
  surface.dataset.prefixInput = String(prefix);
  surface.dataset.inputReadonly = 'false';
  surface.getAttribute = () => null;
  surface.hasAttribute = name => name === 'data-property-input';
  surface.querySelector = selector => selector === '[data-feel-completions]' || selector === '[role="listbox"]'
    ? null : fluent ? null : input;
  surface.querySelectorAll = () => fluent ? [state.host] : [];
  surface.ownerDocument = state.host?.ownerDocument ?? new EventTarget();
  surface.dataset.enhanced = 'true';
  const bridge = await attachProperty(surface, null, {
    async invokeMethodAsync(method, ...args) { calls.push({ method, args }); },
  }, 1);
  return { ...state, bridge, attributes };
}

test('property adapter uses original input and raw snapshots without an overlay', async () => {
  const { bridge, input, calls, surface } = await setupProperty();
  input.dispatchEvent(new Event('input'));
  await settle();
  assert.equal(calls[0].method, 'PropertySnapshotAsync');
  assert.equal(calls[0].args[0], 'cus');
  assert.deepEqual(bridge.read(), { text: 'cus', start: 3, end: 3 });
  assert.equal(surface.dataset.enhanced, 'true');
  bridge.dispose();
});

test('prefix property adapter maps UTF16 replacement and selection without duplicating marker', async () => {
  const { bridge, input, calls } = await setupProperty(true);
  input.value = '=cus';
  input.selectionStart = input.selectionEnd = 4;
  input.dispatchEvent(new Event('input'));
  await settle();
  assert.deepEqual(bridge.read(), { text: 'cus', start: 3, end: 3 });
  assert.equal(bridge.replace('cus', 0, 3, 'customer😀', calls[0].args[3], 1), true);
  await settle();
  assert.equal(input.value, '=customer😀');
  assert.deepEqual(bridge.read(), { text: 'customer😀', start: 10, end: 10 });
  bridge.dispose();
});

test('Fluent property adapter leaves native text visible and forwards focus and commits', async () => {
  const { bridge, input, calls, style } = await setupProperty(false, true);
  assert.equal(style.textContent, undefined);
  input.dispatchEvent(new Event('focusin'));
  input.dispatchEvent(new Event('focusout'));
  await settle();
  assert.equal(calls[0].method, 'ActivatePropertyAsync');
  assert.ok(calls.some(call => call.method === 'PropertyKeyAsync' && call.args[0] === 'commit'));
  bridge.dispose();
});

test('Fluent property draft survives host attribute resynchronization during modal focus changes', async () => {
  const { bridge, input, host, calls } = await setupProperty(false, true);
  input.value = '77';
  input.selectionStart = input.selectionEnd = 2;
  input.dispatchEvent(new Event('input'));
  await settle();
  assert.equal(host.getAttribute('value'), '77');
  input.value = host.getAttribute('value');
  assert.equal(bridge.read().text, '77');
  assert.equal(bridge.replace('77', 0, 2, 'amount', calls.at(-1).args[3], 1), true);
  await settle();
  assert.equal(host.getAttribute('value'), 'amount');
  bridge.dispose();
});

test('moving property focus directly to manager toolbar defers persistence to isolated manager', async () => {
  const { bridge, input, calls } = await setupProperty();
  const event = new Event('focusout');
  Object.assign(event, { relatedTarget: { closest() { return this; } } });
  input.dispatchEvent(event);
  await settle();
  assert.ok(calls.some(call => call.method === 'PropertySnapshotAsync'));
  assert.ok(!calls.some(call => call.method === 'PropertyKeyAsync' && call.args[0] === 'commit'));
  bridge.dispose();
});

test('toolbar pointer transfer guards null shadow blur target without suppressing later normal blur', async () => {
  const { bridge, input, calls, surface } = await setupProperty();
  const pointer = new Event('pointerdown');
  pointer.composedPath = () => [{ getAttribute() { return 'manage'; } }];
  surface.ownerDocument.dispatchEvent(pointer);
  input.dispatchEvent(new Event('focusout'));
  await settle();
  assert.ok(!calls.some(call => call.method === 'PropertyKeyAsync'));
  surface.ownerDocument.dispatchEvent(new Event('click'));
  input.dispatchEvent(new Event('focusout'));
  await settle();
  assert.ok(calls.some(call => call.method === 'PropertyKeyAsync' && call.args[0] === 'commit'));
  bridge.dispose();
});

function setupFloating({ x = 20, y = 100, rtl = false, property = true, height = 100 } = {}) {
  const state = setup();
  state.bridge.dispose();
  const view = new EventTarget();
  view.getComputedStyle = () => ({ direction: rtl ? 'rtl' : 'ltr' });
  view.visualViewport = new EventTarget();
  const owner = { defaultView: view, documentElement: { clientWidth: 400, clientHeight: 300 } };
  const anchor = { left: x, right: x + 120, top: y, bottom: y + 30, width: 120 };
  const popup = {
    style: {}, opened: false, shows: 0, hides: 0,
    matches() { return this.opened; },
    showPopover() { this.opened = true; this.shows++; },
    hidePopover() { this.opened = false; this.hides++; },
    getBoundingClientRect() { return { width: parseFloat(this.style.width), height: Math.min(height, parseFloat(this.style.maxHeight)) }; },
  };
  state.surface.ownerDocument = owner;
  state.surface.hasAttribute = () => property;
  state.surface.closest = () => state.surface.hidden ? {} : null;
  state.surface.querySelector = selector => selector === '[data-feel-completions]' ? popup : null;
  state.surface.parentElement = { querySelector: state.surface.querySelector };
  state.surface.dataset.completion = 'true';
  state.input.getBoundingClientRect = () => anchor;
  state.input.getClientRects = () => [anchor];
  const bridge = attach(state.surface, state.input, { async invokeMethodAsync() {} }, 1);
  return { ...state, bridge, popup, view, anchor };
}

test('all completion presentations use top layer without moving focus and flip within viewport', () => {
  for (const property of [true, false]) {
    const { bridge, popup, input } = setupFloating({ x: 330, y: 240, property });
    assert.equal(popup.opened, true);
    assert.equal(popup.style.left, '152px');
    assert.equal(popup.style.top, '136px');
    assert.equal(globalThis.document.activeElement, input);
    bridge.dispose();
    assert.equal(popup.opened, false);
  }
});

test('completion positioning handles RTL, narrow available height and scroll or resize changes', () => {
  const { bridge, popup, view, anchor } = setupFloating({ x: 200, y: 140, rtl: true, height: 256 });
  assert.equal(popup.style.left, '80px');
  assert.equal(popup.style.maxHeight, '128px');
  assert.equal(popup.style.top, '8px');
  anchor.top = 20;
  anchor.bottom = 50;
  anchor.left = 10;
  anchor.right = 130;
  view.dispatchEvent(new Event('scroll'));
  assert.equal(popup.style.left, '8px');
  assert.equal(popup.style.top, '54px');
  view.visualViewport.dispatchEvent(new Event('resize'));
  assert.equal(popup.shows, 1);
  bridge.dispose();
  anchor.bottom = 100;
  view.dispatchEvent(new Event('resize'));
  assert.equal(popup.style.top, '54px');
});

test('completion popup hides for tab changes, readonly and missing suggestions', () => {
  const { bridge, popup, view, surface } = setupFloating();
  surface.hidden = true;
  view.dispatchEvent(new Event('resize'));
  assert.equal(popup.opened, false);
  surface.hidden = false;
  view.dispatchEvent(new Event('resize'));
  assert.equal(popup.opened, true);
  surface.dataset.inputReadonly = 'true';
  view.dispatchEvent(new Event('resize'));
  assert.equal(popup.opened, false);
  surface.dataset.inputReadonly = 'false';
  surface.dataset.completion = 'false';
  view.dispatchEvent(new Event('resize'));
  assert.equal(popup.opened, false);
  bridge.dispose();
});

test('popup geometry coalesces events, reopens after offscreen scroll and cancels disposal work', () => {
  const { bridge, popup, view, anchor, input } = setupFloating();
  const frames = new Map();
  let next = 0;
  let reads = 0;
  view.requestAnimationFrame = callback => { frames.set(++next, callback); return next; };
  view.cancelAnimationFrame = id => frames.delete(id);
  input.getBoundingClientRect = () => { reads++; return anchor; };
  const flush = () => {
    const callbacks = [...frames.values()];
    frames.clear();
    callbacks.forEach(callback => callback());
  };
  anchor.top = 400;
  anchor.bottom = 430;
  view.dispatchEvent(new Event('scroll'));
  view.dispatchEvent(new Event('resize'));
  view.visualViewport.dispatchEvent(new Event('scroll'));
  assert.equal(frames.size, 1);
  assert.equal(reads, 0);
  flush();
  assert.equal(reads, 1);
  assert.equal(popup.opened, false);
  anchor.top = 100;
  anchor.bottom = 130;
  view.dispatchEvent(new Event('scroll'));
  flush();
  assert.equal(popup.opened, true);
  view.dispatchEvent(new Event('resize'));
  assert.equal(frames.size, 1);
  bridge.dispose();
  assert.equal(frames.size, 0);
  view.dispatchEvent(new Event('scroll'));
  assert.equal(frames.size, 0);
  assert.equal(reads, 2);
});

test('completion positioning respects visual viewport offsets and keyboard height', () => {
  const { bridge, popup, view } = setupFloating({ x: 200, y: 170 });
  Object.assign(view.visualViewport, { width: 280, height: 180, offsetLeft: 40, offsetTop: 50 });
  view.visualViewport.dispatchEvent(new Event('resize'));
  assert.equal(popup.style.left, '72px');
  assert.equal(popup.style.top, '66px');
  assert.equal(popup.style.width, '240px');
  view.visualViewport.height = 100;
  view.visualViewport.dispatchEvent(new Event('resize'));
  assert.equal(popup.opened, false);
  bridge.dispose();
});

function setup(invoke) {
  const calls = [];
  const input = new EventTarget();
  Object.assign(input, {
    value: 'cus', selectionStart: 3, selectionEnd: 3, readOnly: false,
    scrollTop: 0, scrollLeft: 0,
    focus() { globalThis.document.activeElement = input; },
    setSelectionRange(start, end) { this.selectionStart = start; this.selectionEnd = end; },
    setRangeText(text, start, end) {
      this.value = this.value.slice(0, start) + text + this.value.slice(end);
      this.selectionStart = this.selectionEnd = start + text.length;
    },
  });
  const highlight = {};
  const surface = {
    dataset: { generation: '1', completion: 'false', enhanced: 'true' },
    querySelector() { return highlight; },
    showPopover() {},
  };
  surface.querySelector = selector => selector === '.bpmn-feelsharp-highlight' ? highlight : null;
  globalThis.document = { activeElement: input, execCommand() { return false; } };
  globalThis.MutationObserver = class { observe() {} disconnect() {} };
  globalThis.ResizeObserver = class { observe() {} disconnect() {} };
  const receiver = {
    async invokeMethodAsync(method, ...args) {
      calls.push({ method, args });
      await invoke?.(method, ...args);
    },
  };
  const bridge = attach(surface, input, receiver, 1);
  return { input, surface, highlight, calls, bridge };
}

test('blur commit survives a later native selection snapshot', async () => {
  const { input, calls, bridge } = setup();
  input.dispatchEvent(new Event('input'));
  globalThis.document.activeElement = null;
  input.dispatchEvent(new Event('focusout'));
  input.dispatchEvent(new Event('select'));
  await settle();
  assert.equal(calls.filter(call => call.method === 'NativeKeyAsync' && call.args[0] === 'commit').length, 1);
  assert.equal(calls.find(call => call.method === 'SnapshotAsync').args[0], 'cus');
  bridge.dispose();
});

test('identical acknowledged snapshots are suppressed without dropping caret changes or explicit completion', async () => {
  const { input, calls, bridge } = setup();
  input.dispatchEvent(new Event('input'));
  await settle();
  for (const type of ['select', 'click', 'select']) {
    input.dispatchEvent(new Event(type));
    await settle();
  }
  assert.equal(calls.filter(call => call.method === 'SnapshotAsync').length, 1);
  input.selectionStart = input.selectionEnd = 2;
  input.dispatchEvent(new Event('select'));
  await settle();
  bridge.complete(1);
  await settle();
  assert.equal(calls.filter(call => call.method === 'SnapshotAsync').length, 3);
  assert.equal(calls.filter(call => call.method === 'NativeKeyAsync' && call.args[0] === 'complete').length, 1);
  bridge.dispose();
});

test('identical snapshot retries after failed delivery and Escape allows reopening at the same caret', async () => {
  let fails = true;
  const { input, surface, calls, bridge } = setup(async method => {
    if (method === 'SnapshotAsync' && fails) { fails = false; throw new Error('test delivery failure'); }
  });
  const log = console.error;
  console.error = () => {};
  try {
    input.dispatchEvent(new Event('input'));
    await settle();
  } finally { console.error = log; }
  input.dispatchEvent(new Event('select'));
  await settle();
  assert.equal(calls.filter(call => call.method === 'SnapshotAsync').length, 2);
  surface.dataset.completion = 'true';
  const escape = new Event('keydown', { cancelable: true });
  Object.assign(escape, { key: 'Escape' });
  input.dispatchEvent(escape);
  await settle();
  input.dispatchEvent(new Event('click'));
  await settle();
  assert.equal(calls.filter(call => call.method === 'SnapshotAsync').length, 3);
  bridge.dispose();
});

test('a slow snapshot retains only the latest queued draft and pending commit', async () => {
  let release;
  const pending = new Promise(resolve => { release = resolve; });
  const { input, calls, bridge } = setup(async method => {
    if (method === 'SnapshotAsync') await pending;
  });
  input.dispatchEvent(new Event('input'));
  await settle();
  for (const value of ['a', 'am', 'amount']) {
    input.value = value;
    input.selectionStart = input.selectionEnd = value.length;
    input.dispatchEvent(new Event('input'));
  }
  input.dispatchEvent(new Event('focusout'));
  release();
  await settle();
  assert.equal(calls.filter(call => call.method === 'SnapshotAsync').length, 2);
  assert.equal(calls.filter(call => call.method === 'SnapshotAsync').at(-1).args[0], 'amount');
  assert.equal(calls.filter(call => call.method === 'NativeKeyAsync' && call.args[0] === 'commit').length, 1);
  bridge.dispose();
});

test('composition blocks commits and completion edits until finished', async () => {
  const { input, calls, bridge } = setup();
  input.dispatchEvent(new Event('compositionstart'));
  input.dispatchEvent(new Event('focusout'));
  await settle();
  assert.ok(calls.some(call => call.method === 'SnapshotAsync' && call.args[5]));
  assert.ok(!calls.some(call => call.method === 'NativeKeyAsync'));
  assert.equal(bridge.replace('cus', 0, 3, 'customer', 2, 1), false);
  input.dispatchEvent(new Event('compositionend'));
  await settle();
  assert.ok(calls.some(call => call.method === 'SnapshotAsync' && !call.args[5]));
  assert.equal(calls.filter(call => call.method === 'NativeKeyAsync' && call.args[0] === 'commit').length, 1);
  bridge.dispose();
});

test('replacement validates source, sequence and generation', async () => {
  const { input, calls, bridge } = setup();
  input.dispatchEvent(new Event('input'));
  await settle();
  const sequence = calls[0].args[3];
  assert.equal(bridge.replace('wrong', 0, 3, 'customer', sequence, 1), false);
  assert.equal(bridge.replace('cus', 0, 3, 'customer', sequence, 0), false);
  assert.equal(bridge.replace('cus', 0, 3, 'customer', sequence - 1, 1), false);
  assert.equal(bridge.replace('cus', 0, 3, 'customer', sequence, 1), true);
  await settle();
  assert.equal(input.value, 'customer');
  assert.equal(input.selectionStart, 8);
  bridge.dispose();
});

test('scroll sync and disposal preserve native fallback', async () => {
  const { input, surface, highlight, calls, bridge } = setup();
  input.scrollLeft = 20;
  input.scrollTop = 30;
  input.dispatchEvent(new Event('scroll'));
  assert.equal(highlight.scrollLeft, 20);
  assert.equal(highlight.scrollTop, 30);
  bridge.dispose();
  input.dispatchEvent(new Event('input'));
  await settle();
  assert.equal(calls.length, 0);
  assert.equal(surface.dataset.enhanced, 'false');
});

test('failed commit remains retryable after connection recovery', async t => {
  let fail = true;
  const errors = [];
  t.mock.method(console, 'error', (...args) => errors.push(args));
  const { input, surface, calls, bridge } = setup(async method => {
    if (method === 'NativeKeyAsync' && fail) {
      fail = false;
      throw new Error('Transient connection failure');
    }
  });
  input.dispatchEvent(new Event('focusout'));
  await settle();
  assert.equal(errors.length, 1);
  assert.equal(surface.dataset.enhanced, 'true');
  input.dispatchEvent(new Event('select'));
  await settle();
  assert.equal(calls.filter(call => call.method === 'NativeKeyAsync').length, 2);
  bridge.dispose();
});

function setupFluent() {
  const state = setup();
  state.bridge.dispose();
  state.surface.dataset.enhanced = 'true';
  state.highlight.style = {};
  state.surface.getBoundingClientRect = () => ({ left: 5, top: 10 });
  state.surface.getAttribute = () => null;
  state.surface.hasAttribute = () => false;
  const attributes = new Map([['aria-label', 'Expression'], ['aria-expanded', 'false']]);
  const surfaceAttributes = new Map([['data-input-label', 'Expression']]);
  state.surface.getAttribute = name => surfaceAttributes.get(name) ?? null;
  state.surface.hasAttribute = name => surfaceAttributes.has(name);
  const nativeAttributes = new Map();
  Object.assign(state.input, {
    getBoundingClientRect() { return { left: 7, top: 14 }; },
    clientWidth: 300, clientHeight: 100,
    getAttribute(name) { return nativeAttributes.get(name) ?? null; },
    setAttribute(name, value) { nativeAttributes.set(name, value); },
    removeAttribute(name) { nativeAttributes.delete(name); },
  });
  const shadow = { activeElement: state.input, querySelector() { return state.input; }, append() {} };
  state.input.getRootNode = () => shadow;
  const host = { localName: 'fluentui-textarea', isConnected: true, shadowRoot: shadow,
    ownerDocument: { getElementById() { return null; } },
    setAttribute(name, value) { attributes.set(name, value); },
    getAttribute(name) { return attributes.get(name) ?? null; } };
  globalThis.document.activeElement = host;
  const style = { remove() { this.removed = true; } };
  globalThis.document.createElement = () => style;
  globalThis.getComputedStyle = () => ({ color: 'rgb(0, 0, 0)', font: '14px monospace', letterSpacing: 'normal',
    tabSize: '4', padding: '8px' });
  globalThis.customElements = { whenDefined: async () => {} };
  globalThis.requestAnimationFrame = callback => { callback(); };
  globalThis.ResizeObserver = class { observe() {} disconnect() {} };
  return { ...state, host, attributes, surfaceAttributes, nativeAttributes, style };
}

test('Fluent adapter recognizes shadow focus and aligns native input overlay', async () => {
  const { surface, input, host, calls, highlight, nativeAttributes, style } = setupFluent();
  const bridge = await attachFluent(surface, host, { async invokeMethodAsync(method, ...args) { calls.push({ method, args }); } }, 1);
  input.dispatchEvent(new Event('input'));
  await settle();
  assert.equal(calls[0].args[6], true);
  assert.equal(nativeAttributes.get('aria-label'), 'Expression');
  assert.equal(highlight.style.left, '2px');
  assert.equal(highlight.style.top, '4px');
  assert.equal(highlight.style.width, '300px');
  bridge.dispose();
  assert.equal(style.removed, true);
});

test('Fluent overlay alignment batches within a frame while readonly synchronization stays immediate', async () => {
  const { surface, input, host, calls } = setupFluent();
  const frames = new Map();
  let next = 0;
  let reads = 0;
  input.getBoundingClientRect = () => { reads++; return { left: 7, top: 14 }; };
  host.ownerDocument.defaultView = {
    requestAnimationFrame(callback) { frames.set(++next, callback); return next; },
    cancelAnimationFrame(id) { frames.delete(id); },
  };
  const bridge = await attachFluent(surface, host, { async invokeMethodAsync(method, ...args) { calls.push({ method, args }); } }, 1);
  assert.equal(reads, 1);
  for (let index = 0; index < 5; index++) input.dispatchEvent(new Event('scroll'));
  assert.equal(frames.size, 1);
  assert.equal(reads, 1);
  for (const callback of frames.values()) callback();
  frames.clear();
  assert.equal(reads, 2);
  input.dispatchEvent(new Event('scroll'));
  bridge.dispose();
  assert.equal(frames.size, 0);
});
test('Fluent adapter attaches ready native controls without waiting for animation frames', async () => {
  const { surface, host } = setupFluent();
  globalThis.customElements.whenDefined = () => { throw new Error('Already-ready control must not wait for definition'); };
  globalThis.requestAnimationFrame = () => { throw new Error('Already-ready control must not wait for frames'); };
  const bridge = await attachFluent(surface, host, { async invokeMethodAsync() {} }, 1);
  bridge.dispose();
});

test('Fluent adapter still waits for definition and frames when native control is delayed', async () => {
  const { surface, host } = setupFluent();
  const shadow = host.shadowRoot;
  host.shadowRoot = null;
  let frames = 0;
  globalThis.customElements.whenDefined = async () => { host.shadowRoot = shadow; };
  globalThis.requestAnimationFrame = callback => { frames++; callback(); };
  const bridge = await attachFluent(surface, host, { async invokeMethodAsync() {} }, 1);
  assert.equal(frames, 2);
  bridge.dispose();
});

test('Fluent adapter rejects missing native control and skips stale generations', async () => {
  const { surface, host } = setupFluent();
  surface.dataset.generation = '2';
  const bridge = await attachFluent(surface, host, {}, 1);
  assert.equal(bridge.replace(), false);
  host.shadowRoot = null;
  await assert.rejects(attachFluent(surface, host, {}, 1), /native control unavailable/);
});

test('Fluent modal suspension updates native readonly without reapplying draft text', async () => {
  const { surface, host, input, surfaceAttributes } = setupFluent();
  surfaceAttributes.set('data-input-readonly', 'true');
  surface.dataset.inputReadonly = 'true';
  const bridge = await attachFluent(surface, host, { async invokeMethodAsync() {} }, 1);
  assert.equal(input.readOnly, true);
  assert.equal(input.value, 'cus');
  assert.equal(bridge.replace('cus', 0, 3, 'stale', 0, 1), false);
  surface.dataset.inputReadonly = 'false';
  input.dispatchEvent(new Event('scroll'));
  assert.equal(input.readOnly, false);
  assert.equal(input.value, 'cus');
  bridge.dispose();
});

test('explicit completion captures current native caret and survives selection coalescing', async () => {
  const { input, calls, bridge } = setup();
  input.value = 'amount + ';
  input.selectionStart = input.selectionEnd = 9;
  bridge.complete(1);
  input.dispatchEvent(new Event('select'));
  await settle();
  assert.equal(calls.length, 2);
  assert.equal(calls[0].method, 'SnapshotAsync');
  assert.deepEqual(calls[0].args.slice(0, 3), ['amount + ', 9, 9]);
  assert.equal(calls[1].method, 'NativeKeyAsync');
  assert.equal(calls[1].args[0], 'complete');
  assert.equal(calls[1].args[1], calls[0].args[3]);
  assert.equal(input.value, 'amount + ');
  bridge.dispose();
});

test('blur cancels a pending explicit completion rather than reopening suggestions after commit', async () => {
  const { input, calls, bridge } = setup();
  bridge.complete(1);
  globalThis.document.activeElement = null;
  input.dispatchEvent(new Event('focusout'));
  await settle();
  assert.equal(calls.filter(call => call.method === 'NativeKeyAsync' && call.args[0] === 'commit').length, 1);
  assert.equal(calls.filter(call => call.method === 'NativeKeyAsync' && call.args[0] === 'complete').length, 0);
  bridge.dispose();
});

test('Ctrl Space requests completion without swallowing other modifiers or composition', async () => {
  const { input, surface, calls, bridge } = setup();
  function key(options = {}) {
    const event = new Event('keydown', { cancelable: true });
    Object.assign(event, { key: ' ', code: 'Space', ctrlKey: true, metaKey: false,
      altKey: false, shiftKey: false, isComposing: false, ...options });
    input.dispatchEvent(event);
    return event;
  }
  assert.equal(key().defaultPrevented, true);
  await settle();
  assert.equal(calls.at(-1).args[0], 'complete');
  const count = calls.length;
  assert.equal(key({ altKey: true }).defaultPrevented, false);
  assert.equal(key({ metaKey: true }).defaultPrevented, false);
  assert.equal(key({ shiftKey: true }).defaultPrevented, false);
  assert.equal(key({ isComposing: true }).defaultPrevented, false);
  bridge.complete(0);
  await settle();
  assert.equal(calls.length, count);
  input.dispatchEvent(new Event('compositionstart'));
  await settle();
  assert.equal(key().defaultPrevented, false);
  bridge.complete(1);
  const composingCount = calls.length;
  await settle();
  assert.equal(calls.length, composingCount);
  input.dispatchEvent(new Event('compositionend'));
  await settle();
  surface.dataset.generation = '2';
  bridge.complete(1);
  const staleCount = calls.length;
  await settle();
  assert.equal(calls.length, staleCount);
  bridge.dispose();
});

test('configurable acceptance retains native Enter and Tab when disabled', async () => {
  const { input, surface, calls, bridge } = setup();
  surface.dataset.completion = 'true';
  surface.dataset.acceptEnter = surface.dataset.acceptTab = 'false';
  for (const key of ['Enter', 'Tab']) {
    const event = new Event('keydown', { cancelable: true });
    Object.assign(event, { key });
    input.dispatchEvent(event);
    assert.equal(event.defaultPrevented, false);
  }
  await settle();
  assert.equal(calls.length, 0);
  bridge.dispose();
});

test('snippet selection is applied through guarded native replacement', async () => {
  const { input, calls, bridge } = setup();
  input.dispatchEvent(new Event('input'));
  await settle();
  const version = calls[0].args[3];
  const text = 'if condition then value else null';
  assert.equal(bridge.replace('cus', 0, 3, text, version, 1, 3, 9), true);
  await settle();
  assert.equal(input.value, text);
  assert.deepEqual([input.selectionStart, input.selectionEnd], [3, 12]);
  assert.deepEqual(bridge.read(), { text, start: 3, end: 12 });
  bridge.dispose();
  assert.throws(() => bridge.read(), /unavailable/);
});

test('Fluent direct references associate light DOM help and selected option with shadow input', async () => {
  const { surface, host, input, calls, surfaceAttributes } = setupFluent();
  const help = { textContent: 'Function documentation' };
  const option = { textContent: 'sum(list)' };
  const list = { textContent: 'Suggestions' };
  host.ownerDocument.getElementById = id => ({ help, option, list })[id] ?? null;
  surfaceAttributes.set('data-input-describedby', 'help');
  surfaceAttributes.set('data-input-controls', 'list');
  surfaceAttributes.set('data-input-activedescendant', 'option');
  Object.assign(input, { ariaDescribedByElements: [], ariaControlsElements: [], ariaActiveDescendantElement: null });
  const bridge = await attachFluent(surface, host, { async invokeMethodAsync(method, ...args) { calls.push({ method, args }); } }, 1);
  assert.deepEqual(input.ariaDescribedByElements, [help]);
  assert.deepEqual(input.ariaControlsElements, [list]);
  assert.equal(input.ariaActiveDescendantElement, option);
  bridge.dispose();
});

test('Fluent unsupported reference properties preserve textual help without invalid cross shadow IDs', async () => {
  const { surface, host, nativeAttributes, surfaceAttributes } = setupFluent();
  host.ownerDocument.getElementById = () => ({ textContent: 'Function help and shortcuts' });
  surfaceAttributes.set('data-input-describedby', 'help');
  surfaceAttributes.set('data-input-controls', 'list');
  surfaceAttributes.set('data-input-activedescendant', 'option');
  const bridge = await attachFluent(surface, host, { async invokeMethodAsync() {} }, 1);
  assert.equal(nativeAttributes.get('aria-description'), 'Function help and shortcuts');
  assert.equal(nativeAttributes.has('aria-describedby'), false);
  assert.equal(nativeAttributes.has('aria-controls'), false);
  assert.equal(nativeAttributes.has('aria-activedescendant'), false);
  bridge.dispose();
});
