import { INTEROP_CONTRACT_VERSION } from '../interop/contract-version.js';
import type {
  CanvasStringsContract,
  ConfigRejection,
  EditingOptionsContract,
  EditorConfigContract,
  GridOptionsContract,
  KeyboardOptionsContract,
  RoutingOptionsContract,
  ShortcutBindingContract,
  SnappingOptionsContract,
  ViewportOptionsContract,
} from '../interop/contracts.js';

/**
 * A validated, frozen configuration snapshot. Everything downstream (paper options,
 * viewport bounds, shape styling) derives from this and never from ad-hoc defaults.
 */
export interface AcceptedConfig {
  readonly contractVersion: string;
  readonly revision: number;
  readonly isReadOnly: boolean;
  readonly background: string;
  readonly grid: GridOptionsContract;
  readonly viewport: ViewportOptionsContract;
  readonly keyboard: KeyboardOptionsContract;
  readonly snapping: SnappingOptionsContract;
  readonly editing: EditingOptionsContract;
  readonly routing: RoutingOptionsContract;
  readonly strings: CanvasStringsContract;
}

export type ConfigResult =
  | { readonly ok: true; readonly config: AcceptedConfig }
  | { readonly ok: false; readonly rejection: ConfigRejection };

const reject = (revision: number, code: string, path: string, message: string): ConfigResult => ({
  ok: false,
  rejection: { revision, code, path, message },
});

const isFiniteNumber = (value: unknown): value is number =>
  typeof value === 'number' && Number.isFinite(value);

/**
 * Validates a snapshot from Blazor and freezes it.
 *
 * Rejection is a value, never an exception: a thrown error would cross the interop
 * boundary as an opaque JS exception and lose the structured `{code, path}` the
 * validation panel needs. On rejection the caller must leave the previously accepted
 * configuration active and must not construct a paper.
 */
