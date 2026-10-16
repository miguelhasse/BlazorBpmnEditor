/** Elements that keep the browser's own menu, because cut, copy and paste are what it is for. */
const EDITABLE = 'input, textarea, select, [contenteditable=""], [contenteditable="true"]';

/**
 * Suppresses the browser's context menu, and native drag-and-drop of the editor's own content,
 * across the whole editor: canvas, toolbar, palette and panels.
 *
 * The canvas has its own menu handler (`ContextMenuController`) that also opens the editor's menu;
 * this covers the chrome around it, where there is nothing to open. Editable fields are the one
 * exception. A stencil tool's drag is left alone: it is the editor's own drag-and-drop.
 *
 * @param root The element that contains the editor.
 * @returns A function that removes both listeners.
 */
export function suppressNativeMenu(root: HTMLElement): () => void {
  const abort = new AbortController();

  root.addEventListener('contextmenu', (event) => {
    if (event.target instanceof Element && event.target.closest(EDITABLE) !== null) return;
    event.preventDefault();
  }, { signal: abort.signal });

  root.addEventListener('dragstart', (event) => {
    if (event.target instanceof Element && event.target.closest('.bpmn-stencil__tool') !== null) return;
    event.preventDefault();
  }, { signal: abort.signal });

  return () => { abort.abort(); };
}
