import manifest from '../../../../icons/bpmn-icons.json' with { type: 'json' };

import type { BpmnEventDefinitionType, BpmnFlowNodeType } from '../model/contracts.js';
import type { BpmnEventContext } from '../model/element-kind.js';

/**
 * The shared BPMN icon manifest.
 *
 * Imported as JSON and bundled, so the shipped artifact carries the path data itself: no webfont,
 * no bpmn-io package, no network access (specification section 4.5). The identical file is
 * embedded in the C# assembly, which is what keeps stencil and canvas notation from drifting.
 */

export interface BpmnIcon {
  readonly iconId: string;
  readonly upstreamName: string;
  readonly codePoint: string | null;
  /** True when the outer ring or diamond was stripped and the editor must draw it. */
  readonly isInnerSymbol: boolean;
  readonly pathData: string;
  readonly viewBox: string;
  readonly baseline: number;
  readonly alignment: string;
  readonly accessibilityLabel: string;
}

/** Bottom-centre Activity markers, in the order section 4.3 requires them to be laid out. */
export type BpmnActivityMarker =
  | 'compensation'
  | 'loop'
  | 'multiInstanceParallel'
  | 'multiInstanceSequential'
  | 'collapsed'
  | 'adHoc';

interface Assignment {
  readonly kind: string;
  readonly iconId: string;
  readonly type?: string;
  readonly context?: string;
  readonly trigger?: string;
  readonly marker?: string;
}

const icons: ReadonlyMap<string, BpmnIcon> = new Map(
  (manifest.icons as readonly BpmnIcon[]).map((icon) => [icon.iconId, icon]),
);

const assignments = manifest.assignments as readonly Assignment[];

function index(kind: string, key: (assignment: Assignment) => string | undefined): ReadonlyMap<string, string> {
  const map = new Map<string, string>();
  for (const assignment of assignments) {
    if (assignment.kind !== kind) continue;
    const value = key(assignment);
    if (value !== undefined) map.set(value, assignment.iconId);
  }
  return map;
}

const gatewayIcons = index('gateway', (a) => a.type);
const taskTypeIcons = index('taskType', (a) => a.type);
const activityMarkerIcons = index('activityMarker', (a) => a.marker);
const dataMarkerIcons = index('dataMarker', (a) => a.marker);
const eventIcons = index('eventTrigger', (a) => `${a.context ?? ''}/${a.trigger ?? ''}`);

/** The manifest version and content digest, for diagnostics and compatibility checks. */
export const iconManifestInfo = {
  manifestVersion: manifest.manifestVersion,
  contentHash: manifest.contentHash,
  attribution: manifest.source.attribution,
} as const;

/**
 * Resolves an icon by id.
 *
 * Throws rather than returning undefined: an unknown id means the manifest and the code that
 * references it have diverged, and a silently missing glyph is a notation error a reader cannot
 * detect (specification section 4.5).
 */
export function icon(iconId: string): BpmnIcon {
  const found = icons.get(iconId);
  if (found === undefined) {
    throw new Error(`No icon '${iconId}' in the BPMN icon manifest.`);
  }
  return found;
}

export function tryIcon(iconId: string | undefined): BpmnIcon | undefined {
  return iconId === undefined ? undefined : icons.get(iconId);
}

/** The inner glyph for a Gateway, or undefined for an unmarked one. */
export function gatewayIcon(type: BpmnFlowNodeType): BpmnIcon | undefined {
  return tryIcon(gatewayIcons.get(type));
}

/**
 * The top-left type symbol for a Task.
 *
 * Undefined for a generic Task, which has no symbol by definition, and for a Call Activity, whose
 * notation is a thick border rather than a glyph (section 4.4).
 */
export function taskTypeIcon(type: BpmnFlowNodeType): BpmnIcon | undefined {
  return tryIcon(taskTypeIcons.get(type));
}

export function activityMarkerIcon(marker: BpmnActivityMarker): BpmnIcon | undefined {
  return tryIcon(activityMarkerIcons.get(marker));
}

/** A Data Object marker, currently only the collection three-bar symbol (section 1.6). */
export function dataMarkerIcon(marker: 'collection'): BpmnIcon | undefined {
  return tryIcon(dataMarkerIcons.get(marker));
}

/**
 * The trigger glyph for an Event.
 *
 * bpmn-font names these per context, so a catching Message and a throwing Message are different
 * glyphs — which is exactly how the unfilled/filled distinction of section 1.1 is carried. A None
 * event has no glyph, and a combination the font does not cover resolves to undefined rather than
 * to something approximate.
 */
export function eventTriggerIcon(
  context: BpmnEventContext,
  definition: BpmnEventDefinitionType,
  isInterrupting: boolean,
): BpmnIcon | undefined {
  if (definition === 'none') return undefined;
  return tryIcon(eventIcons.get(`${manifestContext(context, isInterrupting)}/${definition}`));
}

/**
 * Maps an event's flow context onto the manifest's context vocabulary.
 *
 * Non-interrupting is only meaningful where an event can interrupt at all: a Boundary Event or an
 * Event Sub-Process Start Event. A Boundary Event uses the Intermediate Catch glyphs, since the
 * notation is identical and only the ring dashing differs.
 */
function manifestContext(context: BpmnEventContext, isInterrupting: boolean): string {
  switch (context) {
    case 'start':
      return isInterrupting ? 'start' : 'startNonInterrupting';
    case 'intermediateCatch':
      return 'intermediateCatch';
    case 'boundary':
      return isInterrupting ? 'intermediateCatch' : 'intermediateCatchNonInterrupting';
    case 'intermediateThrow':
      return 'intermediateThrow';
    case 'end':
      return 'end';
  }
}
