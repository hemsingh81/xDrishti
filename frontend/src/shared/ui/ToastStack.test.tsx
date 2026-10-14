import { act, fireEvent, render, screen, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useEffect } from 'react';
import { appliedCss } from '@/test/css';
import { findToast } from '@/test/toast';
import { stubReducedMotion } from '@/test/matchMedia';
import { AppThemeProvider, palettes } from '@/theme';
import { ToastProvider } from './ToastProvider';
import { useToast, type ToastApi } from './toastContext';

/** Gives the test the provider's API, so a toast can be raised without a button to click. */
function setup() {
  const handle: { api: ToastApi | null } = { api: null };
  function Capture() {
    const toast = useToast();
    useEffect(() => {
      handle.api = toast;
    }, [toast]);
    return <input aria-label="Notes" />;
  }
  const view = render(
    <AppThemeProvider>
      <ToastProvider>
        <Capture />
      </ToastProvider>
    </AppThemeProvider>,
  );
  const api = (): ToastApi => {
    if (!handle.api) throw new Error('No provider');
    return handle.api;
  };
  return {
    ...view,
    notify: (...args: Parameters<ToastApi['notify']>) => {
      act(() => {
        api().notify(...args);
      });
    },
    dismiss: (id: string) => {
      act(() => {
        api().dismiss(id);
      });
    },
  };
}

