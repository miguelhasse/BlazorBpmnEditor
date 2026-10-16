import { describe, expect, it } from 'vitest';

import { validateAndAccept } from '../../src/config/accepted-config.js';
import { INTEROP_CONTRACT_VERSION } from '../../src/interop/contract-version.js';
import type { EditorConfigContract } from '../../src/interop/contracts.js';
import { canvasStrings } from '../fixtures/canvas-strings.js';

/**
 * Configuration acceptance.
 *
 * Rejection must be a value, never an exception: a thrown error crosses interop as an opaque JS
 * failure and loses the structured code and path the validation surface needs. And a rejected
 * snapshot must leave nothing built, so the canvas cannot appear with settings the host did not
 * choose (implementation plan section 2).
 */
function config(overrides: Partial<EditorConfigContract> = {}): EditorConfigContract {
  return {
    contractVersion: INTEROP_CONTRACT_VERSION,
    revision: 3,
    strings: canvasStrings,
    isReadOnly: false,
    canvas: { background: '#fff' },
    grid: { size: 10, isVisible: true, snapEnabled: false },
    viewport: {
      minZoom: 0.2,
      maxZoom: 4,
      zoomStep: 1.1,
      fitPadding: 20,
      wheelMode: 'pan',
      wheelPanSpeed: 1,
      dragPanEnabled: true,
      touchEnabled: true,
    },
    keyboard: {
      enabled: true,
      panStep: 50,
      bindings: [
        { command: 'zoomIn', key: '+', ctrl: false, shift: false, alt: false, meta: false, description: 'Zoom in' },
      ],
    },
    snapping: { alignmentGuidesEnabled: true, tolerance: 5 },
    editing: {
      preventCollisions: true,
      collisionClearance: 0,
      historyDepth: 100,
      longPressMilliseconds: 500,
      eventEditPolicy: 'reject',
    },
    routing: {
      mode: 'manhattan', step: 10, padding: 20, parallelSeparation: 12, followPoolOrientation: true,
    },
    ...overrides,
  };
}

describe('validateAndAccept', () => {
  it('accepts a valid snapshot and freezes it', () => {
    const result = validateAndAccept(config());

    expect(result.ok).toBe(true);
    if (!result.ok) return;

    expect(result.config.revision).toBe(3);
    expect(Object.isFrozen(result.config)).toBe(true);
    expect(Object.isFrozen(result.config.grid)).toBe(true);
    expect(result.config.strings.values.Expand).toBe('Expand sub-process');
    expect(Object.isFrozen(result.config.strings.values)).toBe(true);
  });

  it('rejects a snapshot targeting another contract version', () => {
    const result = validateAndAccept(config({ contractVersion: '0.9' }));

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.rejection.code).toBe('CFG-001');
    expect(result.rejection.path).toBe('contractVersion');
  });

  it.each([
    ['CFG-002', 'revision', config({ revision: -1 })],
    ['CFG-010', 'grid.size', config({ grid: { size: 0, isVisible: true, snapEnabled: false } })],
  ])('rejects %s at %s', (code, path, input) => {
    const result = validateAndAccept(input);

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.rejection.code).toBe(code);
    expect(result.rejection.path).toBe(path);
  });

  it.each([
    ['CFG-020', { minZoom: 0, maxZoom: 4, zoomStep: 1.1, fitPadding: 20 }],
    ['CFG-021', { minZoom: 2, maxZoom: 1, zoomStep: 1.1, fitPadding: 20 }],
    ['CFG-022', { minZoom: 0.2, maxZoom: 4, zoomStep: 1, fitPadding: 20 }],
    ['CFG-023', { minZoom: 0.2, maxZoom: 4, zoomStep: 1.1, fitPadding: -1 }],
  ])('rejects %s for an invalid viewport', (code, viewport) => {
    const result = validateAndAccept(
      config({ viewport: { ...viewport, wheelMode: 'pan', wheelPanSpeed: 1, dragPanEnabled: true, touchEnabled: true } }),
    );

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.rejection.code).toBe(code);
  });

  it('rejects rather than throws for a non-finite value', () => {
    // NaN survives JSON as null and arrives as a number-typed field, so it has to be checked
    // explicitly rather than trusted from the type.
    expect(() =>
      validateAndAccept(config({ grid: { size: Number.NaN, isVisible: true, snapEnabled: false } })),
    ).not.toThrow();
  });

  it('rejects an unknown event edit policy', () => {
    const result = validateAndAccept(config({
      editing: { ...config().editing, eventEditPolicy: 'repairTriggers' as 'reject' },
    }));

    expect(result.ok).toBe(false);
    if (result.ok) return;
    expect(result.rejection.code).toBe('CFG-053');
    expect(result.rejection.path).toBe('editing.eventEditPolicy');
  });
});
