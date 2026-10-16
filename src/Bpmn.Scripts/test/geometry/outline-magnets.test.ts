import { describe, expect, it } from 'vitest';

import {
  magnetsOf,
  normalizedEndpoint,
  outlineOf,
  projectToOutline,
  snapToMagnet,
} from '../../src/geometry/outline.js';
import cases from '../fixtures/outline-cases.json';

type Box = readonly [number, number, number, number];

const boundsOf = ([x, y, width, height]: Box) => ({ x, y, width, height });

/**
 * The shared table: the same cases run against `Model/Geometry/BpmnOutline.cs`, so a document
 * normalised on import is drawn where it is stored.
 */
describe('the shared outline cases', () => {
  it.each(cases.projections)('projects: $name', (c) => {
    const point = projectToOutline({ x: c.point[0]!, y: c.point[1]! }, outlineOf(c.elementType, boundsOf(c.bounds as unknown as Box)));

    expect(point.x).toBeCloseTo(c.expected[0]!, 4);
    expect(point.y).toBeCloseTo(c.expected[1]!, 4);
  });

  it.each(cases.endpoints)('normalises: $name', (c) => {
    const point = normalizedEndpoint(
      { x: c.waypoint[0]!, y: c.waypoint[1]! },
      outlineOf(c.elementType, boundsOf(c.bounds as unknown as Box)),
      { x: c.other[0]!, y: c.other[1]! },
    );

    expect(point.x).toBeCloseTo(c.expected[0]!, 4);
    expect(point.y).toBeCloseTo(c.expected[1]!, 4);
  });
});

describe('a rounded rectangle', () => {
  const task = outlineOf('task', { x: 0, y: 0, width: 100, height: 60 });

  it('is followed round its corner arc rather than cut across it', () => {
    // Points running round the top-left corner all sit at the arc's radius from its centre.
    for (const point of [{ x: -5, y: -5 }, { x: -3, y: -12 }, { x: -12, y: -3 }]) {
      const projected = projectToOutline(point, task);

      expect(Math.hypot(projected.x - 10, projected.y - 10)).toBeCloseTo(10, 6);
    }
  });
});

describe('magnets', () => {
  it('are the compass points of circles, diamonds and activities', () => {
    for (const type of ['startEvent', 'exclusiveGateway', 'task']) {
      expect(magnetsOf(outlineOf(type, { x: 0, y: 0, width: 100, height: 60 }))).toEqual([
        { x: 50, y: 0 },
        { x: 100, y: 30 },
        { x: 50, y: 60 },
        { x: 0, y: 30 },
      ]);
    }
  });

  it('are absent from rectangles', () => {
    expect(magnetsOf(outlineOf('pool', { x: 0, y: 0, width: 100, height: 60 }))).toEqual([]);
    expect(magnetsOf(outlineOf('dataObject', { x: 0, y: 0, width: 36, height: 50 }))).toEqual([]);
  });

  it('pull a nearby point onto them', () => {
    const task = outlineOf('task', { x: 0, y: 0, width: 100, height: 60 });

    expect(snapToMagnet({ x: 56, y: 0 }, task)).toEqual({ x: 50, y: 0 });
    expect(snapToMagnet({ x: 100, y: 24 }, task)).toEqual({ x: 100, y: 30 });
  });

  it('leave a point outside their radius where it is', () => {
    const task = outlineOf('task', { x: 0, y: 0, width: 100, height: 60 });

    expect(snapToMagnet({ x: 70, y: 0 }, task)).toEqual({ x: 70, y: 0 });
  });

  it('keep a snapped point on the outline', () => {
    const circle = outlineOf('startEvent', { x: 0, y: 0, width: 36, height: 36 });
    const projected = projectToOutline({ x: 40, y: 22 }, circle);
    const snapped = snapToMagnet(projected, circle);

    expect(projectToOutline(snapped, circle).x).toBeCloseTo(snapped.x, 6);
    expect(projectToOutline(snapped, circle).y).toBeCloseTo(snapped.y, 6);
  });
});
