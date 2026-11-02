import { describe, expect, it } from 'vitest';

import { applyDocumentPatch, type CanvasDocumentPatch } from '../../src/model/document-patch.js';
import { flowNode, linearProcess, shape } from '../fixtures/documents.js';

const patch = (changes: Partial<CanvasDocumentPatch> = {}): CanvasDocumentPatch => ({
  baseRevision: 0, revision: 1, diagramId: null, collections: [], planes: [], ...changes,
});

describe('resident document patches', () => {
  it('replaces only changed records and preserves explicit nulls', () => {
    const before = linearProcess();
    const node = { ...before.flowNodes[1]!, name: null };
    const next = applyDocumentPatch(before, patch({
      collections: [{ collection: 'flowNodes', delta: { upserts: [node], removals: [], order: null } }],
    }))!;

    expect(next.flowNodes[1]).toBe(node);
    expect(next.flowNodes[1]!.name).toBeNull();
    expect(next.flowNodes[0]).toBe(before.flowNodes[0]);
    expect(next.diagrams).toBe(before.diagrams);
    expect(before.flowNodes[1]!.name).toBe('Check stock');
  });

  it('applies create, delete and explicit order atomically across semantics and DI', () => {
    const before = linearProcess();
    const added = flowNode('New', 'userTask');
    const addedShape = shape('New', 0, 0, 100, 80);
    const next = applyDocumentPatch(before, patch({
      collections: [{
        collection: 'flowNodes',
        delta: { upserts: [added], removals: ['Task_1'], order: ['New', 'EndEvent_1', 'StartEvent_1'] },
      }],
      planes: [{
        diagramId: 'Diagram_1',
        shapes: { upserts: [addedShape], removals: ['Task_1_di'], order: ['New_di', 'EndEvent_1_di', 'StartEvent_1_di'] },
        edges: null,
      }],
    }))!;

    expect(next.flowNodes.map((node) => node.id)).toEqual(['New', 'EndEvent_1', 'StartEvent_1']);
    expect(next.diagrams[0]!.plane.shapes[0]).toBe(addedShape);
    expect(before.flowNodes).toHaveLength(3);
    expect(before.diagrams[0]!.plane.shapes[1]!.bpmnElement).toBe('Task_1');
  });

  it.each([
    { upserts: [], removals: ['Missing'], order: null },
    { upserts: [], removals: ['Task_1', 'Task_1'], order: null },
    { upserts: [flowNode('Task_1', 'task')], removals: ['Task_1'], order: null },
    { upserts: [flowNode('New', 'task'), flowNode('New', 'task')], removals: [], order: null },
    { upserts: [], removals: [], order: ['Task_1', 'Task_1', 'StartEvent_1'] },
    { upserts: [], removals: [], order: ['Task_1'] },
    { upserts: [], removals: [], order: ['Task_1', 'Missing', 'StartEvent_1'] },
  ])('rejects invalid array operations without mutating the base: %j', (delta) => {
    const before = linearProcess();
    const snapshot = structuredClone(before);
    expect(applyDocumentPatch(before, patch({ collections: [{ collection: 'flowNodes', delta }] }))).toBeNull();
    expect(before).toEqual(snapshot);
  });

  it('rejects unknown or duplicate planes, including after staging semantic changes', () => {
    const before = linearProcess();
    const change = { diagramId: 'Missing', shapes: null, edges: null };
    expect(applyDocumentPatch(before, patch({ planes: [change] }))).toBeNull();
    expect(applyDocumentPatch(before, patch({ planes: [change, change] }))).toBeNull();
    const delta = { collection: 'flowNodes' as const, delta: { upserts: [], removals: ['Task_1'], order: null } };
    expect(applyDocumentPatch(before, patch({ collections: [delta, delta] }))).toBeNull();
    expect(applyDocumentPatch(before, patch({ collections: [delta], planes: [change] }))).toBeNull();
    expect(before.flowNodes).toHaveLength(3);
  });
});
