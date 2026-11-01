import assert from 'node:assert/strict';
import { test } from 'node:test';
import { attach, attachFluent } from '../dist/feelsharp-editor.js';

const settle = () => new Promise(resolve => setTimeout(resolve, 10));

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
  };
  globalThis.document = { activeElement: input, execCommand() { return false; } };
  globalThis.MutationObserver = class { observe() {} disconnect() {} };
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
    setAttribute(name, value) { nativeAttributes.set(name, value); },
    removeAttribute(name) { nativeAttributes.delete(name); },
  });
  const shadow = { activeElement: state.input, querySelector() { return state.input; }, append() {} };
  state.input.getRootNode = () => shadow;
  const host = { localName: 'fluentui-textarea', isConnected: true, shadowRoot: shadow,
    ownerDocument: { getElementById() { return null; } },
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

test('Fluent adapter rejects missing native control and skips stale generations', async () => {
  const { surface, host } = setupFluent();
  surface.dataset.generation = '2';
  const bridge = await attachFluent(surface, host, {}, 1);
  assert.equal(bridge.replace(), false);
  host.shadowRoot = null;
  await assert.rejects(attachFluent(surface, host, {}, 1), /native control unavailable/);
});
