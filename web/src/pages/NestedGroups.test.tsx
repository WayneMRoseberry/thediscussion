import { screen, waitFor, within } from '@testing-library/react';
import type { Discussion, Statement } from '../api/types';
import { deepGroupDiscussion, mockApi, nestedDiscussion, networkFailure, validationProblem, type Handler } from '../test/fakeApi';
import { renderAt } from '../test/render';

// AC-4 / UC-4: nested, named and editable groups in the web outline, against a mocked API.

const edited = '2026-11-08T10:00:00.000000+00:00';

function outlineItem(id: string): HTMLElement {
  const item = document.querySelector<HTMLElement>(`[data-statement-id="${id}"]`);
  if (!item) throw new Error(`No outline item for ${id}`);
  return item;
}

/** The outline item a statement's item is nested in (its parent in the rendered tree). */
function parentItem(id: string): HTMLElement | null {
  return outlineItem(id).parentElement!.closest<HTMLElement>('li[data-statement-id]');
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

function bodies(fetchMock: ReturnType<typeof mockApi>, method: string): unknown[] {
  return calls(fetchMock, method).map(([, init]) => JSON.parse(init!.body as string));
}

async function openDiscussion() {
  const rendered = renderAt('/discussions/d1');
  await screen.findByRole('list', { name: 'Argument outline' });
  return rendered;
}

async function openAddGroupForm(user: Awaited<ReturnType<typeof openDiscussion>>['user']) {
  await user.click(screen.getByRole('button', { name: 'Add support group' }));
  return screen.getByRole('form', { name: 'Add support group' });
}

describe('Nested groups in the outline (UC-4 steps 1 to 3)', () => {
  it('indents three levels of groups, each inside its parent group (Cluster B "UI nesting shown": 3 levels)', async () => {
    mockApi(() => ({ status: 200, body: nestedDiscussion() }));
    await openDiscussion();

    // Group parent kind: thesis, group, group.
    expect(parentItem('g1')).toBe(outlineItem('t'));
    expect(parentItem('g2')).toBe(outlineItem('g1'));
    expect(parentItem('g3')).toBe(outlineItem('g2'));
    expect(within(outlineItem('g1')).getByText('Replicated studies')).toBeInTheDocument();
    // Each level sits in its own nested list.
    expect(outlineItem('g3').closest('ul')!.closest('li')).toBe(outlineItem('g2'));
    // The supporting statements of a group stay inside it, around the nested group.
    expect(parentItem('s1')).toBe(outlineItem('g1'));
    expect(parentItem('s4')).toBe(outlineItem('g1'));
    expect(parentItem('c1')).toBe(outlineItem('s3'));
  });

  it('indents a sub-argument group under its supporting statement (group parent kind: supportingStatement)', async () => {
    mockApi(() => ({ status: 200, body: nestedDiscussion() }));
    await openDiscussion();

    expect(parentItem('g4')).toBe(outlineItem('s4'));
    expect(parentItem('s5')).toBe(outlineItem('g4'));
    expect(within(outlineItem('s4')).getByText('Average commute is 54 minutes')).toBeInTheDocument();
  });

  it('shows each group row with its type label, name and marker (Cluster B "holds marker shown")', async () => {
    mockApi(() => ({ status: 200, body: nestedDiscussion() }));
    await openDiscussion();

    expect(lineText('g1')).toBe('+ ALL REQUIRED: Productivity mechanisms (holds)');
    expect(lineText('g2')).toBe('+ ANY SUFFICIENT: Evidence (holds)');
    // holds absent: empty.
    expect(lineText('g3')).toBe('+ ALL REQUIRED: Replicated studies (empty)');
    expect(lineText('g4')).toBe('+ ALL REQUIRED: Why commuting drains energy (holds)');
    // holds false: does not hold.
    expect(within(row('g5')).getByText('(does not hold)')).toBeInTheDocument();
    expect(within(row('g5')).getByText('ANY SUFFICIENT:')).toBeInTheDocument();
  });

  it('marks an edited group', async () => {
    mockApi(() => ({ status: 200, body: withStatement(nestedDiscussion(), 'g2', { editedAt: edited }) }));
    await openDiscussion();

    expect(lineText('g2')).toBe('+ ANY SUFFICIENT: Evidence (edited) (holds)');
    expect(lineText('g1')).toBe('+ ALL REQUIRED: Productivity mechanisms (holds)');
  });

  it('renders a chain of 150 nested groups (Cluster B "UI nesting shown": deep, 100 or more levels)', async () => {
    mockApi(() => ({ status: 200, body: deepGroupDiscussion(150) }));
    await openDiscussion();

    expect(lineText('g150')).toBe('+ ANY SUFFICIENT: Level 150 (empty)');
    expect(outlineItem('g1').querySelectorAll('li[data-statement-id]')).toHaveLength(149);
    // Nested lists down to the nesting limit (the thesis is level 1, so g49 is level 50)...
    expect(parentItem('g49')).toBe(outlineItem('g48'));
    // ...then everything deeper is one flat list inside the last nested item, indented and marked with its level.
    expect(parentItem('g50')).toBe(outlineItem('g49'));
    expect(parentItem('g150')).toBe(outlineItem('g49'));
    expect(outlineItem('g150')).toHaveAttribute('aria-level', '151');
    expect(outlineItem('g150').querySelectorAll('li')).toHaveLength(0);
  });

  it('renders a chain of 1,000 nested groups without overflowing, and deep rows still edit', async () => {
    const before = deepGroupDiscussion(1000);
    const after = withStatement(before, 'g999', { name: 'Renamed', editedAt: edited });
    const fetchMock = serve(before, (_request, set) => {
      set(after);
      return { status: 200, body: after.statements.find((s) => s.id === 'g999') };
    });
    const { user } = await openDiscussion();

    expect(lineText('g1000')).toBe('+ ANY SUFFICIENT: Level 1000 (empty)');
    expect(outlineItem('g1000')).toHaveAttribute('aria-level', '1001');

    await user.click(row('g999').querySelector<HTMLButtonElement>('.outline-actions button')!);
    const name = outlineItem('g999').querySelector<HTMLInputElement>('input[type="text"]')!;
    expect(name).toHaveFocus();
    await user.clear(name);
    await user.type(name, 'Renamed');
    await user.click(outlineItem('g999').querySelector<HTMLButtonElement>('button[type="submit"]')!);

    await waitFor(() => expect(lineText('g999')).toBe('+ ALL REQUIRED: Renamed (edited) (empty)'));
    expect(bodies(fetchMock, 'PATCH')).toEqual([{ name: 'Renamed' }]);
  }, 60000);
});

describe('Unnamed migrated groups (UC-4 A1; Cluster B "Migrated null-name group")', () => {
  it('shows "Unnamed group" and an "Add a name" control', async () => {
    mockApi(() => ({ status: 200, body: nestedDiscussion() }));
    await openDiscussion();

    expect(within(row('g5')).getByText('Unnamed group')).toBeInTheDocument();
    expect(within(row('g5')).getByRole('button', { name: /^Add a name/ })).toBeInTheDocument();
    // Named groups have no such control.
    expect(within(row('g1')).queryByRole('button', { name: /^Add a name/ })).not.toBeInTheDocument();
    expect(screen.getAllByRole('button', { name: /^Add a name/ })).toHaveLength(1);
  });

  it('opens the edit form focused on the name, sends only the name and shows it after the reload', async () => {
    const before = nestedDiscussion();
    const after = withStatement(before, 'g5', { name: 'Flexibility', editedAt: edited });
    const fetchMock = serve(before, ({ method, path }, set) => {
      if (method === 'PATCH' && path === '/api/v1/discussions/d1/statements/g5') {
        set(after);
        return { status: 200, body: after.statements.find((s) => s.id === 'g5') };
      }
      return { status: 500 };
    });
    const { user } = await openDiscussion();

    await user.click(within(row('g5')).getByRole('button', { name: /^Add a name/ }));
    const form = within(outlineItem('g5')).getByRole('form', { name: 'Edit support group' });
    const name = within(form).getByLabelText('Name');
    expect(name).toHaveFocus();
    expect(name).toHaveValue('');
    expect(within(form).getByRole('radio', { name: /Any sufficient/ })).toBeChecked();
    await user.type(name, 'Flexibility');
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    await waitFor(() => expect(lineText('g5')).toBe('+ ANY SUFFICIENT: Flexibility (edited) (does not hold)'));
    expect(within(row('g5')).queryByText('Unnamed group')).not.toBeInTheDocument();
    expect(within(row('g5')).queryByRole('button', { name: /^Add a name/ })).not.toBeInTheDocument();
    expect(bodies(fetchMock, 'PATCH')).toEqual([{ name: 'Flexibility' }]);
    expect(screen.getByRole('status')).toHaveTextContent('Saved the support group.');
    // "Add a name" is gone, so focus goes to the row's Edit button.
    expect(within(row('g5')).getByRole('button', { name: /^Edit/ })).toHaveFocus();
  });

  it('Q-1: changing only the type of an unnamed group shows the API error next to the name field', async () => {
    const fetchMock = serve(nestedDiscussion(), () =>
      validationProblem({ name: ['Name is required: this group has no name yet.'] }),
    );
    const { user } = await openDiscussion();

    await user.click(within(row('g5')).getByRole('button', { name: /^Edit/ }));
    const form = within(outlineItem('g5')).getByRole('form', { name: 'Edit support group' });
    await user.click(within(form).getByRole('radio', { name: /All required/ }));
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    const name = within(form).getByLabelText('Name');
    await waitFor(() => expect(name).toHaveAttribute('aria-invalid', 'true'));
    expect(name).toHaveAccessibleDescription(expect.stringContaining('Name is required: this group has no name yet.'));
    expect(bodies(fetchMock, 'PATCH')).toEqual([{ groupType: 'allRequired' }]);
    expect(within(form).getByRole('radio', { name: /All required/ })).toBeChecked();
  });
});

describe('Adding a nested group (UC-4 steps 4 and 5; Cluster B "Add-form target kind")', () => {
  it('offers the thesis, every group and every supporting statement at any depth as targets', async () => {
    mockApi(() => ({ status: 200, body: nestedDiscussion() }));
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);

    const options = within(form).getAllByRole('option') as HTMLOptionElement[];
    expect(options.map((option) => option.value)).toEqual(['t', 'g1', 's1', 'g2', 's2', 's3', 'g3', 's4', 'g4', 's5', 'g5', 's6']);
    expect(options.map((option) => option.textContent)).toEqual([
      '[THESIS] Remote work boosts productivity',
      'ALL REQUIRED: Productivity mechanisms',
      '[SUPPORT] Fewer interruptions',
      'ANY SUFFICIENT: Evidence',
      '[SUPPORT] Studies show +13% output',
      '[SUPPORT] Survey of 500 managers',
      'ALL REQUIRED: Replicated studies',
      '[SUPPORT] No commute saves energy',
      'ALL REQUIRED: Why commuting drains energy',
      '[SUPPORT] Average commute is 54 minutes',
      'ANY SUFFICIENT: Unnamed group',
      '[SUPPORT] Flexible hours',
    ]);
    expect(within(form).getByLabelText('Name')).toHaveValue('');
    expect(within(form).getByRole('group', { name: 'Group type' })).toBeInTheDocument();
  });

  it('numbers target labels that would otherwise repeat', async () => {
    const discussion = nestedDiscussion();
    discussion.statements.push({ ...discussion.statements.find((s) => s.id === 'g5')!, id: 'g6', holds: undefined });
    mockApi(() => ({ status: 200, body: discussion }));
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);

    const labels = (within(form).getAllByRole('option') as HTMLOptionElement[]).map((option) => option.textContent);
    expect(labels).toContain('ANY SUFFICIENT: Unnamed group');
    expect(labels).toContain('ANY SUFFICIENT: Unnamed group (2)');
  });

  it.each([
    ['a nested group', 'g2', 'ANY SUFFICIENT: Evidence'],
    ['a nested supporting statement', 's5', '[SUPPORT] Average commute is 54 minutes'],
    ['the thesis', 't', '[THESIS] Remote work boosts productivity'],
  ])('adds a named group under %s and shows it there after the reload', async (_target, parentId, option) => {
    const before = nestedDiscussion();
    const added: Statement = {
      id: 'new',
      kind: 'supportGroup',
      parentId,
      groupType: 'anySufficient',
      name: 'Peer review',
      createdAt: edited,
      editedAt: null,
    };
    const fetchMock = serve(before, ({ method }, set) => {
      if (method === 'POST') {
        set({ ...before, statements: [...before.statements, added] });
        return { status: 201, body: added };
      }
      return { status: 500 };
    });
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);
    await user.selectOptions(within(form).getByLabelText('Supports'), option);
    await user.type(within(form).getByLabelText('Name'), 'Peer review');
    await user.click(within(form).getByRole('radio', { name: /Any sufficient/ }));
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));

    await waitFor(() => expect(document.querySelector('[data-statement-id="new"]')).not.toBeNull());
    expect(parentItem('new')).toBe(outlineItem(parentId));
    expect(lineText('new')).toBe('+ ANY SUFFICIENT: Peer review (empty)');
    expect(screen.getByRole('status')).toHaveTextContent('Added the support group.');
    expect(screen.queryByRole('form', { name: 'Add support group' })).not.toBeInTheDocument();
    const [post] = calls(fetchMock, 'POST');
    expect(post[0]).toBe('/api/v1/discussions/d1/statements');
    expect(JSON.parse(post[1]!.body as string)).toEqual({ kind: 'supportGroup', parentId, name: 'Peer review', groupType: 'anySufficient' });
  });
});

