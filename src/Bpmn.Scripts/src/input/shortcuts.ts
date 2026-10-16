import type {
  KeyboardOptionsContract,
  ShortcutBindingContract,
  ShortcutCommand,
} from '../interop/contracts.js';

/**
 * Keyboard shortcut matching and dispatch.
 *
 * Bindings arrive from Blazor in the configuration snapshot; nothing is hard-coded here. That is
 * the configuration boundary the specification draws — the host owns public defaults, and a
 * canvas that quietly bound its own keys would fight whatever the host bound.
 */

/** Everything needed to decide whether an event matches, with no DOM dependency. */
export interface KeyStroke {
  readonly key: string;
  readonly ctrlKey: boolean;
  readonly shiftKey: boolean;
  readonly altKey: boolean;
  readonly metaKey: boolean;
}

/**
 * True when a keystroke is exactly this binding.
 *
 * Modifiers must match exactly rather than merely be present: `Ctrl+0` and `Ctrl+Shift+0` are
 * different bindings, and treating the first as a prefix of the second makes one of them
 * unreachable.
 *
 * Printable keys compare case-insensitively, because `Shift+a` reports `key: 'A'` and a host that
 * wrote `key: 'a', shift: true` means the same thing.
 */
export function matches(stroke: KeyStroke, binding: ShortcutBindingContract): boolean {
  const sameKey = binding.key.length === 1
    ? stroke.key.toLowerCase() === binding.key.toLowerCase()
    : stroke.key === binding.key;

  return sameKey
    && stroke.ctrlKey === binding.ctrl
    && stroke.shiftKey === binding.shift
    && stroke.altKey === binding.alt
    && stroke.metaKey === binding.meta;
}

/**
 * The first binding a keystroke matches, or `null`.
 *
 * First rather than only: duplicate bindings are a host configuration mistake, not something to
 * fail initialization over, so the earlier one wins and the canvas stays usable.
 */
export function findBinding(
  stroke: KeyStroke,
  bindings: readonly ShortcutBindingContract[],
): ShortcutBindingContract | null {
  return bindings.find((binding) => matches(stroke, binding)) ?? null;
}

/**
 * True for a target that is consuming the keystroke itself.
 *
 * Without this, typing a `+` into a label editor or a property field would zoom the canvas. The
 * check is on the event target rather than on `document.activeElement` so it stays correct for
 * events that were retargeted out of a shadow root.
 */
export function isTextEntry(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  if (target.isContentEditable) return true;

  const tag = target.tagName;
  return tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT';
}

/**
 * Whether the event came from inside a composite widget that owns its own unmodified keys.
 *
 * A tree, a listbox and a grid are all walked with the arrow keys, Home and End, and by ARIA
 * convention those keys belong to the widget rather than to the page around it. The accessible
 * tree is one, and it sits inside the canvas host: without this, `ArrowRight` matched the pan
 * shortcut and was stopped here before the tree ever saw it. Blazor delegates keydown to the
 * document, so `stopPropagation` on the canvas reaches its handler first — the tree's own keys
 * simply never arrived, and only the ones nothing was bound to (Home, End) worked.
 *
 * Modified combinations are application shortcuts and still apply, so zoom and undo keep working
 * while a reader is in the tree.
 */
export function isInsideCompositeWidget(target: EventTarget | null): boolean {
  return target instanceof HTMLElement
    && target.closest('[role="tree"], [role="listbox"], [role="grid"], [role="menu"]') !== null;
}

/** Whether any modifier is held, which makes a key an application shortcut rather than a widget's. */
function hasModifier(event: KeyboardEvent): boolean {
  return event.ctrlKey || event.metaKey || event.altKey;
}

/** Binds the configured shortcuts to an element and dispatches the commands they name. */
export class ShortcutController {
  readonly #element: HTMLElement;
  readonly #options: KeyboardOptionsContract;
  readonly #dispatch: (command: ShortcutCommand) => void;
  readonly #abort = new AbortController();

  public constructor(
    element: HTMLElement,
    options: KeyboardOptionsContract,
    dispatch: (command: ShortcutCommand) => void,
  ) {
    this.#element = element;
    this.#options = options;
    this.#dispatch = dispatch;

    if (!options.enabled || options.bindings.length === 0) return;

    // Bound to the canvas element, not the window: several editors may share a page, and a
    // shortcut should reach the one the user is actually in.
    this.#element.addEventListener('keydown', this.#handleKeyDown, { signal: this.#abort.signal });
  }

  public destroy(): void {
    this.#abort.abort();
  }

  readonly #handleKeyDown = (event: KeyboardEvent): void => {
    if (isTextEntry(event.target)) return;
    if (!hasModifier(event) && isInsideCompositeWidget(event.target)) return;

    const binding = findBinding(event, this.#options.bindings);
    if (binding === null) return;

    // Only prevented once a binding actually matched, so unbound keys still reach the browser
    // and native behaviour such as tabbing out of the canvas keeps working.
    event.preventDefault();
    event.stopPropagation();
    this.#dispatch(binding.command);
  };
}
