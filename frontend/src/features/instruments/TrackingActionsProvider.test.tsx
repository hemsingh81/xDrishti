import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ToastProvider } from '@/shared/ui/ToastProvider';
import { renderWithProviders } from '@/test/render';
import { server } from '@/test/server';
import { createTrackingFake } from '@/test/trackingFake';
import { trackedSample } from './trackingTestkit';
import { TrackingActionsProvider } from './components/TrackingActionsProvider';
import { useTrackingActions } from './hooks/trackingActionsContext';

const answers: boolean[] = [];

function Asker() {
  const { untrack } = useTrackingActions();
  return (
    <>
      {['first', 'second'].map((name) => (
        <button
          key={name}
          type="button"
          onClick={() => {
            void untrack([`id-${name}`], name).then((done) => answers.push(done));
          }}
        >
          Ask {name}
        </button>
      ))}
    </>
  );
}

describe('TrackingActionsProvider: asking to untrack twice', () => {
  beforeEach(() => {
    answers.length = 0;
    server.use(...createTrackingFake(trackedSample).handlers);
  });

  it('answers the first question "no" when a second one replaces it, so it never hangs', async () => {
    renderWithProviders(
      <ToastProvider>
        <TrackingActionsProvider>
          <Asker />
        </TrackingActionsProvider>
      </ToastProvider>,
    );

    await userEvent.click(screen.getByRole('button', { name: 'Ask first' }));
    await screen.findByRole('dialog', { name: 'Untrack this instrument?' });
    // The dialog is modal, so a second call can only come from code: fire it straight through the same button.
    screen.getByRole('button', { name: 'Ask second', hidden: true }).click();

    await waitFor(() => {
      expect(answers).toEqual([false]);
    });
    expect(screen.getByRole('dialog')).toHaveTextContent('Collecting data and scans stop for second.');

    await userEvent.click(screen.getByRole('button', { name: 'Cancel' }));

    await waitFor(() => {
      expect(answers).toEqual([false, false]);
    });
  });
});
