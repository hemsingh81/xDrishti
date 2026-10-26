import { render, screen } from '@testing-library/react';
import { ErrorState, LoadingState } from './QueryState';

describe('QueryState live regions', () => {
  it('announces what is loading through a polite status region, not only a spinner', () => {
    render(<LoadingState label="Loading the stored runs…" />);
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Loading the stored runs…');
    expect(status).toHaveAttribute('aria-live', 'polite');
    expect(screen.getByRole('progressbar', { name: 'Loading the stored runs…' })).toBeInTheDocument();
  });

  it('announces a failed request through an alert', () => {
    render(<ErrorState error={new Error('The service is not answering.')} />);
    expect(screen.getByRole('alert')).toHaveTextContent('The service is not answering.');
  });
});
