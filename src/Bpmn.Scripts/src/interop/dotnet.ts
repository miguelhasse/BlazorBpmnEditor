/**
 * The only part of the Blazor JS interop surface this module depends on.
 *
 * Declared structurally rather than by importing `@microsoft/dotnet-js-interop`, so the
 * bundle carries no Blazor runtime dependency and the event bridge stays trivially
 * fakeable in Node tests.
 */
export interface DotNetObjectRef {
  invokeMethodAsync<TResult = void>(methodName: string, ...args: readonly unknown[]): Promise<TResult>;
}
