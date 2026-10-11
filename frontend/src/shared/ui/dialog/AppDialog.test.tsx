import { Button, TextField } from '@mui/material';
import { act, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { useRef, type SyntheticEvent } from 'react';
import { AppThemeProvider } from '@/theme';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader, type AppDialogProps } from '.';

type ShellProps = Partial<Omit<AppDialogProps, 'children' | 'initialFocusRef'>> & { focusField?: boolean };

/** A typical dialog built from the shell: title + subtitle, one field, Cancel and a submit button. */
function Shell({ focusField = false, onClose = vi.fn(), ...props }: ShellProps) {
  const fieldRef = useRef<HTMLInputElement>(null);
  return (
    <AppThemeProvider>
      <AppDialog open onClose={onClose} initialFocusRef={focusField ? fieldRef : undefined} {...props}>
        <AppDialogHeader subtitle="Shown under the title">Rename bucket</AppDialogHeader>
        <AppDialogBody>
          <TextField label="Name" inputRef={fieldRef} />
        </AppDialogBody>
        <AppDialogFooter>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained">
            Save
          </Button>
        </AppDialogFooter>
      </AppDialog>
    </AppThemeProvider>
  );
}

const submitSpy = () =>
  vi.fn((event: SyntheticEvent) => {
    event.preventDefault();
  });

afterEach(() => {
  vi.useRealTimers();
});

describe('AppDialog', () => {
  it('is named by its title and renders the header, body and footer content', async () => {
    render(<Shell />);

    const dialog = await screen.findByRole('dialog', { name: 'Rename bucket' });
    expect(within(dialog).getByRole('heading', { name: 'Rename bucket' })).toBeInTheDocument();
    expect(within(dialog).getByText('Shown under the title')).toBeInTheDocument();
    expect(within(dialog).getByLabelText('Name')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cancel' })).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Save' })).toBeInTheDocument();
  });

  it('closes with the close button and with Escape', async () => {
    const onClose = vi.fn();
    render(<Shell onClose={onClose} />);
    const dialog = await screen.findByRole('dialog', { name: 'Rename bucket' });

    await userEvent.click(within(dialog).getByRole('button', { name: 'Close' }));
    expect(onClose).toHaveBeenCalledTimes(1);

    await userEvent.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(2);
  });

  it('closes when the backdrop is clicked, except while busy', async () => {
    const onClose = vi.fn();
    const { rerender } = render(<Shell onClose={onClose} />);
    await screen.findByRole('dialog', { name: 'Rename bucket' });
    const backdrop = () => {
      const element = document.querySelector('.MuiBackdrop-root');
      if (!(element instanceof HTMLElement)) throw new Error('no backdrop');
      return element;
    };

    await userEvent.click(backdrop());
    expect(onClose).toHaveBeenCalledTimes(1);

    rerender(<Shell onClose={onClose} busy />);
    await userEvent.click(backdrop());
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('cannot be closed while busy', async () => {
    const onClose = vi.fn();
    render(<Shell onClose={onClose} busy />);
    const dialog = await screen.findByRole('dialog', { name: 'Rename bucket' });

    expect(within(dialog).getByRole('button', { name: 'Close' })).toBeDisabled();
    await userEvent.type(within(dialog).getByLabelText('Name'), '{Escape}');
    expect(onClose).not.toHaveBeenCalled();
  });

  it('does not pull focus back when another dialog opened on top while it was still opening', async () => {
    function Stacked({ top }: { top: boolean }) {
      const nameRef = useRef<HTMLInputElement>(null);
      return (
        <AppThemeProvider>
          <AppDialog open onClose={vi.fn()} initialFocusRef={nameRef}>
            <AppDialogHeader>Edit profile</AppDialogHeader>
            <AppDialogBody>
              <TextField label="Name" inputRef={nameRef} />
            </AppDialogBody>
          </AppDialog>
          {top && (
            <AppDialog open onClose={vi.fn()}>
              <AppDialogHeader>Confirm your password</AppDialogHeader>
              <AppDialogBody>
                <TextField
                  label="Password"
                  inputRef={(element: HTMLInputElement | null) => {
                    element?.focus(); // focused as soon as it exists, like the password prompt's own field
                  }}
                />
              </AppDialogBody>
            </AppDialog>
          )}
        </AppThemeProvider>
      );
    }
    vi.useFakeTimers({ shouldAdvanceTime: true });
    const { rerender } = render(<Stacked top={false} />);
    await screen.findByRole('dialog', { name: 'Edit profile' });
    rerender(<Stacked top />);
    const password = await screen.findByLabelText('Password');
    await waitFor(() => {
      expect(password).toHaveFocus();
    });

    // Let the first dialog's opening transition (MUI's enter duration is 225 ms) finish, without a real wait.
    await act(async () => {
      await vi.advanceTimersByTimeAsync(500);
    });

    expect(password).toHaveFocus();
  });

  it('submits on Enter in a field and with the submit button when onSubmit is given', async () => {
    const onSubmit = submitSpy();
    render(<Shell onSubmit={onSubmit} />);
    const dialog = await screen.findByRole('dialog', { name: 'Rename bucket' });

    await userEvent.type(within(dialog).getByLabelText('Name'), 'Retirement{Enter}');
    expect(onSubmit).toHaveBeenCalledTimes(1);

    await userEvent.click(within(dialog).getByRole('button', { name: 'Save' }));
    expect(onSubmit).toHaveBeenCalledTimes(2);
  });

  it('renders no form without onSubmit', async () => {
    render(<Shell />);
    const dialog = await screen.findByRole('dialog', { name: 'Rename bucket' });

    // The acceptance criterion is about the DOM itself: a confirm-style dialog must not be a form.
    expect(dialog.querySelector('form')).toBeNull();
  });

  it('moves focus to the initial focus target once open', async () => {
    render(<Shell focusField />);
    const dialog = await screen.findByRole('dialog', { name: 'Rename bucket' });

    await waitFor(() => {
      expect(within(dialog).getByLabelText('Name')).toHaveFocus();
    });
  });
});
