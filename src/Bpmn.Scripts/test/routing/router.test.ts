import { describe, expect, it } from 'vitest';

import type { RoutingOptionsContract } from '../../src/interop/contracts.js';
import { isOrthogonal, routeFor } from '../../src/routing/router.js';
import { alignToEndpoints } from '../../src/routing/align.js';
import { offsetFor, separate } from '../../src/routing/parallel.js';

/**
 * The router abstraction (implementation plan, M5).
 *
 * Two things are being pinned here. One is that the choice of router is a property of the connector
 * kind rather than a global setting — BPMN draws Sequence Flow with right angles and an Association
 * without them, and a router that treated them alike would make an annotation read as process flow.
 * The other is that nothing outside the routing folder names a JointJS router: the plan asks for an
 * abstraction that could be swapped, which only holds while the rest of the editor asks for *a
 * route*.
 */
const OPTIONS: RoutingOptionsContract = {
  mode: 'manhattan',
  step: 10,
  padding: 20,
  parallelSeparation: 12,
  followPoolOrientation: true,
};

describe('choosing a router', () => {
  it('routes Sequence Flow and Message Flow orthogonally', () => {
    expect(routeFor('sequenceFlow', OPTIONS).name).toBe('bpmnOrthogonal');
    expect(routeFor('messageFlow', OPTIONS).name).toBe('bpmnOrthogonal');
  });

  it('leaves Associations straight', () => {
    // An Association is a loose tie to an annotation or a data object. Right angles would make it
    // read as process flow, which is the one thing it is not.
    expect(routeFor('association', OPTIONS).name).toBe('normal');
    expect(routeFor('dataAssociation', OPTIONS).name).toBe('normal');
    expect(isOrthogonal('association')).toBe(false);
  });

  it('keeps flows right-angled whatever the mode says, and only Associations straight', () => {
    // A flow is right-angled wherever it is drawn, moved or dragged, and the document holds it that
    // way, so the mode cannot make a preview straight for a route that is stored bent.
    const straight = { ...OPTIONS, mode: 'straight' } as const;

    expect(routeFor('sequenceFlow', straight).name).toBe('bpmnOrthogonal');
    expect(routeFor('messageFlow', straight).name).toBe('bpmnOrthogonal');
    expect(routeFor('association', straight).name).toBe('normal');
    expect(routeFor('association', straight).args).toBeUndefined();
  });

  it('carries the configured step and padding through', () => {
    const args = routeFor('sequenceFlow', { ...OPTIONS, step: 25, padding: 8 }).args;

    expect(args).toMatchObject({ step: 25, padding: 8 });
  });
});

describe('following the Pool orientation', () => {
  /*
   * These assert the *set* of directions, not its order. The earlier versions pinned
   * startDirections[0] and passed while the option did nothing: JointJS only ever asks whether a
   * direction is in the list, seeds every one of them into the same open set at equal cost, and
   * discards the order entirely. Listing all four with the dominant axis first was therefore
   * identical to listing all four in any order, and a connector in a horizontal Pool left through
   * the top of its shape whenever that happened to be a step shorter.
   */
  const setOf = (values: string[]): string[] => [...values].sort();

  it('confines a horizontal Pool to the horizontal edges', () => {
    const args = routeFor('sequenceFlow', OPTIONS, 'horizontal').args as {
      startDirections: string[];
      endDirections: string[];
    };

    expect(setOf(args.startDirections)).toEqual(['left', 'right']);
    expect(setOf(args.endDirections)).toEqual(['left', 'right']);
  });

  it('confines a vertical Pool to the vertical edges', () => {
    const args = routeFor('sequenceFlow', OPTIONS, 'vertical').args as {
      startDirections: string[];
      endDirections: string[];
    };

    expect(setOf(args.startDirections)).toEqual(['bottom', 'top']);
    expect(setOf(args.endDirections)).toEqual(['bottom', 'top']);
  });

  it('keeps both ends of the axis so a connector can double back', () => {
    // A loop to an earlier Task leaves by the side it came from. Pinning the direction to
    // "forwards" would make that route impossible rather than merely unusual.
    const args = routeFor('sequenceFlow', OPTIONS, 'horizontal').args as { startDirections: string[] };

    expect(args.startDirections).toContain('left');
    expect(args.startDirections).toContain('right');
  });

  it('allows every direction when the host turns it off', () => {
    const args = routeFor('sequenceFlow', { ...OPTIONS, followPoolOrientation: false }, 'vertical').args as {
      startDirections: string[];
    };

    expect(setOf(args.startDirections)).toEqual(['bottom', 'left', 'right', 'top']);
  });

  it('is the one thing that distinguishes the option', () => {
    // The regression this file missed: with the option on and off producing the same set, nothing
    // downstream could tell them apart.
    const on = routeFor('sequenceFlow', OPTIONS, 'horizontal').args as { startDirections: string[] };
    const off = routeFor('sequenceFlow', { ...OPTIONS, followPoolOrientation: false }, 'horizontal').args as {
      startDirections: string[];
    };

    expect(setOf(on.startDirections)).not.toEqual(setOf(off.startDirections));
  });
});

