import { render } from '@testing-library/react';
import { axe } from 'vitest-axe';

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
