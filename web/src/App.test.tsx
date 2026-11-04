import { screen } from '@testing-library/react';
import { renderAt } from './test/render';

describe('App', () => {
  it('shows a not-found page for unknown routes', () => {
    renderAt('/no/such/page');

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'Back to all discussions' })).toHaveAttribute('href', '/');
  });
});
