import { screen, waitFor, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { Discussion, Statement } from '../api/types';
import { mockApi, networkFailure, referenceDiscussion, validationProblem, type Handler } from '../test/fakeApi';
import { renderAt } from '../test/render';

const edited = '2026-11-03T14:00:00.000000+00:00';

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

function withStatement(discussion: Discussion, id: string, change: Partial<Statement>): Discussion {
  return { ...discussion, statements: discussion.statements.map((s) => (s.id === id ? { ...s, ...change } : s)) };
}

/** Serves `current` for every GET of the discussion and hands other requests to `write`. */
function serve(initial: Discussion, write: (request: Parameters<Handler>[0], set: (next: Discussion) => void) => ReturnType<Handler>) {
  let current = initial;
  return mockApi((request) => {
    if (request.method === 'GET' && request.path === '/api/v1/discussions/d1') {
      return { status: 200, body: current };
    }
    return write(request, (next) => {
      current = next;
    });
  });
}

function calls(fetchMock: ReturnType<typeof mockApi>, method: string) {
  return fetchMock.mock.calls.filter(([, init]) => (init?.method ?? 'GET') === method);
}

async function openDiscussion() {
  const rendered = renderAt('/discussions/d1');
  await screen.findByRole('list', { name: 'Argument outline' });
  return rendered;
}

describe('Editing and deleting statements', () => {
  it('offers Edit for every statement with text and Delete for every statement except the thesis', async () => {
    mockApi(() => ({ status: 200, body: referenceDiscussion() }));
    await openDiscussion();

    for (const statement of referenceDiscussion().statements) {
      const controls = within(row(statement.id)).queryAllByRole('button').map((button) => button.textContent!.split(' ')[0]);
      const expected = [...(statement.kind === 'supportGroup' ? [] : ['Edit']), ...(statement.kind === 'thesis' ? [] : ['Delete'])];
      expect(controls, statement.id).toEqual(expected);
    }
    expect(within(row('c1')).getByRole('button', { name: 'Edit counter argument: Slack pings replace them' })).toBeInTheDocument();
    expect(within(row('g1')).getByRole('button', { name: 'Delete support group (ALL REQUIRED)' })).toBeInTheDocument();
  });

  it('marks edited statements', async () => {
    const discussion = withStatement(withStatement(referenceDiscussion(), 's1', { editedAt: edited }), 't', { editedAt: edited });
    mockApi(() => ({ status: 200, body: discussion }));
    await openDiscussion();

    expect(lineText('s1')).toBe('[SUPPORT] Fewer interruptions (edited) (countered)');
    expect(lineText('t')).toBe('[THESIS] Remote work boosts productivity (edited) Status: COUNTERED');
    expect(lineText('s2')).toBe('[SUPPORT] No commute saves energy (standing)');
  });

  it('edits a countered statement in place, sends only the text and refreshes the outline', async () => {
    const before = referenceDiscussion();
    const after = withStatement(before, 's1', { text: 'Fewer meetings', editedAt: edited });
    const fetchMock = serve(before, ({ method, path }, set) => {
      if (method === 'PATCH' && path === '/api/v1/discussions/d1/statements/s1') {
        set(after);
        return { status: 200, body: after.statements.find((s) => s.id === 's1') };
      }
      return { status: 500 };
    });
    const { user } = await openDiscussion();

    await user.click(within(row('s1')).getByRole('button', { name: /^Edit/ }));
    const form = within(outlineItem('s1')).getByRole('form', { name: 'Edit supporting statement' });
    const text = within(form).getByLabelText('Text');
    expect(text).toHaveValue('Fewer interruptions');
    expect(text).toHaveFocus();
    // The counter beneath stays in place while editing.
    expect(within(outlineItem('s1')).getByText('Slack pings replace them')).toBeInTheDocument();
    await user.clear(text);
    await user.type(text, 'Fewer meetings');
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(lineText('s1')).toBe('[SUPPORT] Fewer meetings (edited) (countered)'));
    expect(screen.getByRole('status')).toHaveTextContent('Saved the supporting statement.');
    expect(screen.queryByRole('form', { name: 'Edit supporting statement' })).not.toBeInTheDocument();
    expect(within(outlineItem('s1')).getByText('Slack pings replace them')).toBeInTheDocument();
    expect(within(row('s1')).getByRole('button', { name: /^Edit/ })).toHaveFocus();
    const [patch] = calls(fetchMock, 'PATCH');
    expect(JSON.parse(patch[1]!.body as string)).toEqual({ text: 'Fewer meetings' });
    expect(calls(fetchMock, 'GET')).toHaveLength(2);
  });

  it('shows API validation errors next to the text field and keeps the form open', async () => {
    serve(referenceDiscussion(), () => validationProblem({ text: ['Text is required.'] }));
    const { user } = await openDiscussion();

    await user.click(within(row('t')).getByRole('button', { name: /^Edit/ }));
    const form = screen.getByRole('form', { name: 'Edit thesis' });
    await user.clear(within(form).getByLabelText('Text'));
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    const text = within(form).getByLabelText('Text');
    await waitFor(() => expect(text).toHaveAttribute('aria-invalid', 'true'));
    expect(text).toHaveAccessibleDescription(expect.stringContaining('Text is required.'));
    expect(screen.getByRole('form', { name: 'Edit thesis' })).toBeInTheDocument();
  });

  it('shows an error with Retry when saving fails, and Retry saves', async () => {
    const after = withStatement(referenceDiscussion(), 'c2', { text: 'Teams collaborate less', editedAt: edited });
    let attempts = 0;
    const fetchMock = serve(referenceDiscussion(), ({ method }, set) => {
      if (method !== 'PATCH') return { status: 500 };
      attempts += 1;
      if (attempts === 1) networkFailure();
      set(after);
      return { status: 200, body: after.statements.find((s) => s.id === 'c2') };
    });
    const { user } = await openDiscussion();

    await user.click(within(row('c2')).getByRole('button', { name: /^Edit/ }));
    const form = screen.getByRole('form', { name: 'Edit counter argument' });
    await user.clear(within(form).getByLabelText('Text'));
    await user.type(within(form).getByLabelText('Text'), 'Teams collaborate less');
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    const alert = await within(form).findByRole('alert');
    expect(alert).toHaveTextContent('Could not save the counter argument. Could not reach the server.');
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(lineText('c2')).toBe('x [COUNTER] Teams collaborate less (edited) (refuted)'));
    expect(calls(fetchMock, 'PATCH')).toHaveLength(2);
  });

  it('cancelling an edit sends nothing and keeps the text', async () => {
    const fetchMock = serve(referenceDiscussion(), () => ({ status: 500 }));
    const { user } = await openDiscussion();

    await user.click(within(row('i1')).getByRole('button', { name: /^Edit/ }));
    const form = screen.getByRole('form', { name: 'Edit illustration' });
    await user.type(within(form).getByLabelText('Text'), ' and more');
    await user.click(within(form).getByRole('button', { name: 'Cancel' }));

    expect(lineText('i1')).toBe('[ILLUSTRATION] e.g. my team shipped early');
    expect(calls(fetchMock, 'PATCH')).toHaveLength(0);
  });

  it('names how many statements beneath the target will also be deleted', async () => {
    mockApi(() => ({ status: 200, body: referenceDiscussion() }));
    const { user } = await openDiscussion();

    await user.click(within(row('g1')).getByRole('button', { name: /^Delete/ }));
    expect(screen.getByRole('alertdialog', { name: 'Delete support group' })).toHaveAccessibleDescription(
      'Delete this support group and 3 statements beneath it? This cannot be undone.',
    );
    await user.click(screen.getByRole('button', { name: 'Cancel' }));

    await user.click(within(row('s3')).getByRole('button', { name: /^Delete/ }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('Delete this supporting statement and 1 statement beneath it?');
    await user.keyboard('{Escape}');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();

    await user.click(within(row('r1')).getByRole('button', { name: /^Delete/ }));
    expect(screen.getByRole('alertdialog')).toHaveTextContent('Delete this rebuttal? This cannot be undone.');
  });

  it('deletes an unrefuted counter after confirmation and refreshes the statuses', async () => {
    const before = referenceDiscussion();
    const after: Discussion = {
      ...before,
      status: 'tested',
      statements: before.statements
        .filter((s) => s.id !== 'c1')
        .map((s) => (s.id === 's1' ? { ...s, stands: true } : s.id === 'g1' ? { ...s, holds: true } : s)),
    };
    const fetchMock = serve(before, ({ method, path }, set) => {
      if (method === 'DELETE' && path === '/api/v1/discussions/d1/statements/c1') {
        set(after);
        return { status: 204 };
      }
      return { status: 500 };
    });
    const { user } = await openDiscussion();
    expect(within(outlineItem('t')).getByText('COUNTERED')).toBeInTheDocument();

    const deleteButton = within(row('c1')).getByRole('button', { name: /^Delete/ });
    await user.click(deleteButton);
    const dialog = screen.getByRole('alertdialog', { name: 'Delete counter argument' });
    expect(dialog).toHaveTextContent('Delete this counter argument?');
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toHaveFocus();
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(within(outlineItem('t')).getByText('TESTED')).toBeInTheDocument());
    expect(document.querySelector('[data-statement-id="c1"]')).toBeNull();
    expect(lineText('s1')).toBe('[SUPPORT] Fewer interruptions (standing)');
    expect(lineText('g1')).toBe('+ Support group: ALL REQUIRED (holds)');
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Deleted the counter argument.');
    expect(calls(fetchMock, 'DELETE')).toHaveLength(1);
  });

  it('deleting a subtree announces how many statements went with it', async () => {
    const before = referenceDiscussion();
    const after: Discussion = { ...before, statements: before.statements.filter((s) => !['c2', 'r1'].includes(s.id)) };
    serve(before, (_request, set) => {
      set(after);
      return { status: 204 };
    });
    const { user } = await openDiscussion();

    await user.click(within(row('c2')).getByRole('button', { name: /^Delete/ }));
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete' }));

    await waitFor(() => expect(document.querySelector('[data-statement-id="r1"]')).toBeNull());
    expect(screen.getByRole('status')).toHaveTextContent('Deleted the counter argument and 1 statement beneath it.');
  });

  it('cancelling a delete sends nothing', async () => {
    const fetchMock = serve(referenceDiscussion(), () => ({ status: 500 }));
    const { user } = await openDiscussion();

    await user.click(within(row('s2')).getByRole('button', { name: /^Delete/ }));
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument();
    expect(lineText('s2')).toBe('[SUPPORT] No commute saves energy (standing)');
    expect(calls(fetchMock, 'DELETE')).toHaveLength(0);
  });

  it('shows an error with Retry when a delete fails, and Retry deletes', async () => {
    const before = referenceDiscussion();
    const after: Discussion = { ...before, statements: before.statements.filter((s) => s.id !== 'i1') };
    let attempts = 0;
    serve(before, (_request, set) => {
      attempts += 1;
      if (attempts === 1) return { status: 500, body: { title: 'Server error', status: 500 } };
      set(after);
      return { status: 204 };
    });
    const { user } = await openDiscussion();

    await user.click(within(row('i1')).getByRole('button', { name: /^Delete/ }));
    const dialog = screen.getByRole('alertdialog');
    await user.click(within(dialog).getByRole('button', { name: 'Delete' }));

    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Could not delete. Server error');
    await user.click(within(dialog).getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(document.querySelector('[data-statement-id="i1"]')).toBeNull();
  });

  it('has no accessibility violations with an edit form showing an error', async () => {
    serve(referenceDiscussion(), () => validationProblem({ text: ['Text is required.'] }));
    const { container, user } = await openDiscussion();
    await user.click(within(row('c1')).getByRole('button', { name: /^Edit/ }));
    await user.click(within(screen.getByRole('form', { name: 'Edit counter argument' })).getByRole('button', { name: 'Save' }));
    await screen.findByText('Text is required.');

    expect(await axe(container)).toHaveNoViolations();
  });

  it('has no accessibility violations with the delete confirmation open, including its error', async () => {
    serve(referenceDiscussion(), () => networkFailure());
    const { container, user } = await openDiscussion();
    await user.click(within(row('g2')).getByRole('button', { name: /^Delete/ }));
    expect(await axe(container)).toHaveNoViolations();

    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete' }));
    await within(screen.getByRole('alertdialog')).findByRole('alert');
    expect(await axe(container)).toHaveNoViolations();
  });
});

describe('Editing and deleting the discussion', () => {
  it('shows the title, the tags as links to their tag pages, and Edit and Delete', async () => {
    mockApi(() => ({ status: 200, body: { ...referenceDiscussion(), topicTags: ['work.remote', 'management'] } }));
    await openDiscussion();

    expect(screen.getByRole('heading', { level: 1, name: 'Remote work boosts productivity' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '#work.remote' })).toHaveAttribute('href', '/topics/work.remote');
    expect(screen.getByRole('link', { name: '#management' })).toHaveAttribute('href', '/topics/management');
    expect(screen.getByRole('button', { name: 'Edit discussion title and tags' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Delete discussion' })).toBeInTheDocument();
  });

  it('opens the tag page from a tag link', async () => {
    mockApi(({ path }) => {
      if (path === '/api/v1/topics') return { status: 200, body: [] };
      if (path.startsWith('/api/v1/discussions?tag=')) return { status: 200, body: [] };
      return { status: 200, body: referenceDiscussion() };
    });
    const { user } = await openDiscussion();

    await user.click(screen.getByRole('link', { name: '#work.remote' }));

    expect(await screen.findByText('No discussions under work.remote')).toBeInTheDocument();
  });

  it('edits the title and tags, accepting tags separated by commas or spaces', async () => {
    const before = referenceDiscussion();
    const after = { ...before, title: 'Remote work wins', topicTags: ['work.remote', 'management', 'productivity'] };
    const fetchMock = serve(before, ({ method, path }, set) => {
      if (method === 'PATCH' && path === '/api/v1/discussions/d1') {
        set(after);
        return { status: 200, body: after };
      }
      return { status: 500 };
    });
    const { user } = await openDiscussion();

    await user.click(screen.getByRole('button', { name: 'Edit discussion title and tags' }));
    const form = screen.getByRole('form', { name: 'Edit discussion' });
    const title = within(form).getByLabelText('Title');
    const tags = within(form).getByLabelText('Topic tags');
    expect(title).toHaveValue('Remote work boosts productivity');
    expect(title).toHaveFocus();
    expect(tags).toHaveValue('work.remote');
    await user.clear(title);
    await user.type(title, 'Remote work wins');
    await user.type(tags, ', management productivity');
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Remote work wins' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: '#productivity' })).toHaveAttribute('href', '/topics/productivity');
    expect(screen.getByRole('status')).toHaveTextContent('Saved the discussion.');
    const [patch] = calls(fetchMock, 'PATCH');
    expect(JSON.parse(patch[1]!.body as string)).toEqual({
      title: 'Remote work wins',
      topicTags: ['work.remote', 'management', 'productivity'],
    });
  });

  it('sends only the fields that changed', async () => {
    const before = referenceDiscussion();
    const fetchMock = serve(before, (_request, set) => {
      const after = { ...before, topicTags: ['work'] };
      set(after);
      return { status: 200, body: after };
    });
    const { user } = await openDiscussion();

    await user.click(screen.getByRole('button', { name: 'Edit discussion title and tags' }));
    const tags = screen.getByLabelText('Topic tags');
    await user.clear(tags);
    await user.type(tags, 'work');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    await screen.findByRole('link', { name: '#work' });
    expect(JSON.parse(calls(fetchMock, 'PATCH')[0][1]!.body as string)).toEqual({ topicTags: ['work'] });
  });

  it('shows API validation errors next to the title and tags fields', async () => {
    serve(referenceDiscussion(), () =>
      validationProblem({ topicTags: ["Topic tag 'work.remote' is listed more than once."], title: ['Title is required.'] }),
    );
    const { user } = await openDiscussion();

    await user.click(screen.getByRole('button', { name: 'Edit discussion title and tags' }));
    await user.clear(screen.getByLabelText('Title'));
    await user.type(screen.getByLabelText('Topic tags'), ' work.remote');
    await user.click(screen.getByRole('button', { name: 'Save' }));

    const tags = screen.getByLabelText('Topic tags');
    await waitFor(() => expect(tags).toHaveAttribute('aria-invalid', 'true'));
    expect(tags).toHaveAccessibleDescription(expect.stringContaining("Topic tag 'work.remote' is listed more than once."));
    expect(screen.getByLabelText('Title')).toHaveAccessibleDescription(expect.stringContaining('Title is required.'));
  });

  it('shows an error with Retry when saving the discussion fails', async () => {
    let attempts = 0;
    serve(referenceDiscussion(), (_request, set) => {
      attempts += 1;
      if (attempts === 1) networkFailure();
      const after = { ...referenceDiscussion(), title: 'Retried title' };
      set(after);
      return { status: 200, body: after };
    });
    const { user } = await openDiscussion();

    await user.click(screen.getByRole('button', { name: 'Edit discussion title and tags' }));
    await user.clear(screen.getByLabelText('Title'));
    await user.type(screen.getByLabelText('Title'), 'Retried title');
    await user.click(screen.getByRole('button', { name: 'Save' }));
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Could not save the discussion.');
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Retried title' })).toBeInTheDocument();
  });

  it('deletes the discussion after a confirmation naming its statements, and returns to the list', async () => {
    const fetchMock = mockApi(({ method, path }) => {
      if (method === 'DELETE' && path === '/api/v1/discussions/d1') return { status: 204 };
      if (path === '/api/v1/discussions') return { status: 200, body: [] };
      return { status: 200, body: referenceDiscussion() };
    });
    const { user } = await openDiscussion();

    await user.click(screen.getByRole('button', { name: 'Delete discussion' }));
    const dialog = screen.getByRole('alertdialog', { name: 'Delete discussion' });
    expect(dialog).toHaveTextContent('Delete this discussion and all 10 statements in it, including the thesis?');
    await user.click(within(dialog).getByRole('button', { name: 'Delete discussion' }));

    expect(await screen.findByRole('heading', { level: 1, name: 'Discussions' })).toBeInTheDocument();
    expect(await screen.findByText('Deleted the discussion "Remote work boosts productivity".')).toHaveAttribute('role', 'status');
    expect(calls(fetchMock, 'DELETE').map(([url]) => url)).toEqual(['/api/v1/discussions/d1']);
  });

  it('shows an error with Retry when deleting the discussion fails, and stays on the page', async () => {
    mockApi(({ method }) => (method === 'DELETE' ? { status: 500, body: { title: 'Server error', status: 500 } } : { status: 200, body: referenceDiscussion() }));
    const { user } = await openDiscussion();

    await user.click(screen.getByRole('button', { name: 'Delete discussion' }));
    await user.click(within(screen.getByRole('alertdialog')).getByRole('button', { name: 'Delete discussion' }));

    const dialog = screen.getByRole('alertdialog');
    expect(await within(dialog).findByRole('alert')).toHaveTextContent('Could not delete the discussion. Server error');
    expect(within(dialog).getByRole('button', { name: 'Retry' })).toBeEnabled();
    expect(screen.getByRole('list', { name: 'Argument outline' })).toBeInTheDocument();
  });

  it('has no accessibility violations with the discussion edit form showing errors, and with the delete confirmation', async () => {
    serve(referenceDiscussion(), () => validationProblem({ topicTags: ['At least one topic tag is required.'] }));
    const { container, user } = await openDiscussion();

    await user.click(screen.getByRole('button', { name: 'Edit discussion title and tags' }));
    await user.clear(screen.getByLabelText('Topic tags'));
    await user.click(screen.getByRole('button', { name: 'Save' }));
    await screen.findByText('At least one topic tag is required.');
    expect(await axe(container)).toHaveNoViolations();

    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    await user.click(screen.getByRole('button', { name: 'Delete discussion' }));
    expect(await axe(container)).toHaveNoViolations();
  });
});
