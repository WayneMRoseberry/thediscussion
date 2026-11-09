import { screen, waitFor, within } from '@testing-library/react';
import type { Discussion, Statement } from '../api/types';
import { deepGroupDiscussion, mockApi, nestedDiscussion, type Handler } from '../test/fakeApi';
import { renderAt } from '../test/render';

// AC-5 / UC-5: regression of the existing delete flow on nested trees, against a mocked API. The confirmation names
// every statement beneath the target at any depth (countBeneath), and a confirmed delete reloads the outline and statuses.

function outlineItem(id: string): HTMLElement {
  const item = document.querySelector<HTMLElement>(`[data-statement-id="${id}"]`);
  if (!item) throw new Error(`No outline item for ${id}`);
  return item;
}

/** One outline row (the statement and its controls), without its nested children. */
function row(id: string): HTMLElement {
  const found = outlineItem(id).querySelector<HTMLElement>(':scope > .outline-row, :scope > form');
  if (!found) throw new Error(`No row for ${id}`);
  return found;
}

function lineText(id: string): string {
  return outlineItem(id).querySelector('.outline-node')!.textContent!.replace(/\s+/g, ' ').trim();
}

function shown(id: string): boolean {
  return document.querySelector(`[data-statement-id="${id}"]`) !== null;
}

/** `discussion` without the given statements, with `changes` applied to the rest and the given thesis status. */
function afterDelete(
  discussion: Discussion,
  deleted: string[],
  status: Discussion['status'],
  changes: Record<string, Partial<Statement>> = {},
): Discussion {
  return {
    ...discussion,
    status,
    statements: discussion.statements.filter((s) => !deleted.includes(s.id)).map((s) => ({ ...s, ...changes[s.id] })),
  };
}

/**
 * Serves `before` for every GET of the discussion until `DELETE` of `target` succeeds (204), then serves `after`.
 * Any other request fails.
 */
function serveDelete(before: Discussion, target: string, after: Discussion) {
  let current = before;
  const handler: Handler = ({ method, path }) => {
    if (method === 'GET' && path === '/api/v1/discussions/d1') {
      return { status: 200, body: current };
    }
    if (method === 'DELETE' && path === `/api/v1/discussions/d1/statements/${target}`) {
      current = after;
      return { status: 204 };
    }
    return { status: 500 };
  };
  return mockApi(handler);
}

function calls(fetchMock: ReturnType<typeof mockApi>, method: string) {
  return fetchMock.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === method);
}

async function openDiscussion() {
  const rendered = renderAt('/discussions/d1');
  await screen.findByRole('list', { name: 'Argument outline' });
  return rendered;
}

async function startDelete(user: Awaited<ReturnType<typeof openDiscussion>>['user'], id: string) {
  await user.click(within(row(id)).getByRole('button', { name: /^Delete/ }));
  return screen.getByRole('alertdialog');
}

describe('Delete confirmation on nested trees (UC-5; countBeneath at any depth)', () => {
  it.each([
    ['g1', 'a top-level group with nested groups and a sub-argument', 'Delete this support group and 9 statements beneath it?'],
    ['g2', 'a nested group with its own nested group', 'Delete this support group and 4 statements beneath it?'],
    ['g3', 'an empty nested group', 'Delete this support group?'],
    ['s4', 'a supporting statement with a sub-argument group', 'Delete this supporting statement and 2 statements beneath it?'],
    ['g4', 'a sub-argument group', 'Delete this support group and 1 statement beneath it?'],
    ['g5', 'an unnamed group', 'Delete this support group and 2 statements beneath it?'],
  ])('names every statement beneath %s (%s)', async (id, _scenario, question) => {
    mockApi(() => ({ status: 200, body: nestedDiscussion() }));
    const { user } = await openDiscussion();

    const dialog = await startDelete(user, id);

    expect(dialog).toHaveAccessibleDescription(`${question} This cannot be undone.`);
    await user.click(within(dialog).getByRole('button', { name: 'Cancel' }));
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
  });

  it('names every statement beneath a group deep in a 150-level chain, below the nesting limit', async () => {
    mockApi(() => ({ status: 200, body: deepGroupDiscussion(150) }));
    const { user } = await openDiscussion();

    expect(await startDelete(user, 'g1')).toHaveTextContent('Delete this support group and 149 statements beneath it?');
    await user.keyboard('{Escape}');
    expect(await startDelete(user, 'g100')).toHaveTextContent('Delete this support group and 50 statements beneath it?');
  });
});

