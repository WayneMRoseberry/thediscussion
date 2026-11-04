import { referenceDiscussion } from '../test/fakeApi';
import { buildOutline, markerFor } from './outline';

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
  const base = { id: 'x', parentId: 'p', createdAt: '' };

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