describe('separating parallel connectors', () => {
  // A four-point route: out of the source, along a corridor, into the target. The corridor is the
  // only part an orthogonal route between two fixed attachment points is free to move.
  const corridor = [
    { x: 320, y: 180 },
    { x: 390, y: 180 },
    { x: 390, y: 451 },
    { x: 901, y: 451 },
  ];

  const axial = (route: readonly { x: number; y: number }[]): boolean =>
    route.every((point, index) => {
      const previous = route[index - 1];
      return previous === undefined
        || Math.abs(point.x - previous.x) < 0.5
        || Math.abs(point.y - previous.y) < 0.5;
    });

  it('leaves the first connector between a pair alone', () => {
    expect(separate(corridor, 0, 12)).toEqual(corridor);
  });

  it('shifts the corridor and leaves the attachment points where they are', () => {
    const shifted = separate(corridor, 1, 12);

    // Section 4.6 puts the ends on the outline, so they may not move.
    expect(shifted[0]).toEqual(corridor[0]);
    expect(shifted.at(-1)).toEqual(corridor.at(-1));

    // The vertical run moved sideways, by the full separation.
    expect(shifted[1]).toEqual({ x: 402, y: 180 });
    expect(shifted[2]).toEqual({ x: 402, y: 451 });
  });

  it('keeps every segment on its own axis', () => {
    // The separation is worthless if it makes the route diagonal, which is what happened when it
    // moved the endpoints instead of the corridor.
    expect(axial(separate(corridor, 1, 12))).toBe(true);
    expect(axial(separate(corridor, 2, 12))).toBe(true);
    expect(axial(separate(corridor, 3, 12))).toBe(true);
  });

  it('alternates sides so adding one never moves the ones already drawn', () => {
    // Moving an existing connector would mean rewriting waypoints the document already states.
    expect(offsetFor(1, 12)).toBe(12);
    expect(offsetFor(2, 12)).toBe(-12);
    expect(offsetFor(3, 12)).toBe(24);
    expect(offsetFor(4, 12)).toBe(-24);
  });

  it('does nothing when separation is switched off', () => {
    expect(separate(corridor, 3, 0)).toEqual(corridor);
  });

  it('has nothing to offset on a straight two-point route', () => {
    // Two attachment points with a straight line between them have no interior, so two such
    // connectors overlap. Documented in docs/routing.md rather than faked.
    const straight = [{ x: 0, y: 0 }, { x: 100, y: 0 }];

    expect(separate(straight, 1, 12)).toEqual(straight);
  });

  it('moves every interior segment of a long route', () => {
    const long = [
      { x: 0, y: 0 },
      { x: 50, y: 0 },
      { x: 50, y: 100 },
      { x: 150, y: 100 },
      { x: 150, y: 200 },
      { x: 200, y: 200 },
    ];

    const shifted = separate(long, 1, 10);

    expect(axial(shifted)).toBe(true);
    expect(shifted[0]).toEqual(long[0]);
    expect(shifted.at(-1)).toEqual(long.at(-1));
    expect(shifted).not.toEqual(long);
  });
});

