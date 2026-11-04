import { screen, waitFor, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { Discussion, Statement } from '../api/types';
import { mockApi, networkFailure, notFoundProblem, referenceDiscussion, validationProblem } from '../test/fakeApi';
import { renderAt } from '../test/render';

function outlineItem(id: string): HTMLElement {
  const item = document.querySelector<HTMLElement>(`[data-statement-id="${id}"]`);
  if (!item) throw new Error(`No outline item for ${id}`);
  return item;
}

/** The text of one outline line, without its nested children. */
function lineText(id: string): string {
  return outlineItem(id).querySelector('.outline-node')!.textContent!.replace(/\s+/g, ' ').trim();
}

describe('DiscussionPage', () => {
  it('shows a loading state', () => {
    mockApi(() => new Promise(() => {}));

    renderAt('/discussions/d1');

    expect(screen.getByRole('status')).toHaveTextContent('Loading discussion');
  });

  it('shows "Discussion not found" for an unknown id', async () => {
    mockApi(() => notFoundProblem);

    renderAt('/discussions/missing');

    expect(await screen.findByRole('heading', { level: 1, name: 'Discussion not found' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to all discussions' })).toHaveAttribute('href', '/');
  });

  it('shows an API error banner with a retry', async () => {
    let calls = 0;
    mockApi(() => {
      calls += 1;
      return calls === 1 ? { status: 500, body: { title: 'Server error', status: 500 } } : { status: 200, body: referenceDiscussion() };
    });
    const { user } = renderAt('/discussions/d1');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load the discussion');
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Remote work boosts productivity' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows the unreachable-server error and recovers on retry', async () => {
    let calls = 0;
    mockApi(() => {
      calls += 1;
      return calls === 1 ? networkFailure() : { status: 200, body: referenceDiscussion() };
    });
    const { user } = renderAt('/discussions/d1');

    await user.click(within(await screen.findByRole('alert')).getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('list', { name: 'Argument outline' })).toBeInTheDocument();
  });

  it('renders the discussion as the reference outline', async () => {
    mockApi(() => ({ status: 200, body: referenceDiscussion() }));

    renderAt('/discussions/d1');

    const outline = await screen.findByRole('list', { name: 'Argument outline' });
    const breadcrumb = screen.getByRole('navigation', { name: 'Breadcrumb' });
    expect(within(breadcrumb).getByRole('link', { name: 'Discussions' })).toHaveAttribute('href', '/');
    expect(within(breadcrumb).getByText('Remote work boosts productivity')).toHaveAttribute('aria-current', 'page');

    expect(lineText('t')).toBe('[THESIS] Remote work boosts productivity Status: COUNTERED');
    expect(lineText('g1')).toBe('+ Support group: ALL REQUIRED (does not hold)');
    expect(lineText('s1')).toBe('[SUPPORT] Fewer interruptions (countered)');
    expect(lineText('c1')).toBe('x [COUNTER] Slack pings replace them (unrefuted)');
    expect(lineText('s2')).toBe('[SUPPORT] No commute saves energy (standing)');
    expect(lineText('g2')).toBe('+ Support group: ANY SUFFICIENT (holds)');
    expect(lineText('s3')).toBe('[SUPPORT] Studies show +13% output (standing)');
    expect(lineText('i1')).toBe('[ILLUSTRATION] e.g. my team shipped early');
    expect(lineText('c2')).toBe('x [COUNTER] Collaboration suffers (refuted)');
    expect(lineText('r1')).toBe('x [REBUTTAL] Async docs offset this (unrefuted)');

    // Indentation follows the tree: the counter sits under its supporting statement, the rebuttal under its counter.
    expect(within(outlineItem('s1')).getByText('Slack pings replace them')).toBeInTheDocument();
    expect(within(outlineItem('c2')).getByText('Async docs offset this')).toBeInTheDocument();
    expect(within(outlineItem('g2')).getByText('e.g. my team shipped early')).toBeInTheDocument();
    expect(outline.querySelectorAll(':scope > li')).toHaveLength(1);
  });

  it.each([
    ['unsupported', 'UNSUPPORTED'],
    ['open', 'OPEN'],
    ['countered', 'COUNTERED'],
    ['tested', 'TESTED'],
  ] as const)('shows the %s status badge', async (status, label) => {
    mockApi(() => ({ status: 200, body: { ...referenceDiscussion(), status } }));

    renderAt('/discussions/d1');

    await screen.findByRole('list', { name: 'Argument outline' });
    expect(within(outlineItem('t')).getByText(label)).toHaveClass(`status-badge--${status}`);
  });

  it('adds a counter argument and refreshes the outline and statuses', async () => {
    const before = referenceDiscussion();
    // Start from a tested discussion: the support's counter is refuted.
    before.status = 'tested';
    before.statements = before.statements.filter((s) => s.id !== 'c1');
    const added: Statement = {
      id: 'c3',
      kind: 'counterArgument',
      parentId: 's2',
      text: 'Commutes are thinking time',
      createdAt: '2026-11-03T13:00:00Z',
      refuted: false,
    };
    const after: Discussion = {
      ...before,
      status: 'countered',
      statements: [
        ...before.statements.map((s) => (s.id === 's2' ? { ...s, stands: false } : s.id === 'g1' ? { ...s, holds: false } : s)),
        added,
      ],
    };
    let current = before;
    const fetchMock = mockApi(({ method }) => {
      if (method === 'POST') {
        current = after;
        return { status: 201, body: added };
      }
      return { status: 200, body: current };
    });
    const { user } = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });
    expect(within(outlineItem('t')).getByText('TESTED')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'Add counter' }));
    const form = screen.getByRole('form', { name: 'Add counter argument' });
    await user.selectOptions(within(form).getByLabelText('Counters'), '[SUPPORT] No commute saves energy');
    await user.type(within(form).getByLabelText('Text'), 'Commutes are thinking time');
    await user.click(within(form).getByRole('button', { name: 'Post counter argument' }));

    await waitFor(() => expect(within(outlineItem('t')).getByText('COUNTERED')).toBeInTheDocument());
    expect(lineText('c3')).toBe('x [COUNTER] Commutes are thinking time (unrefuted)');
    expect(within(outlineItem('s2')).getByText('Commutes are thinking time')).toBeInTheDocument();
    expect(lineText('s2')).toBe('[SUPPORT] No commute saves energy (countered)');
    expect(screen.getByRole('status')).toHaveTextContent('Added the counter argument.');
    expect(screen.queryByRole('form', { name: 'Add counter argument' })).not.toBeInTheDocument();

    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!;
    expect(post[0]).toBe('/api/v1/discussions/d1/statements');
    expect(JSON.parse(post[1]!.body as string)).toEqual({ kind: 'counterArgument', parentId: 's2', text: 'Commutes are thinking time' });
    expect(fetchMock.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === 'GET')).toHaveLength(2);
  });

  it('posts a support group with its group type', async () => {
    const fetchMock = mockApi(({ method }) =>
      method === 'POST'
        ? { status: 201, body: { id: 'g3', kind: 'supportGroup', parentId: 't', groupType: 'anySufficient', createdAt: '' } }
        : { status: 200, body: referenceDiscussion() },
    );
    const { user } = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });

    await user.click(screen.getByRole('button', { name: 'Add support group' }));
    const form = screen.getByRole('form', { name: 'Add support group' });
    await user.click(within(form).getByRole('radio', { name: /Any sufficient/ }));
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));

    await screen.findByText('Added the support group.');
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!;
    expect(JSON.parse(post[1]!.body as string)).toEqual({ kind: 'supportGroup', parentId: 't', groupType: 'anySufficient' });
  });

  it('offers only valid targets for each kind', async () => {
    mockApi(() => ({ status: 200, body: referenceDiscussion() }));
    const { user } = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });

    await user.click(screen.getByRole('button', { name: 'Add rebuttal' }));
    const rebuttalTargets = within(screen.getByRole('form', { name: 'Add rebuttal' })).getAllByRole('option');
    expect(rebuttalTargets.map((option) => option.textContent)).toEqual([
      '[COUNTER] Slack pings replace them',
      '[COUNTER] Collaboration suffers',
      '[REBUTTAL] Async docs offset this',
    ]);

    await user.click(screen.getByRole('button', { name: 'Add support' }));
    const groupTargets = within(screen.getByRole('form', { name: 'Add supporting statement' })).getAllByRole('option');
    expect(groupTargets.map((option) => option.textContent)).toEqual(['Support group 1: ALL REQUIRED', 'Support group 2: ANY SUFFICIENT']);

    await user.click(screen.getByRole('button', { name: 'Add illustration' }));
    const illustrationTargets = within(screen.getByRole('form', { name: 'Add illustration' })).getAllByRole('option');
    expect(illustrationTargets.map((option) => (option as HTMLOptionElement).value)).toEqual(['t', 's1', 's2', 's3']);
  });

  it('explains when a kind has nothing to target yet', async () => {
    const thesisOnly = { ...referenceDiscussion(), status: 'unsupported' as const, statements: [referenceDiscussion().statements[0]] };
    mockApi(() => ({ status: 200, body: thesisOnly }));
    const { user } = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });

    await user.click(screen.getByRole('button', { name: 'Add rebuttal' }));

    const form = screen.getByRole('form', { name: 'Add rebuttal' });
    expect(within(form).getByText('Add a counter argument first.')).toBeInTheDocument();
    expect(within(form).getByRole('button', { name: 'Post rebuttal' })).toBeDisabled();
  });

  it('shows API validation errors next to the field', async () => {
    mockApi(({ method }) =>
      method === 'POST'
        ? validationProblem({ text: ['Text is required.'], parentId: ['A rebuttal must target a counter argument or a rebuttal.'] })
        : { status: 200, body: referenceDiscussion() },
    );
    const { user } = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });

    await user.click(screen.getByRole('button', { name: 'Add rebuttal' }));
    const form = screen.getByRole('form', { name: 'Add rebuttal' });
    await user.click(within(form).getByRole('button', { name: 'Post rebuttal' }));

    const text = within(form).getByLabelText('Text');
    await waitFor(() => expect(text).toHaveAttribute('aria-invalid', 'true'));
    expect(text).toHaveAccessibleDescription(expect.stringContaining('Text is required.'));
    expect(within(form).getByLabelText('Rebuts')).toHaveAccessibleDescription(
      'A rebuttal must target a counter argument or a rebuttal.',
    );
    expect(screen.getByRole('form', { name: 'Add rebuttal' })).toBeInTheDocument();
  });

  it('has no accessibility violations on the discussion view', async () => {
    mockApi(() => ({ status: 200, body: referenceDiscussion() }));
    const { container } = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });

    expect(await axe(container)).toHaveNoViolations();
  });

  it('has no accessibility violations with an add form showing errors', async () => {
    mockApi(({ method }) =>
      method === 'POST' ? validationProblem({ text: ['Text is required.'] }) : { status: 200, body: referenceDiscussion() },
    );
    const { container, user } = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });
    await user.click(screen.getByRole('button', { name: 'Add counter' }));
    await user.click(screen.getByRole('button', { name: 'Post counter argument' }));
    await screen.findByText('Text is required.');

    expect(await axe(container)).toHaveNoViolations();
  });

  it('has no accessibility violations in the not-found and error states', async () => {
    mockApi(() => notFoundProblem);
    const notFound = renderAt('/discussions/missing');
    await screen.findByRole('heading', { name: 'Discussion not found' });
    expect(await axe(notFound.container)).toHaveNoViolations();
    notFound.unmount();

    mockApi(() => ({ status: 500, body: { title: 'Server error', status: 500 } }));
    const failed = renderAt('/discussions/d1');
    await screen.findByRole('alert');
    expect(await axe(failed.container)).toHaveNoViolations();
  });
});
