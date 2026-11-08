import { vi } from 'vitest';
import type { Discussion, Statement } from '../api/types';

export interface FakeResponse {
  status: number;
  body?: unknown;
}

export type Handler = (request: { method: string; path: string; body: unknown }) => FakeResponse | Promise<FakeResponse>;

/**
 * Replaces global fetch with a handler-driven fake of the REST API, so components run against
 * the real API client. Returns the mock for call assertions.
 */
export function mockApi(handler: Handler) {
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const url = typeof input === 'string' ? input : input instanceof URL ? input.toString() : input.url;
    const method = (init?.method ?? 'GET').toUpperCase();
    const body = typeof init?.body === 'string' ? JSON.parse(init.body) : undefined;
    const response = await handler({ method, path: url, body });
    const isProblem = response.status >= 400;
    return new Response(response.body === undefined ? null : JSON.stringify(response.body), {
      status: response.status,
      headers: { 'Content-Type': isProblem ? 'application/problem+json' : 'application/json' },
    });
  });
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

/** Simulates the server being unreachable. */
export function networkFailure(): never {
  throw new TypeError('Failed to fetch');
}

export function validationProblem(errors: Record<string, string[]>): FakeResponse {
  return {
    status: 400,
    body: { type: 'https://tools.ietf.org/html/rfc9110#section-15.5.1', title: 'The request is invalid.', status: 400, errors },
  };
}

export const notFoundProblem: FakeResponse = {
  status: 404,
  body: { title: 'Discussion not found', status: 404, detail: 'No discussion with that id exists.' },
};

const created = '2026-11-03T12:00:00.000000+00:00';

function statement(partial: Partial<Statement> & Pick<Statement, 'id' | 'kind'>): Statement {
  return { parentId: null, createdAt: created, editedAt: null, ...partial };
}

/**
 * The outline reference discussion: two groups, supports, an illustration, counters and a rebuttal.
 * Status values follow the AC-4 rules (the counter on "Fewer interruptions" is unrefuted, so it is countered).
 */
export function referenceDiscussion(): Discussion {
  return {
    id: 'd1',
    title: 'Remote work boosts productivity',
    topicTags: ['work.remote'],
    createdAt: created,
    status: 'countered',
    thesisId: 't',
    statements: [
      statement({ id: 't', kind: 'thesis', text: 'Remote work boosts productivity' }),
      statement({ id: 'g1', kind: 'supportGroup', parentId: 't', groupType: 'allRequired', name: 'Productivity mechanisms', holds: false }),
      statement({ id: 's1', kind: 'supportingStatement', parentId: 'g1', text: 'Fewer interruptions', stands: false }),
      statement({ id: 'c1', kind: 'counterArgument', parentId: 's1', text: 'Slack pings replace them', refuted: false }),
      statement({ id: 's2', kind: 'supportingStatement', parentId: 'g1', text: 'No commute saves energy', stands: true }),
      statement({ id: 'g2', kind: 'supportGroup', parentId: 't', groupType: 'anySufficient', name: 'Evidence', holds: true }),
      statement({ id: 's3', kind: 'supportingStatement', parentId: 'g2', text: 'Studies show +13% output', stands: true }),
      statement({ id: 'i1', kind: 'illustration', parentId: 's3', text: 'e.g. my team shipped early' }),
      statement({ id: 'c2', kind: 'counterArgument', parentId: 't', text: 'Collaboration suffers', refuted: true }),
      statement({ id: 'r1', kind: 'rebuttal', parentId: 'c2', text: 'Async docs offset this', refuted: false }),
    ],
  };
}

