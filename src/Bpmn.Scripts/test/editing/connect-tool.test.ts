// @vitest-environment jsdom
import { describe, expect, it, vi } from 'vitest';

import { connectTool } from '../../src/editing/connect-tool.js';

vi.mock('@joint/core', () => {
  class Tool {
    public constructor(public readonly options: Record<string, unknown>) {}
  }
  return { dia: { ToolsView: Tool }, elementTools: { Connect: Tool } };
});

describe('connector affordance', () => {
  it.each(['textAnnotation', 'task', 'pool', 'lane'])('positions the %s handle without changing its size', (type) => {
    const tools = connectTool(type);
    const connect = tools.options.tools?.[0];
    expect(connect?.options).toMatchObject({
      x: type === 'textAnnotation' ? 0 : '100%',
      y: '50%',
      offset: { x: type === 'textAnnotation' ? -12 : 12, y: 0 },
      magnet: 'root',
    });
    expect(connect?.options.markup).toMatchObject([
      { tagName: 'circle', attributes: { r: 7 } }, { tagName: 'path' },
    ]);
  });
});