describe('Editing a group (UC-4 steps 6 and 7)', () => {
  it('opens a form with the current name and type, and no text field', async () => {
    mockApi(() => ({ status: 200, body: nestedDiscussion() }));
    const { user } = await openDiscussion();

    await user.click(within(row('g2')).getByRole('button', { name: 'Edit support group (ANY SUFFICIENT: Evidence)' }));

    const form = within(outlineItem('g2')).getByRole('form', { name: 'Edit support group' });
    expect(within(form).getByLabelText('Name')).toHaveValue('Evidence');
    expect(within(form).getByLabelText('Name')).toHaveFocus();
    expect(within(form).getByRole('radio', { name: /Any sufficient/ })).toBeChecked();
    expect(within(form).getByRole('radio', { name: /All required/ })).not.toBeChecked();
    expect(within(form).queryByLabelText('Text')).not.toBeInTheDocument();
    expect(within(form).queryByRole('textbox', { name: /text/i })).not.toBeInTheDocument();
    // The nested group and supports beneath stay in place while editing.
    expect(within(outlineItem('g2')).getByText('Replicated studies')).toBeInTheDocument();
  });

  it.each([
    ['only the name', { name: 'Hard evidence' }, 'ANY SUFFICIENT: Hard evidence'],
    ['only the type', { groupType: 'allRequired' as const }, 'ALL REQUIRED: Evidence'],
    ['both', { name: 'Hard evidence', groupType: 'allRequired' as const }, 'ALL REQUIRED: Hard evidence'],
  ])('changing %s sends just that and shows the new values and recomputed statuses', async (_change, sent, label) => {
    const before = nestedDiscussion();
    // With the type now ALL REQUIRED, the countered survey makes Evidence fail, and with it its parent and the thesis.
    let after = withStatement(before, 'g2', { ...sent, editedAt: edited, holds: 'groupType' in sent ? false : true });
    if ('groupType' in sent) {
      after = { ...withStatement(after, 'g1', { holds: false }), status: 'countered' };
    }
    const fetchMock = serve(before, ({ method, path }, set) => {
      if (method === 'PATCH' && path === '/api/v1/discussions/d1/statements/g2') {
        set(after);
        return { status: 200, body: after.statements.find((s) => s.id === 'g2') };
      }
      return { status: 500 };
    });
    const { user } = await openDiscussion();

    await user.click(within(row('g2')).getByRole('button', { name: /^Edit/ }));
    const form = within(outlineItem('g2')).getByRole('form', { name: 'Edit support group' });
    if ('name' in sent) {
      await user.clear(within(form).getByLabelText('Name'));
      await user.type(within(form).getByLabelText('Name'), sent.name);
    }
    if ('groupType' in sent) {
      await user.click(within(form).getByRole('radio', { name: /All required/ }));
    }
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    const marker = 'groupType' in sent ? 'does not hold' : 'holds';
    await waitFor(() => expect(lineText('g2')).toBe(`+ ${label} (edited) (${marker})`));
    expect(bodies(fetchMock, 'PATCH')).toEqual([sent]);
    expect(bodies(fetchMock, 'PATCH').some((body) => 'text' in (body as object))).toBe(false);
    if ('groupType' in sent) {
      expect(lineText('g1')).toBe('+ ALL REQUIRED: Productivity mechanisms (does not hold)');
      expect(within(outlineItem('t')).getByText('COUNTERED')).toBeInTheDocument();
    } else {
      expect(within(outlineItem('t')).getByText('TESTED')).toBeInTheDocument();
    }
    expect(screen.queryByRole('form', { name: 'Edit support group' })).not.toBeInTheDocument();
    expect(within(row('g2')).getByRole('button', { name: /^Edit/ })).toHaveFocus();
  });

  it('saving with nothing changed sends no request and closes the form', async () => {
    const fetchMock = serve(nestedDiscussion(), () => ({ status: 500 }));
    const { user } = await openDiscussion();

    await user.click(within(row('g1')).getByRole('button', { name: /^Edit/ }));
    await user.click(within(outlineItem('g1')).getByRole('button', { name: 'Save' }));

    expect(screen.queryByRole('form', { name: 'Edit support group' })).not.toBeInTheDocument();
    expect(calls(fetchMock, 'PATCH')).toHaveLength(0);
  });
});

