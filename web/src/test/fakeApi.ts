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
  return { parentId: null, createdAt: created, ...partial };
}

/**
 * The outline reference discussion: two groups, supports, an illustration, counters and a rebuttal.
 * Status values follow the AC-4 rules (the counter on "Fewer interruptions" is unrefuted, so it is countered).
 */
export function referenceDiscussion(): Discussion {
  return {
    id: 'd1',
    title: 'Remote work boosts productivity',
    topicTag: 'work.remote',
    createdAt: created,
    status: 'countered',
    thesisId: 't',
    statements: [
      statement({ id: 't', kind: 'thesis', text: 'Remote work boosts productivity' }),
      statement({ id: 'g1', kind: 'supportGroup', parentId: 't', groupType: 'allRequired', holds: false }),
      statement({ id: 's1', kind: 'supportingStatement', parentId: 'g1', text: 'Fewer interruptions', stands: false }),
      statement({ id: 'c1', kind: 'counterArgument', parentId: 's1', text: 'Slack pings replace them', refuted: false }),
      statement({ id: 's2', kind: 'supportingStatement', parentId: 'g1', text: 'No commute saves energy', stands: true }),
      statement({ id: 'g2', kind: 'supportGroup', parentId: 't', groupType: 'anySufficient', holds: true }),
      statement({ id: 's3', kind: 'supportingStatement', parentId: 'g2', text: 'Studies show +13% output', stands: true }),
      statement({ id: 'i1', kind: 'illustration', parentId: 's3', text: 'e.g. my team shipped early' }),
      statement({ id: 'c2', kind: 'counterArgument', parentId: 't', text: 'Collaboration suffers', refuted: true }),
      statement({ id: 'r1', kind: 'rebuttal', parentId: 'c2', text: 'Async docs offset this', refuted: false }),
    ],
  };
}
