import type { dia } from '@joint/core';

import { findCell } from '../graph/cells.js';
import type { RuleViolation } from '../rules/connection-rules.js';
import { isProvisional } from './provisional.js';

export function isAnnotation(cell: dia.Cell | undefined): boolean {
  return (cell?.get('bpmn') as { elementType?: string } | undefined)?.elementType === 'textAnnotation';
}

export function annotationConnections(graph: dia.Graph, id: string): readonly dia.Link[] {
  return graph.getLinks().filter((link) => !isProvisional(link)
    && (link.get('bpmn') as { elementType?: string } | undefined)?.elementType === 'association'
    && (String(link.source().id) === id || String(link.target().id) === id));
}

/** Cardinality belongs to editor policy, not BPMN endpoint notation rules. */
export function annotationConnectionProblem(
  graph: dia.Graph, sourceId: string, targetId: string, excludedId?: string,
): RuleViolation | null {
  for (const id of [sourceId, targetId]) {
    if (isAnnotation(findCell(graph, id))
      && annotationConnections(graph, id).some((link) => String(link.id) !== excludedId)) {
      return {
        id: 'AN-001', severity: 'error',
        message: 'Editor policy allows one target per Text Annotation. Reconnect its existing Association instead.',
      };
    }
  }
  return null;
}

export function isAnnotationLink(link: dia.Link): boolean {
  return (link.get('bpmn') as { elementType?: string } | undefined)?.elementType === 'association'
    && (isAnnotation(findCell(link.graph, String(link.source().id)))
      || isAnnotation(findCell(link.graph, String(link.target().id))));
}
