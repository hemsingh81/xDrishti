import { render, screen } from '@testing-library/react';
import { ErrorState, LoadingState } from './QueryState';

describe('QueryState live regions', () => {
  it('shows what is loading in a status region that stays silent: the page-level announcer speaks once for all panels', () => {
    render(<LoadingState label="Loading the stored runs…" />);
    const status = screen.getByRole('status');
    expect(status).toHaveTextContent('Loading the stored runs…');
    expect(status).toHaveAttribute('aria-live', 'off');
    // The label is announced once: the spinner is hidden from assistive technology and carries no name of its own.
    expect(screen.getAllByText('Loading the stored runs…')).toHaveLength(1);
    expect(screen.queryByRole('progressbar')).not.toBeInTheDocument();
  });

  it('announces itself politely where no page-level announcer exists (the sign-in check)', () => {
    render(<LoadingState label="Checking your session…" announce />);
    expect(screen.getByRole('status')).toHaveAttribute('aria-live', 'polite');
  });

  it('announces a failed request through an alert', () => {
    render(<ErrorState error={new Error('The service is not answering.')} />);
    expect(screen.getByRole('alert')).toHaveTextContent('The service is not answering.');
  });
});
