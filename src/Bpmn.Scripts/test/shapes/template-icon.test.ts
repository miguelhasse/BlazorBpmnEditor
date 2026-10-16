import { describe, expect, it } from 'vitest';

import { TableFontMetrics } from '../../src/labels/font-metrics.js';
import { createFlowNodeCell, TEMPLATE_REPLACED_CLASS } from '../../src/shapes/registry.js';
import { isSafeTemplateIcon, templateIconOf, TEMPLATE_ICON_MAX_LENGTH } from '../../src/shapes/template-icon.js';
import type { BpmnFlowNodeContract } from '../../src/model/contracts.js';
import { flowNode, shape } from '../fixtures/documents.js';

/**
 * Element-template icons drawn in place of the type glyph, the way Camunda Modeler's
 * element-template-icon-renderer does, but only for inline data URIs.
 */
const context = { metrics: new TableFontMetrics() };

const ZEEBE = '{http://camunda.org/schema/zeebe/1.0}modelerTemplateIcon';
const CAMUNDA = '{http://camunda.org/schema/1.0/bpmn}modelerTemplateIcon';
const SVG = "data:image/svg+xml,%3Csvg xmlns='http://www.w3.org/2000/svg'/%3E";
const PNG = 'data:image/png;base64,iVBORw0KGgo=';

const withIcons = (attributes: Record<string, string>, type: BpmnFlowNodeContract['type'] = 'serviceTask') =>
  flowNode('Node_1', type, { extras: { documentation: null, foreignAttributes: attributes, extensions: [] } });

describe('templateIconOf', () => {
  it('reads the Camunda 8 attribute', () => {
    expect(templateIconOf(withIcons({ [ZEEBE]: SVG }))).toBe(SVG);
  });

  it('reads the Camunda 7 attribute', () => {
    expect(templateIconOf(withIcons({ [CAMUNDA]: PNG }))).toBe(PNG);
  });

  it('prefers Camunda 8 when both are present', () => {
    expect(templateIconOf(withIcons({ [CAMUNDA]: PNG, [ZEEBE]: SVG }))).toBe(SVG);
  });

  it('is null without extras', () => {
    expect(templateIconOf(flowNode('Node_1', 'task'))).toBeNull();
  });

  it.each([
    'https://example.com/icon.svg',
    'javascript:alert(1)',
    'data:text/html,<script>alert(1)</script>',
    'data:image/x-icon;base64,AAA=',
    '',
  ])('refuses %s', (value) => {
    expect(isSafeTemplateIcon(value)).toBe(false);
    expect(templateIconOf(withIcons({ [ZEEBE]: value }))).toBeNull();
  });

  it('refuses an icon above the size cap', () => {
    const huge = `data:image/png;base64,${'A'.repeat(TEMPLATE_ICON_MAX_LENGTH)}`;
    expect(templateIconOf(withIcons({ [ZEEBE]: huge }))).toBeNull();
  });
});

interface IconAttrs {
  readonly display?: string;
  readonly href?: string | null;
  readonly x?: number;
  readonly y?: number;
  readonly width?: number;
  readonly class?: string;
}

describe('an Activity with a template icon', () => {
  const cell = createFlowNodeCell(withIcons({ [ZEEBE]: SVG }), shape('Node_1', 0, 0, 100, 80), context);

  it('draws the icon in the type-glyph corner', () => {
    const icon = cell.attr('templateIcon') as IconAttrs;
    expect(icon.display).toBe('inline');
    expect(icon.href).toBe(SVG);
    expect([icon.x, icon.y, icon.width]).toEqual([8, 8, 18]);
  });

  it('marks the type glyph as replaced rather than deleting it', () => {
    const glyph = cell.attr('typeIcon') as IconAttrs;
    expect(glyph.class).toContain(TEMPLATE_REPLACED_CLASS);
    expect(glyph.display).toBe('inline');
  });
});

describe('an Activity without a template icon', () => {
  const cell = createFlowNodeCell(flowNode('Node_1', 'serviceTask'), shape('Node_1', 0, 0, 100, 80), context);

  it('hides the image and keeps the glyph plain', () => {
    expect((cell.attr('templateIcon') as IconAttrs).display).toBe('none');
    expect((cell.attr('typeIcon') as IconAttrs).class).toBe('bpmn-activity-icon');
  });
});

describe('an Event with a template icon', () => {
  const cell = createFlowNodeCell(
    withIcons({ [ZEEBE]: SVG }, 'intermediateThrowEvent'),
    shape('Node_1', 0, 0, 36, 36),
    context,
  );

  it('centres the icon', () => {
    const icon = cell.attr('templateIcon') as IconAttrs;
    expect(icon.display).toBe('inline');
    expect([icon.x, icon.y, icon.width]).toEqual([9, 9, 18]);
  });

  it('marks the trigger glyph as replaced', () => {
    expect((cell.attr('icon') as IconAttrs).class).toContain(TEMPLATE_REPLACED_CLASS);
  });
});
