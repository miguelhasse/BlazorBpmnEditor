// @vitest-environment jsdom
import { dia } from '@joint/core';
import { describe, expect, it } from 'vitest';

import { annotationConnectionProblem } from '../../src/editing/annotation-policy.js';
import { PROVISIONAL_FLAG } from '../../src/editing/provisional.js';

function fixture(reversed = false) {
  const graph = new dia.Graph();
  const note = new dia.Element({ type: 'test.Element', id: 'Note', bpmn: { elementType: 'textAnnotation' } });
  const target = new dia.Element({ type: 'test.Element', id: 'A', bpmn: { elementType: 'task' } });
  const other = new dia.Link({ type: 'test.Link', id: 'Flow', bpmn: { elementType: 'sequenceFlow' } });
  const association = new dia.Link({
    [PROVISIONAL_FLAG]: false,
    type: 'test.Link', id: 'Association', bpmn: { elementType: 'association' },
    source: { id: reversed ? 'A' : 'Note' }, target: { id: reversed ? 'Note' : 'A' },
  });
  graph.addCells([note, target, other, association]);
  return { graph, association };
}

describe('annotation editor policy', () => {
  it.each([false, true])('rejects a second binding in either orientation (%s)', (reversed) => {
    const { graph } = fixture(reversed);
    expect(annotationConnectionProblem(graph, 'Note', 'Flow')?.id).toBe('AN-001');
    expect(annotationConnectionProblem(graph, 'Flow', 'Note')?.id).toBe('AN-001');
    expect(annotationConnectionProblem(graph, 'Note', 'A')?.id).toBe('AN-001');
  });

  it('excludes the currently reconnected Association', () => {
    const { graph } = fixture();
    expect(annotationConnectionProblem(graph, 'Note', 'Flow', 'Association')).toBeNull();
  });

  it('ignores provisional links and detaches after deletion', () => {
    const { graph, association } = fixture();
    association.set({ [PROVISIONAL_FLAG]: true });
    expect(annotationConnectionProblem(graph, 'Note', 'Flow')).toBeNull();
    association.remove();
    expect(annotationConnectionProblem(graph, 'Note', 'Flow')).toBeNull();
  });

  it('allows several notes on a shared target and leaves ordinary connectors alone', () => {
    const { graph } = fixture();
    graph.addCell(new dia.Element({ type: 'test.Element', id: 'OtherNote', bpmn: { elementType: 'textAnnotation' } }));
    expect(annotationConnectionProblem(graph, 'OtherNote', 'A')).toBeNull();
    expect(annotationConnectionProblem(graph, 'A', 'Flow')).toBeNull();
  });
});
