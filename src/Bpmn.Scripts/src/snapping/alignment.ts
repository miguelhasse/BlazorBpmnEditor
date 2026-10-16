/**
 * Alignment guides ("snaplines") for a moving shape against its peers.
 *
 * Pure geometry, with no JointJS or DOM dependency, because this is where the behaviour actually
 * lives: which edges count as aligned, which candidate wins when several are in range, and how far
 * the shape is nudged. Rendering the result is a separate, trivial concern.
 *
 * JointJS OSS has no alignment-guide feature — it is a JointJS+ widget — so this is one of the
 * places the specification explicitly allows custom code.
 */

/** An axis-aligned rectangle in paper coordinates. */
export interface Box {
  readonly x: number;
  readonly y: number;
  readonly width: number;
  readonly height: number;
}

/** A line to draw, spanning the shapes it relates. */
export interface Guide {
  readonly orientation: 'vertical' | 'horizontal';
  /** The x of a vertical guide, or the y of a horizontal one. */
  readonly position: number;
  /** The extent of the line along its own axis, covering both aligned shapes. */
  readonly from: number;
  readonly to: number;
}

/** Where the shape should sit, and the guides that explain why. */
export interface SnapResult {
  readonly x: number;
  readonly y: number;
  readonly guides: readonly Guide[];
}

/** The three alignments considered on each axis: leading edge, centre, trailing edge. */
type Alignment = 'start' | 'center' | 'end';

const ALIGNMENTS: readonly Alignment[] = ['start', 'center', 'end'];

interface Candidate {
  readonly delta: number;
  readonly position: number;
  readonly peer: Box;
}

const horizontalEdge = (box: Box, alignment: Alignment): number => {
  switch (alignment) {
    case 'start': return box.x;
    case 'center': return box.x + (box.width / 2);
    case 'end': return box.x + box.width;
  }
};

const verticalEdge = (box: Box, alignment: Alignment): number => {
  switch (alignment) {
    case 'start': return box.y;
    case 'center': return box.y + (box.height / 2);
    case 'end': return box.y + box.height;
  }
};

/**
 * Finds the nearest alignment on one axis within tolerance.
 *
 * Every combination of the moving shape's three edges against every peer's three edges is
 * considered, and the smallest correction wins. Comparing only like with like — left to left,
 * centre to centre — would miss the common case of a shape's left edge lining up with the centre
 * of the one above it.
 */
function nearest(
  moving: Box,
  peers: readonly Box[],
  tolerance: number,
  edgeOf: (box: Box, alignment: Alignment) => number,
): Candidate | null {
  let best: Candidate | null = null;

  for (const peer of peers) {
    for (const movingAlignment of ALIGNMENTS) {
      const from = edgeOf(moving, movingAlignment);

      for (const peerAlignment of ALIGNMENTS) {
        const to = edgeOf(peer, peerAlignment);
        const delta = to - from;
        if (Math.abs(delta) > tolerance) continue;

        // Strictly nearer, so an equally good later candidate does not displace an earlier one
        // and the result stays stable as the pointer moves.
        if (best === null || Math.abs(delta) < Math.abs(best.delta)) {
          best = { delta, position: to, peer };
        }
      }
    }
  }

  return best;
}

/** The span a guide should cover so it visibly joins both shapes. */
const spanOf = (a: number, b: number, c: number, d: number): { from: number; to: number } => ({
  from: Math.min(a, c),
  to: Math.max(b, d),
});

/**
 * Snaps a moving box to its peers and returns the guides to draw.
 *
 * The two axes are resolved independently, which is what lets a shape snap to one neighbour
 * horizontally and a different one vertically at the same time — the usual case when dropping a
 * node into a grid of existing ones.
 *
 * @param moving The box being dragged, at its unsnapped position.
 * @param peers Candidate boxes to align against; the moving box must not be among them.
 * @param tolerance Maximum correction, in paper units. Zero or less disables snapping.
 */
export function snapToPeers(moving: Box, peers: readonly Box[], tolerance: number): SnapResult {
  if (tolerance <= 0 || peers.length === 0) {
    return { x: moving.x, y: moving.y, guides: [] };
  }

  const guides: Guide[] = [];

  const horizontal = nearest(moving, peers, tolerance, horizontalEdge);
  const vertical = nearest(moving, peers, tolerance, verticalEdge);

  const x = moving.x + (horizontal?.delta ?? 0);
  const y = moving.y + (vertical?.delta ?? 0);
  const snapped: Box = { x, y, width: moving.width, height: moving.height };

  if (horizontal !== null) {
    // A vertical line marks a horizontal alignment: the guide runs along the other axis.
    const span = spanOf(
      snapped.y,
      snapped.y + snapped.height,
      horizontal.peer.y,
      horizontal.peer.y + horizontal.peer.height,
    );

    guides.push({ orientation: 'vertical', position: horizontal.position, ...span });
  }

  if (vertical !== null) {
    const span = spanOf(
      snapped.x,
      snapped.x + snapped.width,
      vertical.peer.x,
      vertical.peer.x + vertical.peer.width,
    );

    guides.push({ orientation: 'horizontal', position: vertical.position, ...span });
  }

  return { x, y, guides };
}
