/** A BPMN file the user dropped on the canvas. */
export interface DroppedFile {
  readonly fileName: string;
  /** The file's text: BPMN XML or BPMN JSON; the editor tells them apart. */
  readonly xml: string;
}

/**
 * Accepts a BPMN file (`.bpmn`, `.xml` or `.json`) dropped onto the canvas.
 *
 * The canvas already owns dropping — that is how the stencil creates elements — so a file dropped
 * on it is the same gesture with a different payload, and handling it anywhere else would mean two
 * drop targets fighting over the same pointer.
 *
 * Reading the file is all this does. What the file contains, whether it is worth opening and what
 * happens to the document on screen are questions for the editor, which is where the document
 * lives; a drop that silently replaced the open file would be the worst possible answer to an
 * accidental one.
 */
export class FileDropTarget {
  static readonly #Accepted = /\.(bpmn|xml|json)$/i;
  static readonly #ActiveClass = 'bpmn-canvas--file-over';

  readonly #root: HTMLElement;
  readonly #onFile: (file: DroppedFile) => void;
  #attached = false;

  /**
   * @param root The canvas root the drop is made on.
   * @param onFile Called once the file has been read.
   */
  public constructor(root: HTMLElement, onFile: (file: DroppedFile) => void) {
    this.#root = root;
    this.#onFile = onFile;
  }

  public attach(): void {
    if (this.#attached) return;

    this.#root.addEventListener('dragover', this.#handleDragOver);
    this.#root.addEventListener('dragleave', this.#handleDragLeave);
    this.#root.addEventListener('drop', this.#handleDrop);
    this.#attached = true;
  }

  public detach(): void {
    if (!this.#attached) return;

    this.#root.removeEventListener('dragover', this.#handleDragOver);
    this.#root.removeEventListener('dragleave', this.#handleDragLeave);
    this.#root.removeEventListener('drop', this.#handleDrop);
    this.#root.classList.remove(FileDropTarget.#ActiveClass);
    this.#attached = false;
  }

  readonly #handleDragOver = (event: DragEvent): void => {
    if (!FileDropTarget.#carriesFile(event)) return;

    // Without this the browser navigates to the file, which loses the whole application.
    event.preventDefault();
    if (event.dataTransfer !== null) event.dataTransfer.dropEffect = 'copy';

    this.#root.classList.add(FileDropTarget.#ActiveClass);
  };

  readonly #handleDragLeave = (event: DragEvent): void => {
    // Only when the pointer has actually left the canvas: dragleave also fires on every child it
    // passes over, and reacting to those makes the highlight flicker for the whole drag.
    if (event.relatedTarget instanceof Node && this.#root.contains(event.relatedTarget)) return;

    this.#root.classList.remove(FileDropTarget.#ActiveClass);
  };

  readonly #handleDrop = (event: DragEvent): void => {
    if (!FileDropTarget.#carriesFile(event)) return;

    event.preventDefault();
    this.#root.classList.remove(FileDropTarget.#ActiveClass);

    const file = event.dataTransfer?.files.item(0);
    if (file === null || file === undefined) return;

    // One file. A drop carrying several is ambiguous about which document to open, and opening the
    // first of them silently is a guess.
    void file.text().then((xml) => {
      this.#onFile({ fileName: file.name, xml });
    });
  };

  /**
   * Whether a drag is carrying a file this canvas would take.
   *
   * The name is not readable during `dragover` — the browser only exposes types until the drop — so
   * the type check is what decides whether to offer, and the extension is checked on the drop.
   */
  static #carriesFile(event: DragEvent): boolean {
    const transfer = event.dataTransfer;
    if (transfer === null) return false;

    if (event.type === 'drop') {
      const file = transfer.files.item(0);

      return file !== null && FileDropTarget.#Accepted.test(file.name);
    }

    return [...transfer.types].includes('Files');
  }
}
