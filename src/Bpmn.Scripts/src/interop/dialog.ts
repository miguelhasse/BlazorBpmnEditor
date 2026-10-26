interface DialogOwner {
  invokeMethodAsync(method: 'DismissAsync'): Promise<void>;
}

const dialogs = new WeakMap<HTMLDialogElement, AbortController>();

/** Opens a modal independently of any canvas instance. Native dialog owns focus containment. */
export function showDialog(element: HTMLDialogElement, owner: DialogOwner, dismissOnBackdrop: boolean): void {
  if (element.open) return;
  dialogs.get(element)?.abort();
  const controller = new AbortController();
  dialogs.set(element, controller);
  const options = { signal: controller.signal };
  const dismiss = (): void => {
    closeDialog(element);
    void owner.invokeMethodAsync('DismissAsync').catch((error: unknown) => {
      console.error('BPMN dialog dismissal failed.', error);
    });
  };
  element.addEventListener('cancel', (event) => {
    event.preventDefault();
    event.stopPropagation();
    dismiss();
  }, options);
  element.addEventListener('close', () => {
    if (!element.open) dismiss();
  }, options);
  let backdropPressed = false;
  element.addEventListener('pointerdown', (event) => {
    backdropPressed = event.target === element && outside(element, event);
  }, options);
  element.addEventListener('click', (event) => {
    if (dismissOnBackdrop && backdropPressed && event.target === element && outside(element, event)) dismiss();
    backdropPressed = false;
  }, options);
  element.showModal();
}

/** Releases modal focus; a disposed Blazor host may already have been removed. */
export function closeDialog(element: HTMLDialogElement | null): void {
  if (element === null) return;
  dialogs.get(element)?.abort();
  dialogs.delete(element);
  if (element.open) element.close();
}

function outside(element: HTMLDialogElement, event: MouseEvent): boolean {
  const bounds = element.getBoundingClientRect();
  return event.clientX < bounds.left || event.clientX > bounds.right
    || event.clientY < bounds.top || event.clientY > bounds.bottom;
}