/** Moves the clock, then once more for the exit animation (a closing toast leaves the page 160 ms after it closes). */
const wait = (ms: number) => {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
  act(() => {
    vi.advanceTimersByTime(200);
  });
};
const toasts = () => within(screen.getByRole('region', { name: 'Notifications' }));
/** Text of the toasts on the page, top to bottom (buttons included). */
const texts = () => {
  const stack = screen.queryByRole('region', { name: 'Notifications' });
  if (!stack) return [];
  const open = new Set([...within(stack).queryAllByRole('status'), ...within(stack).queryAllByRole('alert')]);
  return Array.from(stack.children)
    .filter((card) => open.has(card as HTMLElement))
    .map((card) => card.textContent);
};

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
});
afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('ToastProvider: the stack', () => {
  it('shows toasts one above the other, the newest at the bottom', () => {
    const { notify } = setup();

    notify('First');
    notify('Second');
    notify('Third');

    expect(texts()).toEqual(['First', 'Second', 'Third']);
  });

  it('is found by the test helper as the newest in document order, whatever its role', async () => {
    const { notify } = setup();

    notify('Saved');
    notify('Could not save', 'error');

    expect(await findToast()).toHaveTextContent('Could not save');
    notify('Saved again');
    expect(await findToast()).toHaveTextContent('Saved again');
  });

  it('shows at most three: a fourth makes the oldest leave', () => {
    const { notify } = setup();

    for (const message of ['One', 'Two', 'Three', 'Four']) notify(message);

    expect(texts()).toEqual(['Two', 'Three', 'Four']);
  });

  it('lets a timed toast make room before a persistent one', () => {
    const { notify } = setup();
    notify('Reconnect', 'error', { persistent: true, id: 'a' });
    notify('Saved');
    notify('Needs review', 'warning', { persistent: true, id: 'b' });

    notify('Moved');

    expect(texts()).toEqual(['Reconnect', 'Needs review', 'Moved']);
  });

  it('lets the oldest persistent toast go when all are persistent', () => {
    const { notify } = setup();
    for (const id of ['a', 'b', 'c', 'd']) notify(`Item ${id}`, 'info', { persistent: true, id });

    expect(texts()).toEqual(['Item b', 'Item c', 'Item d']);
  });

  it('closes each toast on its own time, 5 s plain and 10 s with a button', () => {
    const { notify } = setup();
    notify('Plain');
    notify('With button', 'success', { actions: [{ label: 'Open', onClick: vi.fn() }] });

    act(() => {
      vi.advanceTimersByTime(4900);
    });
    expect(texts()).toHaveLength(2);

    wait(200);
    expect(texts()).toEqual(['With buttonOpen']);

    wait(5000);
    expect(screen.queryByRole('region', { name: 'Notifications' })).not.toBeInTheDocument();
  });

  it('takes a custom time', () => {
    const { notify } = setup();
    notify('Quick', 'info', { durationMs: 1000 });

    wait(1000);

    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('keeps a persistent toast until it is closed', async () => {
    const { notify } = setup();
    notify('Dhan login expired', 'error', { persistent: true });

    wait(120_000);
    const toast = screen.getByRole('alert');
    expect(toast).toHaveTextContent('Dhan login expired');
    expect(toast.querySelector('[data-countdown]')).toBeNull();

    await userEvent.click(within(toast).getByRole('button', { name: 'Close notification' }));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('replaces a toast that has the same id, in place, and starts its time again', () => {
    const { notify } = setup();
    notify('Trades: reading 1 of 3', 'info', { id: 'sync' });
    notify('Other');
    act(() => {
      vi.advanceTimersByTime(4000);
    });

    notify('Trades: reading 2 of 3', 'info', { id: 'sync' });
    act(() => {
      vi.advanceTimersByTime(4000);
    });

    expect(texts()).toEqual(['Trades: reading 2 of 3']);
    wait(1000);
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('closes a toast by its id', () => {
    const { notify, dismiss } = setup();
    notify('Reconnect Dhan', 'error', { persistent: true, id: 'reconnect' });
    notify('Saved');

    dismiss('reconnect');
    dismiss('nobody');

    expect(texts()).toEqual(['Saved']);
  });

  it('closes the newest toast with Escape', async () => {
    const { notify } = setup();
    notify('Older');
    notify('Newer');

    await userEvent.keyboard('{Escape}');
    expect(texts()).toEqual(['Older']);

    await userEvent.keyboard('{Escape}');
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('leaves the toast alone when something else already handled Escape', async () => {
    const { notify } = setup();
    notify('Saved');
    const field = screen.getByLabelText('Notes');
    field.addEventListener('keydown', (event) => {
      if (event.key === 'Escape') event.preventDefault();
    });
    field.focus();

    await userEvent.keyboard('{Escape}');

    expect(texts()).toEqual(['Saved']);
  });
});

describe('ToastProvider: content and announcement', () => {
  it('shows a title, the message, at most two buttons and a close button; a button runs and closes the toast', async () => {
    const open = vi.fn();
    const retry = vi.fn();
    const third = vi.fn();
    const { notify } = setup();
    notify('Trades have not been read for 2 days.', 'error', {
      title: 'A sync keeps failing',
      persistent: true,
      actions: [
        { label: 'Retry now', onClick: retry },
        { label: 'Open', onClick: open },
        { label: 'Third', onClick: third },
      ],
    });

    const toast = screen.getByRole('alert');
    expect(within(toast).getByText('A sync keeps failing')).toBeInTheDocument();
    expect(within(toast).getByText('Trades have not been read for 2 days.')).toBeInTheDocument();
    expect(
      within(toast)
        .getAllByRole('button')
        .map((button) => button.textContent),
    ).toEqual(['Retry now', 'Open', '']);
    expect(within(toast).getByRole('button', { name: 'Close notification' })).toBeInTheDocument();
    expect(within(toast).queryByRole('button', { name: 'Third' })).not.toBeInTheDocument();

    await userEvent.click(within(toast).getByRole('button', { name: 'Open' }));

    expect(open).toHaveBeenCalledTimes(1);
    expect(retry).not.toHaveBeenCalled();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('announces errors and warnings as alerts and the rest as statuses, once each', () => {
    const { notify } = setup();
    notify('Saved', 'info');
    notify('Careful', 'warning');
    notify('Broken', 'error');

    expect(toasts().getAllByRole('status')).toHaveLength(1);
    expect(toasts().getAllByRole('alert')).toHaveLength(2);
    // The only live regions are the cards themselves: nothing wraps them in a second one.
    expect(screen.getByRole('region', { name: 'Notifications' }).closest('[aria-live]')).toBeNull();
    expect(screen.getByRole('region', { name: 'Notifications' }).querySelectorAll('[aria-live]')).toHaveLength(0);
  });

  it('has no region while there is nothing to show', () => {
    setup();

    expect(screen.queryByRole('region', { name: 'Notifications' })).not.toBeInTheDocument();
  });

  it('answers Ctrl+Z only for the single undo button, not for the other buttons', async () => {
    const undo = vi.fn();
    const open = vi.fn();
    const { notify } = setup();
    notify('Moved 2 instruments', 'success', { action: { label: 'Undo', onClick: undo } });
    notify('Reconnect', 'error', { persistent: true, actions: [{ label: 'Open', onClick: open }] });

    expect(screen.getAllByText('Ctrl/⌘+Z')).toHaveLength(1);
    await userEvent.keyboard('{Control>}z{/Control}');

    expect(undo).toHaveBeenCalledTimes(1);
    expect(open).not.toHaveBeenCalled();
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Undo' })).not.toBeInTheDocument();
  });
});

describe('ToastProvider: pausing', () => {
  it('pauses every toast while the pointer is on any of them, and goes on with what was left', async () => {
    const { notify } = setup();
    notify('Older');
    notify('Newer');
    act(() => {
      vi.advanceTimersByTime(3000);
    });

    await userEvent.hover(screen.getByText('Newer'));
    act(() => {
      vi.advanceTimersByTime(60_000);
    });
    expect(texts()).toHaveLength(2);

    await userEvent.unhover(screen.getByText('Newer'));
    act(() => {
      vi.advanceTimersByTime(1900);
    });
    expect(texts()).toHaveLength(2);
    wait(200);
    expect(texts()).toHaveLength(0);
  });

  it('does not pause for a finger: a touch has no leave to end the pause', () => {
    const { notify } = setup();
    notify('Saved');

    fireEvent.pointerEnter(screen.getByText('Saved'), { pointerType: 'touch' });
    act(() => {
      vi.advanceTimersByTime(11_000);
    });
    wait(0);

    expect(texts()).toHaveLength(0);
  });

  it('pauses while a button in the toast has keyboard focus', async () => {
    const { notify } = setup();
    notify('Reconnect', 'info', { actions: [{ label: 'Open', onClick: vi.fn() }] });

    await userEvent.tab();
    expect(screen.getByRole('textbox', { name: 'Notes' })).toHaveFocus();
    await userEvent.tab();
    expect(screen.getByRole('button', { name: 'Open' })).toHaveFocus();
    act(() => {
      vi.advanceTimersByTime(60_000);
    });
    expect(texts()).toHaveLength(1);

    await userEvent.tab({ shift: true });
    wait(10_000);
    expect(texts()).toHaveLength(0);
  });
});

describe('ToastProvider: motion', () => {
  it('shows a countdown bar and slides in', () => {
    const { notify } = setup();
    notify('Saved');

    const toast = screen.getByRole('status');
    expect(toast.querySelector('[data-countdown]')).toBeInTheDocument();
    expect(appliedCss(toast).base).toContain('animation');
  });

  it('shows no countdown bar and does not slide under reduced motion, but still stays its full time', () => {
    stubReducedMotion();
    const { notify } = setup();
    notify('Saved');

    const toast = screen.getByRole('status');
    expect(toast.querySelector('[data-countdown]')).toBeNull();
    expect(appliedCss(toast).base).not.toContain('animation');

    act(() => {
      vi.advanceTimersByTime(4900);
    });
    expect(screen.getByRole('status')).toBeInTheDocument();
    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});

describe('ToastProvider: placement', () => {
  it('fills the width above the bottom edge on phones and sits at the corner from the md breakpoint', () => {
    const { notify } = setup();
    notify('Saved');

    const stack = screen.getByRole('region', { name: 'Notifications' });
    const css = appliedCss(stack);
    expect(css.base).toContain('position: fixed');
    expect(css.base).toMatch(/left: 12px; right: 12px;/);
    expect(css.base).toContain('env(safe-area-inset-bottom');
    const wide = css.responsive.find((rule) => rule.query.includes('min-width'));
    expect(wide?.css).toMatch(/left: auto/);
    expect(wide?.css).toMatch(/right: 24px/);
    expect(wide?.css).toMatch(/width: 400px/);
  });

  it('lies above drawers and dialogs, like the snackbar did', () => {
    const { notify } = setup();
    notify('Saved');

    expect(appliedCss(screen.getByRole('region', { name: 'Notifications' })).base).toContain('z-index: 1400');
  });
});

describe('ToastProvider: colour mode', () => {
  afterEach(() => {
    localStorage.clear();
  });

  const rgb = (hex: string) => {
    const n = Number.parseInt(hex.slice(1), 16);
    return `rgb(${String((n >> 16) & 255)}, ${String((n >> 8) & 255)}, ${String(n & 255)})`;
  };

  it.each([
    ['light', palettes.light.goodSoft],
    ['dark', palettes.dark.goodSoft],
  ] as const)('paints the card with the %s tint of its severity', (mode, tint) => {
    localStorage.setItem('xd-color-mode', mode);
    const { notify } = setup();
    notify('Saved', 'success');

    expect(appliedCss(screen.getByRole('status')).base).toContain(rgb(tint));
  });
});
