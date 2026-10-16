import { afterEach, describe, expect, it, vi } from 'vitest';

import { isSupported, registerAll, unregisterAll } from '../src/webmcp.js';
import type { DotNetReference, ToolDescriptor } from '../src/webmcp.js';

interface RegisteredTool {
  name: string;
  annotations: Record<string, boolean>;
  execute: (input: unknown, options?: { signal?: AbortSignal }) => Promise<unknown>;
}

const descriptor = (name: string, annotations: Partial<ToolDescriptor['annotations']> = {}): ToolDescriptor => ({
  name,
  description: `${name} description`,
  inputSchema: { type: 'object' },
  annotations: { readOnly: true, consequential: false, untrustedContent: false, ...annotations },
});

// A model context that records what is registered, and refuses a name it already holds as the draft does.
function fakeContext() {
  const tools = new Map<string, RegisteredTool>();
  const signals = new Map<string, AbortSignal>();
  return {
    tools,
    signals,
    registerTool: vi.fn((tool: RegisteredTool, options?: { signal: AbortSignal }) => {
      if (tools.has(tool.name)) return Promise.reject(new Error(`A tool named ${tool.name} is already registered.`));
      tools.set(tool.name, tool);
      if (options) {
        signals.set(tool.name, options.signal);
        options.signal.addEventListener('abort', () => tools.delete(tool.name));
      }
      return Promise.resolve();
    }),
  };
}

