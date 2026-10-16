import { describe, expect, it } from 'vitest';
import { shapes } from '@joint/core';

import { isLane } from '../../src/graph/lane.js';

describe('isLane', () => {
  it('recognises a Lane by its element type and nothing else', () => {
    const lane = new shapes.standard.Rectangle({ bpmn: { elementType: 'lane' } });
    const pool = new shapes.standard.Rectangle({ bpmn: { elementType: 'pool' } });
    const plain = new shapes.standard.Rectangle();

    expect(isLane(lane)).toBe(true);
    expect(isLane(pool)).toBe(false);
    expect(isLane(plain)).toBe(false);
  });
});