describe('Cancelling (UC-4 A2)', () => {
  it('cancelling the add-group form sends nothing and returns focus to "Add support group"', async () => {
    const fetchMock = serve(nestedDiscussion(), () => ({ status: 500 }));
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);
    await user.type(within(form).getByLabelText('Name'), 'Draft');
    await user.click(within(form).getByRole('button', { name: 'Cancel' }));

    expect(screen.queryByRole('form', { name: 'Add support group' })).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add support group' })).toHaveFocus();
    expect(calls(fetchMock, 'POST')).toHaveLength(0);
  });

  it('cancelling a group edit sends nothing, keeps the values and returns focus to Edit', async () => {
    const fetchMock = serve(nestedDiscussion(), () => ({ status: 500 }));
    const { user } = await openDiscussion();

    await user.click(within(row('g3')).getByRole('button', { name: /^Edit/ }));
    const form = within(outlineItem('g3')).getByRole('form', { name: 'Edit support group' });
    await user.type(within(form).getByLabelText('Name'), ' and more');
    await user.click(within(form).getByRole('radio', { name: /Any sufficient/ }));
    await user.click(within(form).getByRole('button', { name: 'Cancel' }));

    expect(lineText('g3')).toBe('+ ALL REQUIRED: Replicated studies (empty)');
    expect(within(row('g3')).getByRole('button', { name: /^Edit/ })).toHaveFocus();
    expect(calls(fetchMock, 'PATCH')).toHaveLength(0);
  });

  it('cancelling "Add a name" sends nothing and returns focus to "Add a name"', async () => {
    const fetchMock = serve(nestedDiscussion(), () => ({ status: 500 }));
    const { user } = await openDiscussion();

    await user.click(within(row('g5')).getByRole('button', { name: /^Add a name/ }));
    await user.click(within(outlineItem('g5')).getByRole('button', { name: 'Cancel' }));

    expect(within(row('g5')).getByRole('button', { name: /^Add a name/ })).toHaveFocus();
    expect(calls(fetchMock, 'PATCH')).toHaveLength(0);
  });
});

