import type { BpmnFlowNodeContract } from '../model/contracts.js';

/**
 * The element-template icon an applied template left on a flow node, when it is safe to draw.
 *
 * Camunda Modeler persists the icon as `zeebe:modelerTemplateIcon` (Camunda 8) or
 * `camunda:modelerTemplateIcon` (Camunda 7) and draws it in place of the type glyph. This editor
 * does the same, with a stricter policy than Camunda's: only inline `data:image/*` URIs of a
 * raster or SVG type are drawn, so opening a document never makes a network request and a
 * `javascript:` or remote URL in an untrusted file is simply ignored. The icon is drawn through
 * an SVG `<image>`, which renders SVG content in secure static mode, so no sanitizer is needed.
 */
export const TEMPLATE_ICON_ATTRIBUTES: readonly string[] = [
  '{http://camunda.org/schema/zeebe/1.0}modelerTemplateIcon',
  '{http://camunda.org/schema/1.0/bpmn}modelerTemplateIcon',
];

/** Icons above this many characters are ignored rather than inflating every render. */
export const TEMPLATE_ICON_MAX_LENGTH = 64 * 1024;

const SAFE_ICON = /^data:image\/(?:png|jpeg|gif|webp|svg\+xml)[;,]/i;

/** Whether a value is an icon this editor is willing to draw. */
export function isSafeTemplateIcon(value: string | null | undefined): value is string {
  return typeof value === 'string' && value.length <= TEMPLATE_ICON_MAX_LENGTH && SAFE_ICON.test(value.trim());
}

/** The drawable template icon on a flow node, preferring Camunda 8 over Camunda 7, or null. */
export function templateIconOf(node: BpmnFlowNodeContract): string | null {
  const attributes = node.extras?.foreignAttributes;
  if (attributes === undefined) return null;

  for (const name of TEMPLATE_ICON_ATTRIBUTES) {
    const value = attributes[name];
    if (isSafeTemplateIcon(value)) return value.trim();
  }

  return null;
}
