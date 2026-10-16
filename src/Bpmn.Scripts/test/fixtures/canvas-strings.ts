import resources from '../../../Bpmn/Resources/BpmnCanvasStrings.resx?raw';

import type { CanvasStringsContract } from '../../src/interop/contracts.js';

/** Neutral display resources, not a competing set of script-side English defaults. */
export const canvasStrings: CanvasStringsContract = {
  values: Object.fromEntries([...resources.matchAll(/<data name="([^"]+)"[^>]*><value>([^<]*)<\/value>/g)]
    .map((match) => [match[1] ?? '', match[2] ?? ''] as const)),
};