describe('Confirmed deletes in nested trees refresh the outline and statuses (UC-5 A1 and A2)', () => {
  it('deleting the standing member of a nested any-sufficient group shows it and its all-required parent failing', async () => {
    const before = nestedDiscussion();
    const after = afterDelete(before, ['s2'], 'countered', { g1: { holds: false }, g2: { holds: false } });
    const fetchMock = serveDelete(before, 's2', after);
    const { user } = await openDiscussion();
    expect(within(outlineItem('t')).getByText('TESTED')).toBeInTheDocument();

    const dialog = await startDelete(user, 's2');
    expect(dialog).toHaveTextContent('Delete this supporting statement?');
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(within(outlineItem('t')).getByText('COUNTERED')).toBeInTheDocument());
    expect(shown('s2')).toBe(false);
    expect(lineText('g2')).toBe('+ ANY SUFFICIENT: Evidence (does not hold)');
    expect(lineText('g1')).toBe('+ ALL REQUIRED: Productivity mechanisms (does not hold)');
    expect(screen.getByRole('status')).toHaveTextContent('Deleted the supporting statement.');
    expect(calls(fetchMock, 'DELETE')).toHaveLength(1);
  });

  it('deleting a supporting statement removes its sub-argument group and everything beneath it from the outline', async () => {
    const before = nestedDiscussion();
    const after = afterDelete(before, ['s4', 'g4', 's5'], 'tested');
    serveDelete(before, 's4', after);
    const { user } = await openDiscussion();

    await user.click(within(await startDelete(user, 's4')).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(shown('s4')).toBe(false));
    expect(shown('g4')).toBe(false);
    expect(shown('s5')).toBe(false);
    expect(lineText('g1')).toBe('+ ALL REQUIRED: Productivity mechanisms (holds)');
    expect(screen.getByRole('status')).toHaveTextContent('Deleted the supporting statement and 2 statements beneath it.');
  });

  it('deleting a nested group removes its whole subtree, including its own nested group', async () => {
    const before = nestedDiscussion();
    const after = afterDelete(before, ['g2', 's2', 's3', 'c1', 'g3'], 'tested');
    serveDelete(before, 'g2', after);
    const { user } = await openDiscussion();

    await user.click(within(await startDelete(user, 'g2')).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(shown('g2')).toBe(false));
    for (const gone of ['s2', 's3', 'c1', 'g3']) {
      expect(shown(gone)).toBe(false);
    }
    for (const kept of ['g1', 's1', 's4', 'g4', 's5', 'g5']) {
      expect(shown(kept)).toBe(true);
    }
    expect(screen.getByRole('status')).toHaveTextContent('Deleted the support group and 4 statements beneath it.');
  });

  it('deleting the only member of a sub-argument group shows that group as empty (no holds)', async () => {
    const before = nestedDiscussion();
    const after = afterDelete(before, ['s5'], 'tested', { g4: { holds: undefined } });
    serveDelete(before, 's5', after);
    const { user } = await openDiscussion();
    expect(lineText('g4')).toBe('+ ALL REQUIRED: Why commuting drains energy (holds)');

    await user.click(within(await startDelete(user, 's5')).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(lineText('g4')).toBe('+ ALL REQUIRED: Why commuting drains energy (empty)'));
    expect(lineText('s4')).toBe('[SUPPORT] No commute saves energy (standing)');
  });

  it('deleting a group deep in a 150-level chain removes everything beneath it and leaves its parent empty', async () => {
    const before = deepGroupDiscussion(150);
    const deeper = Array.from({ length: 51 }, (_, index) => `g${index + 100}`);
    const after = afterDelete(before, deeper, 'unsupported');
    const fetchMock = serveDelete(before, 'g100', after);
    const { user } = await openDiscussion();

    await user.click(within(await startDelete(user, 'g100')).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(shown('g100')).toBe(false));
    expect(shown('g150')).toBe(false);
    expect(lineText('g99')).toBe('+ ALL REQUIRED: Level 99 (empty)');
    expect(outlineItem('g1').querySelectorAll('li[data-statement-id]')).toHaveLength(98);
    expect(screen.getByRole('status')).toHaveTextContent('Deleted the support group and 50 statements beneath it.');
    expect(calls(fetchMock, 'DELETE')).toHaveLength(1);
  });
});