describe('API errors on the add-group form (UC-4 E1 to E4; Cluster B "API error field returned to UI")', () => {
  it.each([
    ['a whitespace-only name', '   '],
    ['a 101-character name', 'x'.repeat(101)],
  ])('shows a 400 naming name for %s next to the name field and keeps the input', async (_case, typed) => {
    const fetchMock = serve(nestedDiscussion(), () => validationProblem({ name: ['Name must be 1 to 100 characters.'] }));
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);
    await user.selectOptions(within(form).getByLabelText('Supports'), 'g2');
    await user.click(within(form).getByLabelText('Name'));
    await user.paste(typed);
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));

    const name = within(form).getByLabelText('Name');
    await waitFor(() => expect(name).toHaveAttribute('aria-invalid', 'true'));
    expect(name).toHaveAccessibleDescription(expect.stringContaining('Name must be 1 to 100 characters.'));
    expect(name).toHaveValue(typed);
    expect(within(form).getByLabelText('Supports')).toHaveValue('g2');
    expect(screen.getByRole('form', { name: 'Add support group' })).toBeInTheDocument();
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
    // The name is sent as typed: the server owns the length and whitespace rule (Q-4).
    expect(bodies(fetchMock, 'POST')).toEqual([{ kind: 'supportGroup', parentId: 'g2', name: typed, groupType: 'allRequired' }]);
  });

  it('shows a 400 naming parentId (target deleted meanwhile) next to the target picker', async () => {
    serve(nestedDiscussion(), () => validationProblem({ parentId: ['The parent statement does not exist.'] }));
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);
    await user.selectOptions(within(form).getByLabelText('Supports'), 's5');
    await user.type(within(form).getByLabelText('Name'), 'Sources');
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));

    const target = within(form).getByLabelText('Supports');
    await waitFor(() => expect(target).toHaveAttribute('aria-invalid', 'true'));
    expect(target).toHaveAccessibleDescription('The parent statement does not exist.');
    expect(within(form).getByLabelText('Name')).not.toHaveAttribute('aria-invalid');
  });

  it('shows a 400 naming groupType next to the type choice', async () => {
    serve(nestedDiscussion(), () => validationProblem({ groupType: ['Group type must be one of: allRequired, anySufficient.'] }));
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);
    await user.type(within(form).getByLabelText('Name'), 'Sources');
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));

    const types = within(form).getByRole('group', { name: 'Group type' });
    await waitFor(() => expect(types).toHaveAccessibleDescription('Group type must be one of: allRequired, anySufficient.'));
  });

  it('shows several field errors at once, each next to its own field', async () => {
    serve(nestedDiscussion(), () =>
      validationProblem({
        name: ['Name is required.'],
        parentId: ['A support group must target the thesis, a support group or a supporting statement.'],
        groupType: ['Group type is required.'],
      }),
    );
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));

    const name = within(form).getByLabelText('Name');
    await waitFor(() => expect(name).toHaveAttribute('aria-invalid', 'true'));
    expect(name).toHaveAccessibleDescription(expect.stringContaining('Name is required.'));
    expect(within(form).getByLabelText('Supports')).toHaveAccessibleDescription(
      'A support group must target the thesis, a support group or a supporting statement.',
    );
    expect(within(form).getByRole('group', { name: 'Group type' })).toHaveAccessibleDescription('Group type is required.');
    expect(within(form).queryByRole('alert')).not.toBeInTheDocument();
  });

  it('shows a form-level alert for a network failure', async () => {
    serve(nestedDiscussion(), () => networkFailure());
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);
    await user.type(within(form).getByLabelText('Name'), 'Sources');
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));

    expect(await within(form).findByRole('alert')).toHaveTextContent('Could not reach the server.');
    expect(within(form).getByLabelText('Name')).toHaveValue('Sources');
  });

  it('shows a form-level alert for a 400 with no field errors', async () => {
    serve(nestedDiscussion(), () => ({ status: 400, body: { title: 'The request is invalid.', status: 400 } }));
    const { user } = await openDiscussion();

    const form = await openAddGroupForm(user);
    await user.type(within(form).getByLabelText('Name'), 'Sources');
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));

    expect(await within(form).findByRole('alert')).toHaveTextContent('The request is invalid.');
  });
});