export function validateAndAccept(contract: EditorConfigContract): ConfigResult {
  const revision = isFiniteNumber(contract.revision) ? contract.revision : -1;

  if (contract.contractVersion !== INTEROP_CONTRACT_VERSION) {
    return reject(
      revision,
      'CFG-001',
      'contractVersion',
      `Configuration targets interop contract '${contract.contractVersion}' but this module implements '${INTEROP_CONTRACT_VERSION}'.`,
    );
  }

  if (revision < 0) {
    return reject(revision, 'CFG-002', 'revision', 'Configuration revision must be a non-negative number.');
  }

  const { grid, viewport } = contract;

  if (!isFiniteNumber(grid.size) || grid.size <= 0) {
    return reject(revision, 'CFG-010', 'grid.size', 'Grid size must be a positive number.');
  }

  if (!isFiniteNumber(viewport.minZoom) || viewport.minZoom <= 0) {
    return reject(revision, 'CFG-020', 'viewport.minZoom', 'Minimum zoom must be greater than zero.');
  }

  if (!isFiniteNumber(viewport.maxZoom) || viewport.maxZoom < viewport.minZoom) {
    return reject(
      revision,
      'CFG-021',
      'viewport.maxZoom',
      'Maximum zoom must be greater than or equal to the minimum zoom.',
    );
  }

  if (!isFiniteNumber(viewport.zoomStep) || viewport.zoomStep <= 1) {
    return reject(
      revision,
      'CFG-022',
      'viewport.zoomStep',
      'Zoom step is a multiplier and must be greater than 1.',
    );
  }

  if (!isFiniteNumber(viewport.fitPadding) || viewport.fitPadding < 0) {
    return reject(revision, 'CFG-023', 'viewport.fitPadding', 'Fit padding must not be negative.');
  }

  // Widened deliberately: the value arrives from .NET as JSON, so its declared union type is a
  // claim about the contract rather than a guarantee about this particular payload.
  const wheelMode: string = viewport.wheelMode;
  if (wheelMode !== 'pan' && wheelMode !== 'zoom') {
    return reject(
      revision,
      'CFG-024',
      'viewport.wheelMode',
      `Wheel mode must be 'pan' or 'zoom' but was '${wheelMode}'.`,
    );
  }

  if (!isFiniteNumber(viewport.wheelPanSpeed) || viewport.wheelPanSpeed <= 0) {
    return reject(
      revision,
      'CFG-025',
      'viewport.wheelPanSpeed',
      'Wheel pan speed must be greater than zero.',
    );
  }

  const { keyboard, snapping } = contract;

  if (!isFiniteNumber(keyboard.panStep) || keyboard.panStep <= 0) {
    return reject(revision, 'CFG-030', 'keyboard.panStep', 'Keyboard pan step must be greater than zero.');
  }

  // Aliased before the check: Array.isArray narrows what it is given to `any[]`, so the guard is
  // applied to the property and the typed alias is what the loop below reads.
  const bindings: readonly ShortcutBindingContract[] = keyboard.bindings;

  if (!Array.isArray(keyboard.bindings)) {
    return reject(revision, 'CFG-031', 'keyboard.bindings', 'Keyboard bindings must be an array.');
  }

  for (const [index, binding] of bindings.entries()) {
    if (typeof binding.key !== 'string' || binding.key.length === 0) {
      return reject(
        revision,
        'CFG-032',
        `keyboard.bindings[${String(index)}].key`,
        'A shortcut binding must name a key.',
      );
    }
  }

  if (!isFiniteNumber(snapping.tolerance) || snapping.tolerance < 0) {
    return reject(revision, 'CFG-040', 'snapping.tolerance', 'Snapping tolerance must not be negative.');
  }

  const { editing } = contract;

  const eventEditPolicy: string = editing.eventEditPolicy;
  if (eventEditPolicy !== 'reject' && eventEditPolicy !== 'autoAdjustInterrupting') {
    return reject(
      revision,
      'CFG-053',
      'editing.eventEditPolicy',
      `Event edit policy must be 'reject' or 'autoAdjustInterrupting' but was '${eventEditPolicy}'.`,
    );
  }

  if (!isFiniteNumber(editing.collisionClearance) || editing.collisionClearance < 0) {
    return reject(
      revision,
      'CFG-050',
      'editing.collisionClearance',
      'Collision clearance must not be negative.',
    );
  }

  if (!Number.isInteger(editing.historyDepth) || editing.historyDepth < 1) {
    return reject(revision, 'CFG-051', 'editing.historyDepth', 'History depth must be at least one.');
  }

  // Zero is a legitimate value: it turns the touch long-press off for a host with its own
  // gestures. A negative one is a mistake, and a silently ignored one would be a menu that never
  // opens on a tablet.
  if (!isFiniteNumber(editing.longPressMilliseconds) || editing.longPressMilliseconds < 0) {
    return reject(revision, 'CFG-052', 'editing.longPressMilliseconds', 'Long-press duration cannot be negative.');
  }

  const { routing } = contract;

  /*
   * Widened for the same reason as viewport.wheelMode: the value arrives from .NET as JSON, so its
   * union type is a claim about the contract rather than a guarantee about this payload. Unchecked,
   * anything unrecognised fell through to Manhattan — so a host that asked for straight routing, or
   * a newer .NET side naming a mode this bundle predates, quietly got orthogonal connectors and no
   * indication that its configuration had not been understood.
   */
  const routerMode: string = routing.mode;
  if (routerMode !== 'manhattan' && routerMode !== 'straight') {
    return reject(
      revision,
      'CFG-063',
      'routing.mode',
      `Router mode must be 'manhattan' or 'straight' but was '${routerMode}'.`,
    );
  }

  // A zero or negative step makes the Manhattan router's lattice meaningless and, in the router's
  // own loop, unbounded.
  if (!isFiniteNumber(routing.step) || routing.step <= 0) {
    return reject(revision, 'CFG-060', 'routing.step', 'The router step must be greater than zero.');
  }

  if (!isFiniteNumber(routing.padding) || routing.padding < 0) {
    return reject(revision, 'CFG-061', 'routing.padding', 'Route padding cannot be negative.');
  }

  // Zero is legitimate: it means parallel connectors are left to overlap.
  if (!isFiniteNumber(routing.parallelSeparation) || routing.parallelSeparation < 0) {
    return reject(
      revision,
      'CFG-062',
      'routing.parallelSeparation',
      'Parallel separation cannot be negative.',
    );
  }

  const accepted: AcceptedConfig = {
    contractVersion: contract.contractVersion,
    revision,
    isReadOnly: contract.isReadOnly,
    background: contract.canvas.background,
    grid: Object.freeze({ ...grid }),
    viewport: Object.freeze({ ...viewport }),
    keyboard: Object.freeze({ ...keyboard, bindings: Object.freeze([...bindings]) }),
    snapping: Object.freeze({ ...snapping }),
    editing: Object.freeze({ ...editing }),
    routing: Object.freeze({ ...routing }),
    strings: Object.freeze({ values: Object.freeze({ ...contract.strings.values }) }),
  };

  return { ok: true, config: Object.freeze(accepted) };
}
