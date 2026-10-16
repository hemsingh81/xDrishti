import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useState } from 'react';
import { useSyncBarFocus } from './useSyncBarFocus';
import { useSyncFocusRequest } from './useSyncFocusRequest';

/** A page whose sync bar appears only when "Show the bar" is pressed, as one that is not mounted yet. */
function Page({ requested, onHandled }: { requested: boolean; onHandled: () => void }) {
  const [shown, setShown] = useState(false);
  const [barRef, , focus] = useSyncBarFocus();
  useSyncFocusRequest(requested, focus, onHandled);
  return (
    <>
      <button
        onClick={() => {
          setShown(true);
        }}
      >
        Show the bar
      </button>
      {shown && (
        <div ref={barRef} tabIndex={-1}>
          <section aria-label="Trades and ledger from Dhan">
            <button>Sync trades & ledger</button>
          </section>
        </div>
      )}
    </>
  );
}

describe('useSyncFocusRequest', () => {
  it('keeps the request while the bar is not there, and focuses it (then hands the request back) when it appears', async () => {
    const handled = vi.fn();
    render(<Page requested onHandled={handled} />);
    expect(handled).not.toHaveBeenCalled();

    await userEvent.click(screen.getByRole('button', { name: 'Show the bar' }));

    expect(await screen.findByRole('button', { name: 'Sync trades & ledger' })).toHaveFocus();
    expect(handled).toHaveBeenCalledTimes(1);
  });

  it('does nothing without a request', async () => {
    const handled = vi.fn();
    render(<Page requested={false} onHandled={handled} />);
    await userEvent.click(screen.getByRole('button', { name: 'Show the bar' }));

    expect(screen.getByRole('button', { name: 'Sync trades & ledger' })).not.toHaveFocus();
    expect(handled).not.toHaveBeenCalled();
  });
});
