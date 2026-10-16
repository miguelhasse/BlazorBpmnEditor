/**
 * Registers the editor's tools with the browser's WebMCP model context and hands every call to .NET.
 *
 * This is the only file that knows the WebMCP API. The descriptors and the results come from .NET
 * unchanged, so a change in the draft changes this file and nothing else.
 */

/** What .NET sends for one tool; the annotations are the editor's own, not WebMCP's. */
export interface ToolDescriptor {
  readonly name: string;
  readonly description: string;
  readonly inputSchema: object;
  readonly annotations: {
    readonly readOnly: boolean;
    readonly consequential: boolean;
    readonly untrustedContent: boolean;
  };
}

/** The part of a `DotNetObjectReference` this module calls. */
export interface DotNetReference {
  invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}

/** A tool the browser refused, for example because the name is already registered. */
export interface RegistrationFailure {
  readonly name: string;
  readonly message: string;
}

/** What `registerAll` reports; plain data, so .NET reads it without a JS handle. */
export interface Registration {
  readonly id: number;
  readonly registered: readonly string[];
  readonly failures: readonly RegistrationFailure[];
}

interface ToolAnnotations {
  readOnlyHint: boolean;
  untrustedContentHint: boolean;
  consequentialHint: boolean;
}

interface ExecuteOptions {
  readonly signal?: AbortSignal;
}

interface ModelContextTool {
  name: string;
  description: string;
  inputSchema: object;
  annotations: ToolAnnotations;
  execute: (input: unknown, options?: ExecuteOptions) => Promise<unknown>;
}

interface ModelContext {
  registerTool(tool: ModelContextTool, options?: { signal: AbortSignal }): Promise<unknown> | undefined;
}

const registrations = new Map<number, AbortController>();
let nextRegistration = 0;
let nextCall = 0;

// The draft puts modelContext on Document; navigator is what earlier previews and polyfills used.
function modelContext(): ModelContext | undefined {
  const fromDocument = (globalThis.document as { modelContext?: ModelContext } | undefined)?.modelContext;
  return fromDocument ?? (globalThis.navigator as { modelContext?: ModelContext } | undefined)?.modelContext;
}

/** Whether this browser offers WebMCP. Feature detection only, never the user agent. */
export function isSupported(): boolean {
  return modelContext() !== undefined;
}

/** The one place the editor's annotations become WebMCP's hints. `exposedTo` is never set: same origin only. */
function hintsOf(descriptor: ToolDescriptor): ToolAnnotations {
  return {
    readOnlyHint: descriptor.annotations.readOnly,
    untrustedContentHint: descriptor.annotations.untrustedContent,
    consequentialHint: descriptor.annotations.consequential,
  };
}

function failure(code: string, message: string): object {
  return { ok: false, code, message };
}

// A rejected promise reaches the agent as an opaque DOMException, so a call never rejects: every
// failure is an ordinary result the agent can read.
async function execute(
  reference: DotNetReference,
  name: string,
  input: unknown,
  options: ExecuteOptions | undefined,
): Promise<unknown> {
  const callId = String(++nextCall);
  const signal = options?.signal;
  const cancel = (): void => {
    reference.invokeMethodAsync('Cancel', callId).catch(() => undefined);
  };

  if (signal?.aborted === true) cancel();
  else signal?.addEventListener('abort', cancel, { once: true });

  try {
    const json = await reference.invokeMethodAsync('ExecuteAsync', callId, name, JSON.stringify(input ?? {}));
    // Return the object, not the string: the browser serializes the fulfilled value itself.
    return typeof json === 'string' ? (JSON.parse(json) as unknown) : failure('INTERNAL-ERROR', 'The editor returned no result.');
  } catch (error) {
    return failure('INTERNAL-ERROR', error instanceof Error ? error.message : 'The editor could not run the tool.');
  } finally {
    signal?.removeEventListener('abort', cancel);
  }
}

/**
 * Registers every tool; one the browser refuses is reported and the rest still register.
 * @param reference The bridge that runs a call.
 * @param descriptors The tools to offer.
 * @returns What registered and what failed, with an id for `unregisterAll`.
 */
export async function registerAll(reference: DotNetReference, descriptors: readonly ToolDescriptor[]): Promise<Registration> {
  const id = ++nextRegistration;
  const controller = new AbortController();
  registrations.set(id, controller);

  const registered: string[] = [];
  const failures: RegistrationFailure[] = [];
  const context = modelContext();

  for (const descriptor of descriptors) {
    if (context === undefined) {
      failures.push({ name: descriptor.name, message: 'WebMCP is not available.' });
      continue;
    }

    try {
      await context.registerTool(
        {
          name: descriptor.name,
          description: descriptor.description,
          inputSchema: descriptor.inputSchema,
          annotations: hintsOf(descriptor),
          execute: (input, options) => execute(reference, descriptor.name, input, options),
        },
        { signal: controller.signal },
      );
      registered.push(descriptor.name);
    } catch (error) {
      failures.push({ name: descriptor.name, message: error instanceof Error ? error.message : String(error) });
    }
  }

  return { id, registered, failures };
}

/**
 * Withdraws every tool a `registerAll` call registered. The draft has no `unregisterTool`: aborting the
 * signal given at registration is how a tool goes away.
 * @param id The registration to withdraw.
 */
export function unregisterAll(id: number): void {
  registrations.get(id)?.abort();
  registrations.delete(id);
}
