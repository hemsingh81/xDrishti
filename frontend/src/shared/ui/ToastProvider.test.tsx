import { act, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { AppThemeProvider } from '@/theme';
import { ToastProvider } from './ToastProvider';
import { useToast, type ToastSeverity } from './toastContext';

function Trigger({ severity }: { severity: ToastSeverity }) {
  const { notify } = useToast();
  return (
    <button
      type="button"
      onClick={() => {
        notify('Saved it', severity);
      }}
    >
      Go
    </button>
  );
}

describe('ToastProvider', () => {
  it.each([
    ['success', 'green'],
    ['info', 'indigo'],
    ['warning', 'amber'],
    ['error', 'rose'],
  ] as const)('announces a %s toast with a %s status chip', async (severity, hue) => {
    render(
      <AppThemeProvider>
        <ToastProvider>
          <Trigger severity={severity} />
        </ToastProvider>
      </AppThemeProvider>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Go' }));
    const toast = await screen.findByRole('alert');
    expect(toast).toHaveTextContent('Saved it');
    expect(toast.querySelector(`[data-hue="${hue}"]`)).toBeInTheDocument();
  });

  it('shows a button in the toast, runs it once and closes the toast', async () => {
    const undo = vi.fn();
    function WithUndo() {
      const { notify } = useToast();
      return (
        <button
          type="button"
          onClick={() => {
            notify('Moved 2 instruments', 'success', { label: 'Undo', onClick: undo });
          }}
        >
          Go
        </button>
      );
    }
    render(
      <AppThemeProvider>
        <ToastProvider>
          <WithUndo />
        </ToastProvider>
      </AppThemeProvider>,
    );
    await userEvent.click(screen.getByRole('button', { name: 'Go' }));
    expect(await screen.findByRole('alert')).toHaveTextContent('Moved 2 instruments');

    await userEvent.click(screen.getByRole('button', { name: 'Undo' }));

    expect(undo).toHaveBeenCalledTimes(1);
    await waitFor(() => {
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
  });
});

describe('ToastProvider: toast with an Undo button', () => {
  const setup = (undo = vi.fn()) => {
    function Page() {
      const { notify } = useToast();
      return (
        <>
          <input aria-label="Notes" />
          <button
            type="button"
            onClick={() => {
              notify('Moved 2 instruments', 'success', { label: 'Undo', onClick: undo });
            }}
          >
            Go
          </button>
        </>
      );
    }
    render(
      <AppThemeProvider>
        <ToastProvider>
          <Page />
        </ToastProvider>
      </AppThemeProvider>,
    );
    return { undo, go: screen.getByRole('button', { name: 'Go' }) };
  };

  it('moves focus to Undo when the keyboard started the action, and gives it back when the toast closes', async () => {
    const { go } = setup();
    go.focus();

    await userEvent.keyboard('{Enter}');

    const undo = await screen.findByRole('button', { name: 'Undo' });
    await waitFor(() => {
      expect(undo).toHaveFocus();
    });
    expect(undo).toHaveAttribute('aria-keyshortcuts', 'Control+Z Meta+Z');

    await userEvent.keyboard('{Escape}');

    await waitFor(() => {
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
    expect(go).toHaveFocus();
  });

  it('runs Undo from the keyboard and gives focus back', async () => {
    const { go, undo } = setup();
    go.focus();
    await userEvent.keyboard('{Enter}');
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'Undo' })).toHaveFocus();
    });

    await userEvent.keyboard('{Enter}');

    expect(undo).toHaveBeenCalledTimes(1);
    expect(go).toHaveFocus();
  });

  it('does not take focus when the mouse started the action', async () => {
    const { go } = setup();

    await userEvent.click(go);

    await screen.findByRole('button', { name: 'Undo' });
    expect(go).toHaveFocus();
  });

  it.each([
    ['Ctrl+Z', '{Control>}z{/Control}'],
    ['Cmd+Z', '{Meta>}z{/Meta}'],
  ])('undoes with %s while the toast is open, once', async (_name, keys) => {
    const { go, undo } = setup();
    await userEvent.click(go);
    await screen.findByRole('button', { name: 'Undo' });
    expect(screen.getByText('Ctrl/⌘+Z')).toBeInTheDocument();

    await userEvent.keyboard(keys);
    await userEvent.keyboard(keys);

    expect(undo).toHaveBeenCalledTimes(1);
    await waitFor(() => {
      expect(screen.queryByRole('alert')).not.toBeInTheDocument();
    });
  });

  it('leaves Ctrl+Z to a text field, which undoes its own typing', async () => {
    const { go, undo } = setup();
    await userEvent.click(go);
    await screen.findByRole('button', { name: 'Undo' });
    await userEvent.click(screen.getByRole('textbox', { name: 'Notes' }));

    await userEvent.keyboard('{Control>}z{/Control}');

    expect(undo).not.toHaveBeenCalled();
    expect(screen.getByRole('button', { name: 'Undo' })).toBeInTheDocument();
  });

  describe('timing', () => {
    beforeEach(() => {
      vi.useFakeTimers({ shouldAdvanceTime: true });
    });
    afterEach(() => {
      vi.useRealTimers();
    });

    it('stays open while it has focus, and closes on its own once it has not', async () => {
      const { go } = setup();
      go.focus();
      await userEvent.keyboard('{Enter}');
      await waitFor(() => {
        expect(screen.getByRole('button', { name: 'Undo' })).toHaveFocus();
      });

      act(() => {
        vi.advanceTimersByTime(60_000);
      });
      expect(screen.getByRole('alert')).toBeInTheDocument();

      // Moving on (Tab leaves the toast) starts the countdown again.
      await userEvent.tab();
      act(() => {
        vi.advanceTimersByTime(11_000);
      });
      await waitFor(() => {
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      });
    });

    it('stays open while the pointer is over it', async () => {
      const { go } = setup();
      await userEvent.click(go);
      const toast = await screen.findByRole('alert');

      await userEvent.hover(toast);
      act(() => {
        vi.advanceTimersByTime(60_000);
      });
      expect(screen.getByRole('alert')).toBeInTheDocument();

      await userEvent.unhover(toast);
      act(() => {
        vi.advanceTimersByTime(11_000);
      });
      await waitFor(() => {
        expect(screen.queryByRole('alert')).not.toBeInTheDocument();
      });
    });
  });
});
