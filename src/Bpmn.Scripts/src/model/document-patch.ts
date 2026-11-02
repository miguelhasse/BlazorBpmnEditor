import type { BpmnDocumentContract, BpmnEdgeContract, BpmnShapeContract } from './contracts.js';

type CollectionName = 'flowNodes' | 'sequenceFlows' | 'messageFlows' | 'associations'
  | 'dataAssociations' | 'textAnnotations' | 'groups' | 'dataObjectReferences' | 'dataStoreReferences' | 'lanes' | 'laneSets';

export interface CanvasArrayPatch<T> {
  readonly upserts: readonly T[];
  readonly removals: readonly string[];
  readonly order: readonly string[] | null;
}

type CollectionPatch = {
  [K in CollectionName]: { readonly collection: K; readonly delta: CanvasArrayPatch<BpmnDocumentContract[K][number]> };
}[CollectionName];

export interface CanvasDocumentPatch {
  readonly baseRevision: number;
  readonly revision: number;
  readonly diagramId: string | null;
  readonly collections: readonly CollectionPatch[];
  readonly planes: readonly {
    readonly diagramId: string;
    readonly shapes: CanvasArrayPatch<BpmnShapeContract> | null;
    readonly edges: CanvasArrayPatch<BpmnEdgeContract> | null;
  }[];
}

/** Stages every change before the graph is touched. Null explicitly requests a full resync. */
export function applyDocumentPatch(document: BpmnDocumentContract, patch: CanvasDocumentPatch): BpmnDocumentContract | null {
  let next = document;
  const collections = new Set<CollectionName>();
  for (const change of patch.collections) {
    if (collections.has(change.collection)) return null;
    collections.add(change.collection);
    const updated = applyCollection(next, change);
    if (updated === null) return null;
    next = updated;
  }
  const planes = new Map(patch.planes.map((plane) => [plane.diagramId, plane]));
  if (planes.size !== patch.planes.length) return null;
  if (planes.size === 0) return next;

  const diagrams = [];
  for (const diagram of next.diagrams) {
    const change = planes.get(diagram.id);
    if (change === undefined) { diagrams.push(diagram); continue; }
    planes.delete(diagram.id);
    const shapes = change.shapes === null ? diagram.plane.shapes : applyArray(diagram.plane.shapes, change.shapes);
    const edges = change.edges === null ? diagram.plane.edges : applyArray(diagram.plane.edges, change.edges);
    if (shapes === null || edges === null) return null;
    diagrams.push({ ...diagram, plane: { ...diagram.plane, shapes, edges } });
  }
  return planes.size === 0 ? { ...next, diagrams } : null;
}

function applyCollection(document: BpmnDocumentContract, change: CollectionPatch): BpmnDocumentContract | null {
  switch (change.collection) {
    case 'flowNodes': {
      const items = applyArray(document.flowNodes, change.delta);
      return items === null ? null : { ...document, flowNodes: items };
    }
    case 'sequenceFlows': {
      const items = applyArray(document.sequenceFlows, change.delta);
      return items === null ? null : { ...document, sequenceFlows: items };
    }
    case 'messageFlows': {
      const items = applyArray(document.messageFlows, change.delta);
      return items === null ? null : { ...document, messageFlows: items };
    }
    case 'associations': {
      const items = applyArray(document.associations, change.delta);
      return items === null ? null : { ...document, associations: items };
    }
    case 'dataAssociations': {
      const items = applyArray(document.dataAssociations, change.delta);
      return items === null ? null : { ...document, dataAssociations: items };
    }
    case 'textAnnotations': {
      const items = applyArray(document.textAnnotations, change.delta);
      return items === null ? null : { ...document, textAnnotations: items };
    }
    case 'groups': {
      const items = applyArray(document.groups, change.delta);
      return items === null ? null : { ...document, groups: items };
    }
    case 'dataObjectReferences': {
      const items = applyArray(document.dataObjectReferences, change.delta);
      return items === null ? null : { ...document, dataObjectReferences: items };
    }
    case 'dataStoreReferences': {
      const items = applyArray(document.dataStoreReferences, change.delta);
      return items === null ? null : { ...document, dataStoreReferences: items };
    }
    case 'lanes': {
      const items = applyArray(document.lanes, change.delta);
      return items === null ? null : { ...document, lanes: items };
    }
    case 'laneSets': {
      const items = applyArray(document.laneSets, change.delta);
      return items === null ? null : { ...document, laneSets: items };
    }
    default: return null;
  }
}

function applyArray<T extends { readonly id: string }>(items: readonly T[], patch: CanvasArrayPatch<T>): readonly T[] | null {
  const values = new Map(items.map((item) => [item.id, item]));
  if (values.size !== items.length) return null;
  const removed = new Set(patch.removals);
  if (removed.size !== patch.removals.length) return null;
  for (const id of removed) if (!values.delete(id)) return null;
  const upserted = new Set<string>();
  for (const item of patch.upserts) {
    if (upserted.has(item.id) || removed.has(item.id)) return null;
    upserted.add(item.id);
    values.set(item.id, item);
  }
  if (patch.order === null) return [...values.values()];
  if (patch.order.length !== values.size) return null;
  const result: T[] = [];
  for (const id of patch.order) {
    const value = values.get(id);
    if (value === undefined) return null;
    result.push(value);
    values.delete(id);
  }
  return result;
}