describe('API errors on the group edit form (UC-4 E1, E3 and E4)', () => {
  it.each([
    ['a whitespace-only name', '   '],
    ['a 101-character name', 'y'.repeat(101)],
  ])('shows a 400 naming name for %s next to the name field and keeps the form open', async (_case, typed) => {
    const fetchMock = serve(nestedDiscussion(), () => validationProblem({ name: ['Name must be 1 to 100 characters.'] }));
    const { user } = await openDiscussion();

    await user.click(within(row('g1')).getByRole('button', { name: /^Edit/ }));
    const form = within(outlineItem('g1')).getByRole('form', { name: 'Edit support group' });
    await user.clear(within(form).getByLabelText('Name'));
    await user.paste(typed);
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    const name = within(form).getByLabelText('Name');
    await waitFor(() => expect(name).toHaveAttribute('aria-invalid', 'true'));
    expect(name).toHaveAccessibleDescription(expect.stringContaining('Name must be 1 to 100 characters.'));
    expect(name).toHaveValue(typed);
    expect(within(outlineItem('g1')).getByRole('form', { name: 'Edit support group' })).toBeInTheDocument();
    expect(bodies(fetchMock, 'PATCH')).toEqual([{ name: typed }]);
  });

  it('shows a 400 naming groupType next to the type choice', async () => {
    serve(nestedDiscussion(), () => validationProblem({ groupType: ['Group type must be one of: allRequired, anySufficient.'] }));
    const { user } = await openDiscussion();

    await user.click(within(row('g1')).getByRole('button', { name: /^Edit/ }));
    const form = within(outlineItem('g1')).getByRole('form', { name: 'Edit support group' });
    await user.click(within(form).getByRole('radio', { name: /Any sufficient/ }));
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    const types = within(form).getByRole('group', { name: 'Group type' });
    await waitFor(() => expect(types).toHaveAccessibleDescription('Group type must be one of: allRequired, anySufficient.'));
  });

  it('shows a field error the form has no control for (text) in a form-level alert', async () => {
    serve(nestedDiscussion(), () => validationProblem({ text: ['A support group has no text.'] }));
    const { user } = await openDiscussion();

    await user.click(within(row('g1')).getByRole('button', { name: /^Edit/ }));
    const form = within(outlineItem('g1')).getByRole('form', { name: 'Edit support group' });
    await user.type(within(form).getByLabelText('Name'), '!');
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    expect(await within(form).findByRole('alert')).toHaveTextContent('A support group has no text.');
  });

  it('shows a form-level alert with Retry for a network failure, and Retry saves', async () => {
    const after = withStatement(nestedDiscussion(), 'g1', { name: 'Mechanisms', editedAt: edited });
    let attempts = 0;
    const fetchMock = serve(nestedDiscussion(), ({ method }, set) => {
      if (method !== 'PATCH') return { status: 500 };
      attempts += 1;
      if (attempts === 1) networkFailure();
      set(after);
      return { status: 200, body: after.statements.find((s) => s.id === 'g1') };
    });
    const { user } = await openDiscussion();

    await user.click(within(row('g1')).getByRole('button', { name: /^Edit/ }));
    const form = within(outlineItem('g1')).getByRole('form', { name: 'Edit support group' });
    await user.clear(within(form).getByLabelText('Name'));
    await user.type(within(form).getByLabelText('Name'), 'Mechanisms');
    await user.click(within(form).getByRole('button', { name: 'Save' }));

    const alert = await within(form).findByRole('alert');
    expect(alert).toHaveTextContent('Could not save the support group. Could not reach the server.');
    expect(within(form).getByLabelText('Name')).toHaveValue('Mechanisms');
    await user.click(within(alert).getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(lineText('g1')).toBe('+ ALL REQUIRED: Mechanisms (edited) (holds)'));
    expect(bodies(fetchMock, 'PATCH')).toEqual([{ name: 'Mechanisms' }, { name: 'Mechanisms' }]);
  });
});
