import type { AddableKind, Statement, StatementKind, SupportGroupType } from '../api/types';

export interface OutlineNode {
  statement: Statement;
  children: OutlineNode[];
}

/** Order of children under one node, matching the outline reference: support first, then illustrations, then challenges. */
const kindOrder: Record<StatementKind, number> = {
  thesis: 0,
  supportGroup: 1,
  supportingStatement: 2,
  illustration: 3,
  counterArgument: 4,
  rebuttal: 5,
};

/** Builds the argument tree from the API's flat statement list. Returns undefined when there is no thesis. */
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
  supportGroup: ['thesis'],
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

/** Short label for a statement, used in target pickers. */
export function describeStatement(statement: Statement): string {
  if (statement.kind === 'supportGroup') {
    return `[SUPPORT GROUP] ${statement.groupType ? groupTypeLabels[statement.groupType] : ''}`.trim();
  }
  const text = statement.text ?? '';
  const short = text.length > 80 ? `${text.slice(0, 77)}...` : text;
  return `[${kindLabels[statement.kind]}] ${short}`;
}
