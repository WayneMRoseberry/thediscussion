import { screen, waitFor, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { DiscussionSummary } from '../api/types';
import { mockApi, networkFailure, referenceDiscussion, validationProblem } from '../test/fakeApi';
import { renderAt } from '../test/render';

const summaries: DiscussionSummary[] = [
  { id: 'd1', title: 'Remote work boosts productivity', topicTag: 'work.remote', createdAt: '2026-11-03T12:00:00Z' },
  { id: 'd2', title: 'Cities should ban cars', topicTag: 'cities.transport', createdAt: '2026-11-02T12:00:00Z' },
];

describe('DiscussionListPage', () => {
  it('shows a loading state while discussions load', async () => {
    mockApi(() => new Promise(() => {}));

    renderAt('/');

    expect(screen.getByRole('status')).toHaveTextContent('Loading discussions');
  });

  it('shows an empty state when there are no discussions', async () => {
    mockApi(() => ({ status: 200, body: [] }));

    renderAt('/');

    expect(await screen.findByText(/No discussions yet/)).toBeInTheDocument();
    expect(screen.queryByRole('list', { name: 'All discussions' })).not.toBeInTheDocument();
  });

  it('links to each discussion', async () => {
    mockApi(() => ({ status: 200, body: summaries }));

    renderAt('/');

    const list = await screen.findByRole('list', { name: 'All discussions' });
    const links = within(list).getAllByRole('link');
    expect(links.map((link) => link.textContent)).toEqual(['Remote work boosts productivity', 'Cities should ban cars']);
    expect(links[0]).toHaveAttribute('href', '/discussions/d1');
    expect(links[1]).toHaveAttribute('href', '/discussions/d2');
    expect(within(list).getByText('work.remote')).toBeInTheDocument();
  });

  it('shows an error with a retry that reloads', async () => {
    let calls = 0;
    mockApi(() => {
      calls += 1;
      return calls === 1 ? networkFailure() : { status: 200, body: summaries };
    });
    const { user } = renderAt('/');

    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not load discussions');
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('link', { name: 'Cities should ban cars' })).toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('creates a discussion and opens it', async () => {
    const created = { ...referenceDiscussion(), id: 'new', title: 'Tea beats coffee', statements: [referenceDiscussion().statements[0]], status: 'unsupported' as const };
    const fetchMock = mockApi(({ method, path }) => {
      if (method === 'POST' && path === '/api/v1/discussions') return { status: 201, body: created };
      if (path === '/api/v1/discussions/new') return { status: 200, body: created };
      return { status: 200, body: [] };
    });
    const { user } = renderAt('/');
    await screen.findByText(/No discussions yet/);

    await user.type(screen.getByLabelText('Title'), 'Tea beats coffee');
    await user.type(screen.getByLabelText('Topic tag'), 'food.drinks');
    await user.type(screen.getByLabelText('Thesis'), 'Tea is better than coffee');
    await user.click(screen.getByRole('button', { name: 'Create discussion' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Tea beats coffee' })).toBeInTheDocument();
    const post = fetchMock.mock.calls.find(([, init]) => init?.method === 'POST')!;
    expect(JSON.parse(post[1]!.body as string)).toEqual({
      title: 'Tea beats coffee',
      topicTag: 'food.drinks',
      thesis: 'Tea is better than coffee',
    });
  });

  it('shows API validation errors next to the fields', async () => {
    mockApi(({ method }) =>
      method === 'POST'
        ? validationProblem({ topicTag: ['Topic tag must be 1 to 5 dot-separated segments.'], title: ['Title is required.'] })
        : { status: 200, body: [] },
    );
    const { user } = renderAt('/');
    await screen.findByText(/No discussions yet/);

    await user.type(screen.getByLabelText('Topic tag'), 'Not A Tag');
    await user.click(screen.getByRole('button', { name: 'Create discussion' }));

    const topicTag = screen.getByLabelText('Topic tag');
    await waitFor(() => expect(topicTag).toHaveAttribute('aria-invalid', 'true'));
    expect(topicTag).toHaveAccessibleDescription(expect.stringContaining('Topic tag must be 1 to 5 dot-separated segments.'));
    expect(screen.getByLabelText('Title')).toHaveAccessibleDescription(expect.stringContaining('Title is required.'));
    expect(screen.getByLabelText('Thesis')).not.toHaveAttribute('aria-invalid');
  });

  it('has no accessibility violations with discussions listed', async () => {
    mockApi(() => ({ status: 200, body: summaries }));
    const { container } = renderAt('/');
    await screen.findByRole('list', { name: 'All discussions' });

    expect(await axe(container)).toHaveNoViolations();
  });

  it('has no accessibility violations in the empty state with validation errors shown', async () => {
    mockApi(({ method }) => (method === 'POST' ? validationProblem({ title: ['Title is required.'] }) : { status: 200, body: [] }));
    const { container, user } = renderAt('/');
    await screen.findByText(/No discussions yet/);
    await user.click(screen.getByRole('button', { name: 'Create discussion' }));
    await screen.findByText('Title is required.');

    expect(await axe(container)).toHaveNoViolations();
  });
});
