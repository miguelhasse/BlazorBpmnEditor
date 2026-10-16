import { describe, expect, it } from 'vitest';

import {
  activityMarkerIcon,
  eventTriggerIcon,
  gatewayIcon,
  icon,
  iconManifestInfo,
  taskTypeIcon,
} from '../../src/icons/registry.js';
import type { BpmnEventDefinitionType, BpmnFlowNodeType } from '../../src/model/contracts.js';

/**
 * Icon resolution.
 *
 * The manifest itself is gated by scripts/verify-icons.mjs; these cover the semantic layer on top
 * of it — which glyph a given BPMN construct resolves to, and, just as importantly, which
 * constructs resolve to nothing at all.
 */
describe('manifest', () => {
  it('reports its version and attribution', () => {
    expect(iconManifestInfo.manifestVersion).toBe('1.0.0');
    expect(iconManifestInfo.attribution).toContain('SIL Open Font License');
  });

  it('throws for an unknown icon id', () => {
    // A missing glyph is a notation error a reader cannot detect, so it must not be silent.
    expect(() => icon('not-a-real-icon')).toThrow(/No icon/);
  });
});

describe('gatewayIcon', () => {
  it.each([
    ['exclusiveGateway', 'gateway-xor'],
    ['inclusiveGateway', 'gateway-or'],
    ['parallelGateway', 'gateway-parallel'],
    ['complexGateway', 'gateway-complex'],
    ['eventBasedGateway', 'gateway-eventbased'],
  ] as [BpmnFlowNodeType, string][])('resolves %s', (type, iconId) => {
    expect(gatewayIcon(type)?.iconId).toBe(iconId);
  });

  it('marks gateway glyphs as inner symbols, since the editor draws the diamond', () => {
    expect(gatewayIcon('exclusiveGateway')?.isInnerSymbol).toBe(true);
  });

  it('resolves nothing for a non-gateway', () => {
    expect(gatewayIcon('task')).toBeUndefined();
  });
});

describe('taskTypeIcon', () => {
  it.each([
    ['userTask', 'user'],
    ['manualTask', 'manual'],
    ['serviceTask', 'service'],
    ['scriptTask', 'script'],
    ['businessRuleTask', 'business-rule'],
    ['sendTask', 'send'],
    ['receiveTask', 'receive'],
  ] as [BpmnFlowNodeType, string][])('resolves %s', (type, iconId) => {
    expect(taskTypeIcon(type)?.iconId).toBe(iconId);
  });

  it('uses the bare glyph family, which needs no extraction', () => {
    // The "*-task" pictograms bundle the rounded rectangle and would double the body.
    expect(taskTypeIcon('userTask')?.isInnerSymbol).toBe(false);
  });

  it('resolves nothing for a generic Task or a Call Activity', () => {
    // A Task has no symbol by definition; a Call Activity is denoted by its thick border.
    expect(taskTypeIcon('task')).toBeUndefined();
    expect(taskTypeIcon('callActivity')).toBeUndefined();
  });
});

describe('eventTriggerIcon', () => {
  it('resolves nothing for a None event', () => {
    expect(eventTriggerIcon('start', 'none', true)).toBeUndefined();
  });

  it('distinguishes catching from throwing for the same trigger', () => {
    const caught = eventTriggerIcon('intermediateCatch', 'message', true);
    const thrown = eventTriggerIcon('intermediateThrow', 'message', true);

    // This is how section 1.1's unfilled-versus-filled rule is carried: different glyphs.
    expect(caught?.iconId).toBe('intermediate-event-catch-message');
    expect(thrown?.iconId).toBe('intermediate-event-throw-message');
    expect(caught?.pathData).not.toBe(thrown?.pathData);
  });

  it('uses the non-interrupting glyph for a non-interrupting Start Event', () => {
    expect(eventTriggerIcon('start', 'message', false)?.iconId).toBe('start-event-non-interrupting-message');
  });

  it('draws a Boundary Event with the Intermediate Catch glyphs', () => {
    // The notation is identical; only the ring dashing differs, and the editor owns the rings.
    expect(eventTriggerIcon('boundary', 'timer', true)?.iconId).toBe('intermediate-event-catch-timer');
    expect(eventTriggerIcon('boundary', 'timer', false)?.iconId)
      .toBe('intermediate-event-catch-non-interrupting-timer');
  });

  it('resolves the End Event triggers, including Terminate', () => {
    expect(eventTriggerIcon('end', 'terminate', true)?.iconId).toBe('end-event-terminate');
    expect(eventTriggerIcon('end', 'error', true)?.iconId).toBe('end-event-error');
  });

  it('resolves nothing for a trigger that is illegal in that context', () => {
    // Terminate is End-only and Timer cannot be thrown; the renderer draws no glyph rather than
    // inventing one. Rejecting the combination outright is the validation engine's job.
    expect(eventTriggerIcon('start', 'terminate', true)).toBeUndefined();
    expect(eventTriggerIcon('intermediateThrow', 'timer', true)).toBeUndefined();
  });

  it('marks event glyphs as inner symbols, since the editor draws the rings', () => {
    expect(eventTriggerIcon('start', 'message', true)?.isInnerSymbol).toBe(true);
  });

  it.each([
    'message',
    'timer',
    'conditional',
    'signal',
    'multiple',
    'parallelMultiple',
  ] as BpmnEventDefinitionType[])('resolves %s for a plain Start Event', (definition) => {
    expect(eventTriggerIcon('start', definition, true)).toBeDefined();
  });
});

describe('activityMarkerIcon', () => {
  it('resolves every marker to bare path data', () => {
    for (const marker of [
      'loop',
      'multiInstanceParallel',
      'multiInstanceSequential',
      'compensation',
      'collapsed',
      'adHoc',
    ] as const) {
      const glyph = activityMarkerIcon(marker);
      expect(glyph?.pathData).toMatch(/^M/);
      expect(glyph?.isInnerSymbol).toBe(false);
    }
  });
});
