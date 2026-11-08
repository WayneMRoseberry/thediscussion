import type { Statement } from '../api/types';
import { deepGroupDiscussion, nestedDiscussion, referenceDiscussion } from '../test/fakeApi';
import {
  allowedParentKinds,
  buildOutline,
  countBeneath,
  deleteQuestion,
  describeStatement,
  flattenBeneath,
  groupLabel,
  isUnnamedGroup,
  markerFor,
} from './outline';

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

// AC-4 (UC-4 main flow steps 1 to 3; Cluster B "UI nesting shown", "Migrated null-name group", "Add-form target kind").
describe('nested and named groups', () => {
  function childIds(node: { children: { statement: Statement }[] }): string[] {
    return node.children.map((child) => child.statement.id);
  }

  it('nests groups under the thesis, under groups and under supporting statements, keeping creation order among supports', () => {
    const discussion = nestedDiscussion();

    const root = buildOutline(discussion.statements, discussion.thesisId)!;

    expect(childIds(root)).toEqual(['g1', 'g5']);
    const g1 = root.children[0];
    // A nested group keeps its place among the supporting statements of its parent group, as in the reference.
    expect(childIds(g1)).toEqual(['s1', 'g2', 's4']);
    const g2 = g1.children[1];
    expect(childIds(g2)).toEqual(['s2', 's3', 'g3']);
    // Sub-argument: a group under a supporting statement.
    expect(childIds(g1.children[2])).toEqual(['g4']);
    expect(childIds(g1.children[2].children[0])).toEqual(['s5']);
  });

  it('puts a sub-argument group before the illustrations and challenges of its supporting statement', () => {
    const statements: Statement[] = [
      { id: 't', kind: 'thesis', parentId: null, text: 'T', createdAt: '', editedAt: null },
      { id: 'g', kind: 'supportGroup', parentId: 't', groupType: 'allRequired', name: 'G', createdAt: '', editedAt: null },
      { id: 's', kind: 'supportingStatement', parentId: 'g', text: 'S', createdAt: '', editedAt: null },
      { id: 'c', kind: 'counterArgument', parentId: 's', text: 'C', createdAt: '', editedAt: null },
      { id: 'i', kind: 'illustration', parentId: 's', text: 'I', createdAt: '', editedAt: null },
      { id: 'sub', kind: 'supportGroup', parentId: 's', groupType: 'anySufficient', name: 'Sub', createdAt: '', editedAt: null },
    ];

    const support = buildOutline(statements, 't')!.children[0].children[0];

    expect(childIds(support)).toEqual(['sub', 'i', 'c']);
  });

  it('builds a 1,000-level chain of groups without overflowing', () => {
    const discussion = deepGroupDiscussion(1000);

    let node = buildOutline(discussion.statements, discussion.thesisId);
    let depth = 0;
    while (node && node.children.length > 0) {
      expect(node.children).toHaveLength(1);
      node = node.children[0];
      depth += 1;
    }

    expect(depth).toBe(1000);
    expect(node?.statement.id).toBe('g1000');
    expect(countBeneath(discussion.statements, 't')).toBe(1000);
    expect(countBeneath(discussion.statements, 'g500')).toBe(500);
  });

  it('labels a group with its type and name, and an unnamed group as "Unnamed group"', () => {
    const [, g1, , g2, , , , , , , , g5] = nestedDiscussion().statements;

    expect(groupLabel(g1)).toBe('ALL REQUIRED: Productivity mechanisms');
    expect(groupLabel(g2)).toBe('ANY SUFFICIENT: Evidence');
    expect(groupLabel(g5)).toBe('ANY SUFFICIENT: Unnamed group');
    expect(describeStatement(g1)).toBe('ALL REQUIRED: Productivity mechanisms');
    expect(isUnnamedGroup(g5)).toBe(true);
    expect(isUnnamedGroup(g1)).toBe(false);
    expect(isUnnamedGroup(nestedDiscussion().statements[0])).toBe(false);
  });

  it('lets a group target the thesis, a group or a supporting statement, and nothing else', () => {
    expect(allowedParentKinds.supportGroup).toEqual(['thesis', 'supportGroup', 'supportingStatement']);
    // Unchanged: counters still cannot target groups, and illustrations cannot be countered.
    expect(allowedParentKinds.counterArgument).toEqual(['thesis', 'supportingStatement']);
  });
});

describe('flattenBeneath', () => {
  it('lists every node beneath in outline order with its depth', () => {
    const discussion = nestedDiscussion();
    const root = buildOutline(discussion.statements, discussion.thesisId)!;

    const flat = flattenBeneath(root).map(({ node, depth }) => `${node.statement.id}:${depth}`);

    expect(flat).toEqual(['g1:1', 's1:2', 'g2:2', 's2:3', 's3:3', 'c1:4', 'g3:3', 's4:2', 'g4:3', 's5:4', 'g5:1', 's6:2', 'c2:3']);
  });

  it('flattens a 1,000-level chain without overflowing', () => {
    const discussion = deepGroupDiscussion(1000);
    const flat = flattenBeneath(buildOutline(discussion.statements, discussion.thesisId)!);

    expect(flat).toHaveLength(1000);
    expect(flat[999].node.statement.id).toBe('g1000');
    expect(flat[999].depth).toBe(1000);
  });
});
