import type { CanvasStringsContract } from '../interop/contracts.js';

/** Replaces .NET-style resource placeholders without interpreting document text as markup. */
export function canvasText(strings: CanvasStringsContract, key: string, ...values: readonly (string | number)[]): string {
  return (strings.values[key] ?? key).replace(/\{(\d+)\}/g, (placeholder: string, index: string) =>
    values[Number(index)]?.toString() ?? placeholder);
}
