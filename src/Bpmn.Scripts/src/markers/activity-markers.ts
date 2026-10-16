import * as constants from '../geometry/constants.js';
import { activityMarkerIcon, type BpmnActivityMarker } from '../icons/registry.js';
import type { BoundsContract, BpmnFlowNodeContract } from '../model/contracts.js';
import { isCollapsedContainer } from '../model/element-kind.js';

/** One marker placed in the bottom-centre band, with its offset inside that band. */
export interface MarkerPlacement {
  readonly marker: BpmnActivityMarker;
  readonly pathData: string;
  readonly viewBox: string;
  /** Offset from the activity's own origin, in activity coordinates. */
  readonly x: number;
  readonly y: number;
  readonly size: number;
}

/**
 * Chooses which bottom-centre markers an Activity shows, in specification section 4.3 order.
 *
 * The composition rules are notational, not cosmetic:
 *  - standard loop, sequential MI and parallel MI are mutually exclusive — at most one appears;
 *  - compensation may accompany that one;
 *  - the collapsed marker sits closest to the centre, with compensation and loop/MI to its left
 *    and the ad-hoc tilde to its right;
 *  - ad-hoc applies only to an Ad-Hoc Sub-Process.
 *
 * Returning the list in semantic order and centring it afterwards is what makes the layout
 * deterministic, which the section demands so that a diagram renders identically every time.
 */
export function markersFor(node: BpmnFlowNodeContract, isExpanded: boolean | null): readonly BpmnActivityMarker[] {
  const markers: BpmnActivityMarker[] = [];

  if (node.isForCompensation) markers.push('compensation');

  switch (node.loopCharacteristics) {
    case 'standard':
      markers.push('loop');
      break;
    case 'multiInstanceParallel':
      markers.push('multiInstanceParallel');
      break;
    case 'multiInstanceSequential':
      markers.push('multiInstanceSequential');
      break;
    case 'none':
      break;
  }

  // A Call Activity is opaque too, but its notation is the thick border; only a Sub-Process that
  // is genuinely collapsed carries the plus.
  if (isCollapsedContainer(node, isExpanded)) markers.push('collapsed');

  if (node.type === 'adHocSubProcess') markers.push('adHoc');

  return markers;
}

/**
 * Lays the markers out along the bottom edge.
 *
 * Section 4.3 fixes the arithmetic: each glyph occupies a 14px box, gaps are 5px, and the group is
 * centred at `x + width / 2` sitting `20px` above the activity's bottom edge.
 */
export function layoutActivityMarkers(
  markers: readonly BpmnActivityMarker[],
  bounds: BoundsContract,
): readonly MarkerPlacement[] {
  if (markers.length === 0) return [];

  const size = constants.ACTIVITY_MARKER_BOX;
  const gap = constants.ACTIVITY_MARKER_GAP;
  const groupWidth = size * markers.length + gap * (markers.length - 1);

  const left = bounds.width / 2 - groupWidth / 2;
  const top = bounds.height - constants.ACTIVITY_MARKER_BASELINE_OFFSET;

  const placements: MarkerPlacement[] = [];

  markers.forEach((marker, position) => {
    const glyph = activityMarkerIcon(marker);
    if (glyph === undefined) return;

    placements.push({
      marker,
      pathData: glyph.pathData,
      viewBox: glyph.viewBox,
      x: left + position * (size + gap),
      y: top,
      size,
    });
  });

  return placements;
}
