import { render, screen, within } from '@testing-library/react';
import { axe } from 'vitest-axe';
import { deepGroupDiscussion, mockApi, nestedDiscussion, validationProblem } from './fakeApi';
import { renderAt } from './render';

describe('accessibility check wiring', () => {
  it('reports violations, so passing view checks are meaningful', async () => {
    const { container } = render(
      <main>
        <img src="photo.png" />
        <button type="button" />
      </main>,
    );

    const results = await axe(container);

    expect(results.violations.map((violation) => violation.id)).toEqual(expect.arrayContaining(['image-alt', 'button-name']));
    expect(results).not.toHaveNoViolations();
  });
});

// AC-4 (UC-4 postconditions; Cluster B "Accessibility checks"): nested and unnamed groups, the add-group form and the
// group edit form, each in its normal and its error state.
describe('accessibility of nested and named groups', () => {
  function groupRow(id: string): HTMLElement {
    return document.querySelector<HTMLElement>(`[data-statement-id="${id}"] > .outline-row`)!;
  }

  async function openNestedDiscussion() {
    mockApi(({ method }) =>
      method === 'GET'
        ? { status: 200, body: nestedDiscussion() }
        : validationProblem({
            name: ['Name must be 1 to 100 characters.'],
            groupType: ['Group type must be one of: allRequired, anySufficient.'],
            parentId: ['The parent statement does not exist.'],
          }),
    );
    const rendered = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });
    return rendered;
  }

  it('has no violations on the outline with nested and unnamed groups', async () => {
    const { container } = await openNestedDiscussion();

    expect(screen.getByText('Unnamed group')).toBeInTheDocument();
    expect(await axe(container)).toHaveNoViolations();
  });

  it('has no violations on the add-group form, before and after API errors', async () => {
    const { container, user } = await openNestedDiscussion();

    await user.click(screen.getByRole('button', { name: 'Add support group' }));
    const form = screen.getByRole('form', { name: 'Add support group' });
    expect(await axe(container)).toHaveNoViolations();

    await user.type(within(form).getByLabelText('Name'), '   ');
    await user.click(within(form).getByRole('button', { name: 'Post support group' }));
    await within(form).findByText('Name must be 1 to 100 characters.');
    expect(await axe(container)).toHaveNoViolations();
  });

  it('has no violations on the group edit form, before and after API errors', async () => {
    const { container, user } = await openNestedDiscussion();

    await user.click(within(groupRow('g2')).getByRole('button', { name: /^Edit/ }));
    const form = screen.getByRole('form', { name: 'Edit support group' });
    expect(await axe(container)).toHaveNoViolations();

    await user.clear(within(form).getByLabelText('Name'));
    await user.click(within(form).getByRole('button', { name: 'Save' }));
    await within(form).findByText('Name must be 1 to 100 characters.');
    expect(await axe(container)).toHaveNoViolations();
  });

  it('has no violations on the "Add a name" form for an unnamed group, before and after API errors', async () => {
    const { container, user } = await openNestedDiscussion();

    await user.click(within(groupRow('g5')).getByRole('button', { name: /^Add a name/ }));
    const form = screen.getByRole('form', { name: 'Edit support group' });
    expect(await axe(container)).toHaveNoViolations();

    await user.type(within(form).getByLabelText('Name'), ' ');
    await user.click(within(form).getByRole('button', { name: 'Save' }));
    await within(form).findByText('Group type must be one of: allRequired, anySufficient.');
    expect(await axe(container)).toHaveNoViolations();
  });
});

describe('accessibility of a deep outline', () => {
  it('has no violations past the nesting limit, where deeper statements form a flat list with aria-level', async () => {
    mockApi(() => ({ status: 200, body: deepGroupDiscussion(60) }));
    const { container } = renderAt('/discussions/d1');
    await screen.findByRole('list', { name: 'Argument outline' });

    expect(container.querySelector('[data-statement-id="g60"]')).toHaveAttribute('aria-level', '61');
    expect(await axe(container)).toHaveNoViolations();
  });
});
