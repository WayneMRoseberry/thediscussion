import type { AddableKind, Statement, StatementKind, SupportGroupType } from '../api/types';

export interface OutlineNode {
  statement: Statement;
  children: OutlineNode[];
}

/**
 * Order of children under one node, matching the outline reference: support first, then illustrations, then
 * challenges. Groups and supporting statements share a rank, so a nested group keeps its place among the supporting
 * statements of its parent group (creation order), as in the reference.
 */
const kindOrder: Record<StatementKind, number> = {
  thesis: 0,
  supportGroup: 1,
  supportingStatement: 1,
  illustration: 3,
  counterArgument: 4,
  rebuttal: 5,
};

/**
 * Builds the argument tree from the API's flat statement list, without recursion, so trees of any depth work.
 * Returns undefined when there is no thesis.
 */
export function buildOutline(statements: Statement[], thesisId: string): OutlineNode | undefined {
  const nodes = new Map<string, OutlineNode>();
  statements.forEach((statement) => nodes.set(statement.id, { statement, children: [] }));

  statements.forEach((statement) => {
    if (statement.parentId) {
      nodes.get(statement.parentId)?.children.push(nodes.get(statement.id)!);
    }
  });

  // Array.prototype.sort is stable, so creation order is kept within each kind.
  nodes.forEach((node) => node.children.sort((a, b) => kindOrder[a.statement.kind] - kindOrder[b.statement.kind]));

  return nodes.get(thesisId);
}

/**
 * How many outline levels render as nested lists. React walks nested elements recursively when it commits them, so a
 * very deep tree of nested lists can overflow the call stack; below this level, an item's descendants render as one
 * flat list instead (see `flattenBeneath`), so the outline has no depth limit.
 */
export const maxNestedLevels = 50;

/**
 * Every node beneath `node`, in outline order (each node followed by its own descendants), with its depth below
 * `node` (1 for a direct child). Iterative, so it works for trees of any depth.
 */
export function flattenBeneath(node: OutlineNode): { node: OutlineNode; depth: number }[] {
  const result: { node: OutlineNode; depth: number }[] = [];
  const pending: { node: OutlineNode; depth: number }[] = [];
  for (let index = node.children.length - 1; index >= 0; index -= 1) {
    pending.push({ node: node.children[index], depth: 1 });
  }
  while (pending.length > 0) {
    const next = pending.pop()!;
    result.push(next);
    for (let index = next.node.children.length - 1; index >= 0; index -= 1) {
      pending.push({ node: next.node.children[index], depth: next.depth + 1 });
    }
  }
  return result;
}

export const kindLabels: Record<StatementKind, string> = {
  thesis: 'THESIS',
  supportGroup: 'SUPPORT GROUP',
  supportingStatement: 'SUPPORT',
  illustration: 'ILLUSTRATION',
  counterArgument: 'COUNTER',
  rebuttal: 'REBUTTAL',
};

export const kindNames: Record<AddableKind, string> = {
  supportGroup: 'support group',
  supportingStatement: 'supporting statement',
  illustration: 'illustration',
  counterArgument: 'counter argument',
  rebuttal: 'rebuttal',
};

export const groupTypeLabels: Record<SupportGroupType, string> = {
  allRequired: 'ALL REQUIRED',
  anySufficient: 'ANY SUFFICIENT',
};

/** Which kinds of statement each addable kind may target (mirrors DiscussionRules on the server). */
export const allowedParentKinds: Record<AddableKind, StatementKind[]> = {
  supportGroup: ['thesis', 'supportGroup', 'supportingStatement'],
  supportingStatement: ['supportGroup'],
  illustration: ['thesis', 'supportingStatement'],
  counterArgument: ['thesis', 'supportingStatement'],
  rebuttal: ['counterArgument', 'rebuttal'],
};

/** The stands/refuted/holds marker shown after a statement, if it has one. */
export function markerFor(statement: Statement): { text: string; tone: 'good' | 'bad' | 'neutral' } | undefined {
  switch (statement.kind) {
    case 'supportingStatement':
      return statement.stands === false ? { text: 'countered', tone: 'bad' } : { text: 'standing', tone: 'good' };
    case 'counterArgument':
    case 'rebuttal':
      return statement.refuted ? { text: 'refuted', tone: 'neutral' } : { text: 'unrefuted', tone: 'bad' };
    case 'supportGroup':
      if (statement.holds === undefined) {
        return { text: 'empty', tone: 'neutral' };
      }
      return statement.holds ? { text: 'holds', tone: 'good' } : { text: 'does not hold', tone: 'bad' };
    default:
      return undefined;
  }
}

/** Shown in place of the name of a group stored before groups had names. */
export const unnamedGroupName = 'Unnamed group';

/** True for a support group without a name (one stored before groups had names). */
export function isUnnamedGroup(statement: Statement): boolean {
  return statement.kind === 'supportGroup' && !statement.name;
}

/** A group's type label and name, as in the outline reference: "ALL REQUIRED: Productivity mechanisms". */
export function groupLabel(statement: Statement): string {
  const type = statement.groupType ? groupTypeLabels[statement.groupType] : 'SUPPORT GROUP';
  return `${type}: ${statement.name || unnamedGroupName}`;
}

/** Short label for a statement, used in target pickers. */
export function describeStatement(statement: Statement): string {
  if (statement.kind === 'supportGroup') {
    return groupLabel(statement);
  }
  const text = statement.text ?? '';
  const short = text.length > 80 ? `${text.slice(0, 77)}...` : text;
  return `[${kindLabels[statement.kind]}] ${short}`;
}

/** Lowercase name of every kind, for messages and accessible labels. */
export const statementNames: Record<StatementKind, string> = {
  thesis: 'thesis',
  ...kindNames,
};

/** How many statements sit beneath a statement, at any depth (they are deleted with it). */
export function countBeneath(statements: Statement[], id: string): number {
  const childrenOf = new Map<string, string[]>();
  statements.forEach((statement) => {
    if (statement.parentId) {
      const siblings = childrenOf.get(statement.parentId);
      if (siblings) {
        siblings.push(statement.id);
      } else {
        childrenOf.set(statement.parentId, [statement.id]);
      }
    }
  });

  let count = 0;
  const pending = [...(childrenOf.get(id) ?? [])];
  while (pending.length > 0) {
    const next = pending.pop()!;
    count += 1;
    childrenOf.get(next)?.forEach((child) => pending.push(child));
  }
  return count;
}

/** "1 statement", "3 statements". */
export function statementCount(count: number): string {
  return `${count} ${count === 1 ? 'statement' : 'statements'}`;
}

/** The delete confirmation question, naming how many statements beneath the target also go. */
export function deleteQuestion(statement: Statement, beneath: number): string {
  const name = statementNames[statement.kind];
  return beneath === 0 ? `Delete this ${name}?` : `Delete this ${name} and ${statementCount(beneath)} beneath it?`;
}