describe('holding a routed polyline to its endpoints', () => {
  /*
   * The router computes its bends from the anchors JointJS picked; section 4.6 says the connector
   * ends where the pointer released, projected onto the outline. Substituting section 4.6's ends
   * without moving the end bends leaves the first and last segments diagonal, which is the one
   * thing an orthogonal route may not contain.
   */
  const axial = (route: readonly { x: number; y: number }[]): boolean =>
    route.every((point, index) => {
      const previous = route[index - 1];
      return previous === undefined
        || Math.abs(point.x - previous.x) < 0.5
        || Math.abs(point.y - previous.y) < 0.5;
    });

  it('squares up the first and last segments', () => {
    // The shape the browser produced before this existed: ends that the bends did not line up with.
    const route = alignToEndpoints(
      { x: 320, y: 180 },
      [{ x: 390, y: 140 }, { x: 390, y: 281 }],
      { x: 841, y: 241 },
    );

    expect(axial(route)).toBe(true);
    expect(route[0]).toEqual({ x: 320, y: 180 });
    expect(route.at(-1)).toEqual({ x: 841, y: 241 });
  });

  it('keeps the interior bends where the router put them', () => {
    // Only the end bends move, and only along the one axis that squares them up.
    const route = alignToEndpoints(
      { x: 0, y: 50 },
      [{ x: 100, y: 0 }, { x: 200, y: 0 }, { x: 200, y: 100 }, { x: 300, y: 100 }],
      { x: 400, y: 60 },
    );

    expect(axial(route)).toBe(true);
    expect(route).toContainEqual({ x: 200, y: 0 });
  });

  it('drops a single bend that alignment moved onto an endpoint', () => {
    /*
     * The single-bend branch used to skip dropRepeats. When the two ends already share an axis the
     * corner lands exactly on one of them, and the duplicate survived into the link's vertices as a
     * zero-length final segment — which is the segment the arrowhead takes its rotation from. Two
     * shapes on one line is the commonest layout there is, so this was not an edge case.
     */
    expect(alignToEndpoints({ x: 0, y: 0 }, [{ x: 50, y: 0 }], { x: 100, y: 0 }))
      .toEqual([{ x: 0, y: 0 }, { x: 100, y: 0 }]);

    expect(alignToEndpoints({ x: 0, y: 0 }, [{ x: 0, y: 50 }], { x: 0, y: 100 }))
      .toEqual([{ x: 0, y: 0 }, { x: 0, y: 100 }]);
  });

  it('turns a single bend into whichever elbow the router was aiming at', () => {
    const across = alignToEndpoints({ x: 0, y: 0 }, [{ x: 90, y: 5 }], { x: 100, y: 100 });
    const over = alignToEndpoints({ x: 0, y: 0 }, [{ x: 5, y: 90 }], { x: 100, y: 100 });

    expect(across[1]).toEqual({ x: 100, y: 0 });
    expect(over[1]).toEqual({ x: 0, y: 100 });
  });

  it('leaves an aligned pair as a straight line', () => {
    expect(alignToEndpoints({ x: 0, y: 40 }, [], { x: 200, y: 40 })).toEqual([
      { x: 0, y: 40 },
      { x: 200, y: 40 },
    ]);
  });

  it('elbows a pair that lines up on neither axis', () => {
    // Two points sharing no axis have no straight orthogonal path, so the route needs a corner
    // even when the router offered none.
    const route = alignToEndpoints({ x: 0, y: 0 }, [], { x: 100, y: 60 });

    expect(route).toHaveLength(3);
    expect(axial(route)).toBe(true);
  });

  it('drops a bend that alignment moved onto its neighbour', () => {
    const route = alignToEndpoints(
      { x: 0, y: 0 },
      [{ x: 0, y: 10 }, { x: 0, y: 20 }],
      { x: 0, y: 30 },
    );

    expect(route).toEqual([{ x: 0, y: 0 }, { x: 0, y: 30 }]);
  });
});