/**
 * Nested and named groups, following the AC-4 outline reference. Three levels of groups (g1 > g2 > g3), a sub-argument
 * group under a supporting statement (g4 under s4), and a group stored before groups had names (g5, `name: null`).
 * Markers: g1, g2 and g4 hold, g5 does not hold, and g3 is empty (no `holds`).
 *
 * ```
 * [THESIS] Remote work boosts productivity
 *   + ALL REQUIRED: Productivity mechanisms             g1
 *       [SUPPORT] Fewer interruptions                   s1
 *       + ANY SUFFICIENT: Evidence                      g2
 *           [SUPPORT] Studies show +13% output          s2
 *           [SUPPORT] Survey of 500 managers            s3
 *              x [COUNTER] Self-reported data           c1
 *           + ALL REQUIRED: Replicated studies          g3 (empty)
 *       [SUPPORT] No commute saves energy               s4
 *          + ALL REQUIRED: Why commuting drains energy  g4
 *              [SUPPORT] Average commute is 54 minutes  s5
 *   + ANY SUFFICIENT: Unnamed group  [Add a name]       g5
 *       [SUPPORT] Flexible hours                        s6
 *          x [COUNTER] Hours blur into evenings         c2
 * ```
 */
export function nestedDiscussion(): Discussion {
  return {
    id: 'd1',
    title: 'Remote work boosts productivity',
    topicTags: ['work.remote'],
    createdAt: created,
    status: 'tested',
    thesisId: 't',
    statements: [
      statement({ id: 't', kind: 'thesis', text: 'Remote work boosts productivity' }),
      statement({ id: 'g1', kind: 'supportGroup', parentId: 't', groupType: 'allRequired', name: 'Productivity mechanisms', holds: true }),
      statement({ id: 's1', kind: 'supportingStatement', parentId: 'g1', text: 'Fewer interruptions', stands: true }),
      statement({ id: 'g2', kind: 'supportGroup', parentId: 'g1', groupType: 'anySufficient', name: 'Evidence', holds: true }),
      statement({ id: 's2', kind: 'supportingStatement', parentId: 'g2', text: 'Studies show +13% output', stands: true }),
      statement({ id: 's3', kind: 'supportingStatement', parentId: 'g2', text: 'Survey of 500 managers', stands: false }),
      statement({ id: 'c1', kind: 'counterArgument', parentId: 's3', text: 'Self-reported data', refuted: false }),
      statement({ id: 'g3', kind: 'supportGroup', parentId: 'g2', groupType: 'allRequired', name: 'Replicated studies' }),
      statement({ id: 's4', kind: 'supportingStatement', parentId: 'g1', text: 'No commute saves energy', stands: true }),
      statement({ id: 'g4', kind: 'supportGroup', parentId: 's4', groupType: 'allRequired', name: 'Why commuting drains energy', holds: true }),
      statement({ id: 's5', kind: 'supportingStatement', parentId: 'g4', text: 'Average commute is 54 minutes', stands: true }),
      statement({ id: 'g5', kind: 'supportGroup', parentId: 't', groupType: 'anySufficient', name: null, holds: false }),
      statement({ id: 's6', kind: 'supportingStatement', parentId: 'g5', text: 'Flexible hours', stands: false }),
      statement({ id: 'c2', kind: 'counterArgument', parentId: 's6', text: 'Hours blur into evenings', refuted: false }),
    ],
  };
}

/** A thesis with a chain of `depth` groups, each nested in the one before (ids g1 to g<depth>). */
export function deepGroupDiscussion(depth: number): Discussion {
  const statements: Statement[] = [statement({ id: 't', kind: 'thesis', text: 'Deep thesis' })];
  for (let level = 1; level <= depth; level += 1) {
    statements.push(
      statement({
        id: `g${level}`,
        kind: 'supportGroup',
        parentId: level === 1 ? 't' : `g${level - 1}`,
        groupType: level % 2 === 0 ? 'anySufficient' : 'allRequired',
        name: `Level ${level}`,
      }),
    );
  }
  return {
    id: 'd1',
    title: 'Deep discussion',
    topicTags: ['work'],
    createdAt: created,
    status: 'unsupported',
    thesisId: 't',
    statements,
  };
}
