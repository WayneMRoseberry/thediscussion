import type { Statement } from '../api/types';
import { referenceDiscussion } from '../test/fakeApi';
import { buildOutline, countBeneath, deleteQuestion, markerFor } from './outline';

describe('buildOutline', () => {
  it('nests statements under their parents with support before illustrations and challenges', () => {
    const discussion = referenceDiscussion();
    // Shuffle creation order: a counter on the thesis created before the groups still lists after them.
    const statements = [discussion.statements[0], discussion.statements[8], ...discussion.statements.slice(1, 8), discussion.statements[9]];

    const root = buildOutline(statements, discussion.thesisId)!;

    expect(root.statement.id).toBe('t');
    expect(root.children.map((child) => child.statement.id)).toEqual(['g1', 'g2', 'c2']);
    expect(root.children[0].children.map((child) => child.statement.id)).toEqual(['s1', 's2']);
    expect(root.children[0].children[0].children.map((child) => child.statement.id)).toEqual(['c1']);
    expect(root.children[1].children[0].children.map((child) => child.statement.id)).toEqual(['i1']);
    expect(root.children[2].children.map((child) => child.statement.id)).toEqual(['r1']);
  });

  it('returns undefined when the thesis is missing', () => {
    expect(buildOutline([], 'missing')).toBeUndefined();
  });
});

describe('markerFor', () => {
  const base = { id: 'x', parentId: 'p', createdAt: '', editedAt: null };

  it.each([
    [{ ...base, kind: 'supportingStatement' as const, stands: true }, 'standing'],
    [{ ...base, kind: 'supportingStatement' as const, stands: false }, 'countered'],
    [{ ...base, kind: 'counterArgument' as const, refuted: true }, 'refuted'],
    [{ ...base, kind: 'counterArgument' as const, refuted: false }, 'unrefuted'],
    [{ ...base, kind: 'rebuttal' as const, refuted: false }, 'unrefuted'],
    [{ ...base, kind: 'supportGroup' as const, holds: true }, 'holds'],
    [{ ...base, kind: 'supportGroup' as const, holds: false }, 'does not hold'],
    [{ ...base, kind: 'supportGroup' as const }, 'empty'],
  ])('labels %o as %s', (statement, expected) => {
    expect(markerFor(statement)?.text).toBe(expected);
  });

  it('gives illustrations and the thesis no marker', () => {
    expect(markerFor({ ...base, kind: 'illustration' })).toBeUndefined();
    expect(markerFor({ ...base, kind: 'thesis', parentId: null })).toBeUndefined();
  });
});

describe('countBeneath and deleteQuestion', () => {
  const statements: Statement[] = [
    { id: 't', kind: 'thesis', parentId: null, text: 'T', createdAt: '', editedAt: null },
    { id: 'c', kind: 'counterArgument', parentId: 't', text: 'C', createdAt: '', editedAt: null },
    { id: 'r1', kind: 'rebuttal', parentId: 'c', text: 'R1', createdAt: '', editedAt: null },
    { id: 'r2', kind: 'rebuttal', parentId: 'r1', text: 'R2', createdAt: '', editedAt: null },
    { id: 'r3', kind: 'rebuttal', parentId: 'c', text: 'R3', createdAt: '', editedAt: null },
  ];

  it('counts every statement beneath, at any depth', () => {
    expect(countBeneath(statements, 't')).toBe(4);
    expect(countBeneath(statements, 'c')).toBe(3);
    expect(countBeneath(statements, 'r1')).toBe(1);
    expect(countBeneath(statements, 'r3')).toBe(0);
  });

  it('names the kind and how many statements beneath will also go', () => {
    expect(deleteQuestion(statements[1], 3)).toBe('Delete this counter argument and 3 statements beneath it?');
    expect(deleteQuestion(statements[2], 1)).toBe('Delete this rebuttal and 1 statement beneath it?');
    expect(deleteQuestion(statements[4], 0)).toBe('Delete this rebuttal?');
  });
});
