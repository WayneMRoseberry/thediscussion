import type {
  AddStatementInput,
  CreateDiscussionInput,
  Discussion,
  DiscussionSummary,
  ProblemDetails,
  Statement,
} from './types';

const basePath = '/api/v1';

/** A failed API call. `status` is 0 when the server could not be reached. */
export class ApiError extends Error {
  readonly status: number;
  readonly fieldErrors: Record<string, string[]>;

  constructor(status: number, message: string, fieldErrors: Record<string, string[]> = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.fieldErrors = fieldErrors;
  }

  get isNotFound(): boolean {
    return this.status === 404;
  }

  /** The first message for a field, if the API rejected it. */
  fieldError(field: string): string | undefined {
    return this.fieldErrors[field]?.[0];
  }
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${basePath}${path}`, {
      ...init,
      headers: {
        Accept: 'application/json',
        ...(init?.body ? { 'Content-Type': 'application/json' } : {}),
        ...init?.headers,
      },
    });
  } catch {
    throw new ApiError(0, 'Could not reach the server. Check your connection and try again.');
  }

  if (!response.ok) {
    const problem = await readProblem(response);
    throw new ApiError(
      response.status,
      problem?.detail ?? problem?.title ?? `The server responded with ${response.status}.`,
      problem?.errors ?? {},
    );
  }

  return (await response.json()) as T;
}

async function readProblem(response: Response): Promise<ProblemDetails | undefined> {
  const contentType = response.headers.get('Content-Type') ?? '';
  if (!contentType.includes('json')) {
    return undefined;
  }

  try {
    return (await response.json()) as ProblemDetails;
  } catch {
    return undefined;
  }
}

export function listDiscussions(signal?: AbortSignal): Promise<DiscussionSummary[]> {
  return request<DiscussionSummary[]>('/discussions', { signal });
}

export function getDiscussion(id: string, signal?: AbortSignal): Promise<Discussion> {
  return request<Discussion>(`/discussions/${encodeURIComponent(id)}`, { signal });
}

export function createDiscussion(input: CreateDiscussionInput): Promise<Discussion> {
  return request<Discussion>('/discussions', { method: 'POST', body: JSON.stringify(input) });
}

export function addStatement(discussionId: string, input: AddStatementInput): Promise<Statement> {
  return request<Statement>(`/discussions/${encodeURIComponent(discussionId)}/statements`, {
    method: 'POST',
    body: JSON.stringify(input),
  });
}