const reference = (invoke: (method: string, ...args: unknown[]) => Promise<unknown>): DotNetReference => ({
  invokeMethodAsync: vi.fn(invoke),
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('isSupported', () => {
  it('is false when neither document nor navigator has a model context', () => {
    vi.stubGlobal('document', {});
    vi.stubGlobal('navigator', {});
    expect(isSupported()).toBe(false);
  });

  it('finds the context on document', () => {
    vi.stubGlobal('document', { modelContext: fakeContext() });
    vi.stubGlobal('navigator', {});
    expect(isSupported()).toBe(true);
  });

  it('falls back to navigator, which the first draft used', () => {
    vi.stubGlobal('document', {});
    vi.stubGlobal('navigator', { modelContext: fakeContext() });
    expect(isSupported()).toBe(true);
  });

  it('prefers document over navigator', async () => {
    const onDocument = fakeContext();
    const onNavigator = fakeContext();
    vi.stubGlobal('document', { modelContext: onDocument });
    vi.stubGlobal('navigator', { modelContext: onNavigator });

    await registerAll(reference(() => Promise.resolve('{}')), [descriptor('bpmn_a')]);

    expect(onDocument.tools.has('bpmn_a')).toBe(true);
    expect(onNavigator.tools.size).toBe(0);
  });
});

describe('registerAll', () => {
  it('registers every tool with the editor annotations mapped to hints', async () => {
    const context = fakeContext();
    vi.stubGlobal('document', { modelContext: context });

    const result = await registerAll(reference(() => Promise.resolve('{}')), [
      descriptor('bpmn_get_diagram', { untrustedContent: true }),
      descriptor('bpmn_validate'),
    ]);

    expect(result.registered).toEqual(['bpmn_get_diagram', 'bpmn_validate']);
    expect(result.failures).toEqual([]);
    expect(context.tools.get('bpmn_get_diagram')?.annotations).toEqual({
      readOnlyHint: true,
      untrustedContentHint: true,
      consequentialHint: false,
    });
  });

  it('reports a refused tool instead of throwing, and registers the others', async () => {
    const context = fakeContext();
    vi.stubGlobal('document', { modelContext: context });
    await registerAll(reference(() => Promise.resolve('{}')), [descriptor('bpmn_a')]);

    const result = await registerAll(reference(() => Promise.resolve('{}')), [descriptor('bpmn_a'), descriptor('bpmn_b')]);

    expect(result.registered).toEqual(['bpmn_b']);
    expect(result.failures).toHaveLength(1);
    expect(result.failures[0]?.name).toBe('bpmn_a');
    expect(result.failures[0]?.message).toContain('already registered');
  });

  it('reports every tool as failed when the browser has no model context', async () => {
    vi.stubGlobal('document', {});
    vi.stubGlobal('navigator', {});

    const result = await registerAll(reference(() => Promise.resolve('{}')), [descriptor('bpmn_a')]);

    expect(result.registered).toEqual([]);
    expect(result.failures.map(item => item.name)).toEqual(['bpmn_a']);
  });

  it('never sets exposedTo', async () => {
    const context = fakeContext();
    vi.stubGlobal('document', { modelContext: context });

    await registerAll(reference(() => Promise.resolve('{}')), [descriptor('bpmn_a')]);

    const options = context.registerTool.mock.calls[0]?.[1] as Record<string, unknown>;
    expect(Object.keys(options)).toEqual(['signal']);
  });
});

describe('unregisterAll', () => {
  it('aborts the signal so the browser drops the tools', async () => {
    const context = fakeContext();
    vi.stubGlobal('document', { modelContext: context });
    const { id } = await registerAll(reference(() => Promise.resolve('{}')), [descriptor('bpmn_a'), descriptor('bpmn_b')]);

    unregisterAll(id);

    expect(context.tools.size).toBe(0);
    expect(context.signals.get('bpmn_a')?.aborted).toBe(true);
  });

  it('leaves another registration alone', async () => {
    const context = fakeContext();
    vi.stubGlobal('document', { modelContext: context });
    const first = await registerAll(reference(() => Promise.resolve('{}')), [descriptor('bpmn_a')]);
    await registerAll(reference(() => Promise.resolve('{}')), [descriptor('bpmn_b')]);

    unregisterAll(first.id);

    expect([...context.tools.keys()]).toEqual(['bpmn_b']);
  });

  it('ignores an id it does not know', () => {
    expect(() => {
      unregisterAll(-1);
    }).not.toThrow();
  });
});

describe('a tool call', () => {
  async function registered(invoke: (method: string, ...args: unknown[]) => Promise<unknown>) {
    const context = fakeContext();
    vi.stubGlobal('document', { modelContext: context });
    const calls = vi.fn(invoke);
    await registerAll({ invokeMethodAsync: (method, ...args) => calls(method, ...args) }, [descriptor('bpmn_get_diagram')]);
    return { tool: context.tools.get('bpmn_get_diagram')!, calls };
  }

  it('returns the parsed object, not the string', async () => {
    const { tool, calls } = await registered(() => Promise.resolve('{"ok":true,"revision":"r1"}'));

    const result = await tool.execute({ limit: 5 });

    expect(result).toEqual({ ok: true, revision: 'r1' });
    expect(calls).toHaveBeenCalledWith('ExecuteAsync', expect.any(String), 'bpmn_get_diagram', '{"limit":5}');
  });

  it('sends an empty object when the agent passes nothing', async () => {
    const { tool, calls } = await registered(() => Promise.resolve('{"ok":true}'));

    await tool.execute(undefined);

    expect(calls).toHaveBeenCalledWith('ExecuteAsync', expect.any(String), 'bpmn_get_diagram', '{}');
  });

  it('never rejects: a failing bridge becomes an ok:false result', async () => {
    const { tool } = await registered(() => Promise.reject(new Error('The circuit is gone.')));

    const result = await tool.execute({});

    expect(result).toEqual({ ok: false, code: 'INTERNAL-ERROR', message: 'The circuit is gone.' });
  });

  it('never rejects: a result that is not JSON becomes an ok:false result', async () => {
    const { tool } = await registered(() => Promise.resolve('not json'));

    const result = (await tool.execute({})) as { ok: boolean; code: string };

    expect(result.ok).toBe(false);
    expect(result.code).toBe('INTERNAL-ERROR');
  });

  it('tells .NET to cancel the call when the agent aborts it', async () => {
    let finish: (value: string) => void = () => undefined;
    const { tool, calls } = await registered((method: string) =>
      method === 'ExecuteAsync'
        ? new Promise<string>(resolve => {
            finish = resolve;
          })
        : Promise.resolve(undefined),
    );
    const controller = new AbortController();

    const pending = tool.execute({}, { signal: controller.signal });
    controller.abort();
    finish('{"ok":false,"code":"CANCELLED"}');
    await pending;

    const executed = calls.mock.calls.find(call => call[0] === 'ExecuteAsync');
    const cancelled = calls.mock.calls.find(call => call[0] === 'Cancel');
    expect(cancelled?.[1]).toBe(executed?.[1]);
  });

  it('does not cancel a call that finished before the abort', async () => {
    const { tool, calls } = await registered(() => Promise.resolve('{"ok":true}'));
    const controller = new AbortController();

    await tool.execute({}, { signal: controller.signal });
    controller.abort();

    expect(calls.mock.calls.some(call => call[0] === 'Cancel')).toBe(false);
  });
});
