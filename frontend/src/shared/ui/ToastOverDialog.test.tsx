import { act, render, screen } from '@testing-library/react';
import { useEffect } from 'react';
import { AppThemeProvider } from '@/theme';
import { findToast } from '@/test/toast';
import { AppDialog, AppDialogBody, AppDialogHeader } from './dialog';
import { ToastProvider } from './ToastProvider';
import { useToast, type ToastApi } from './toastContext';

/** A dialog that is open from the start, and the provider's API for raising toasts while it is. */
function setup() {
  const handle: { api: ToastApi | null } = { api: null };
  function Page() {
    const toast = useToast();
    useEffect(() => {
      handle.api = toast;
    }, [toast]);
    return (
      <AppDialog open onClose={() => undefined}>
        <AppDialogHeader>Edit</AppDialogHeader>
        <AppDialogBody>Details</AppDialogBody>
      </AppDialog>
    );
  }
  render(
    <AppThemeProvider>
      <ToastProvider>
        <Page />
      </ToastProvider>
    </AppThemeProvider>,
  );
  return (...args: Parameters<ToastApi['notify']>) => {
    act(() => {
      handle.api?.notify(...args);
    });
  };
}

describe('ToastProvider above a dialog', () => {
  it('announces an error raised while a dialog is open (the dialog hides its siblings, not the toasts)', async () => {
    const notify = setup();
    await screen.findByRole('dialog', { name: 'Edit' });

    notify('Could not save', 'error');

    expect(await screen.findByRole('alert')).toHaveTextContent('Could not save');
  });

  it('keeps announcing when a toast was already on the page as the dialog opened', async () => {
    const notify = setup();
    await screen.findByRole('dialog', { name: 'Edit' });
    notify('Saved earlier', 'success', { persistent: true });
    notify('Could not save', 'error');

    expect(await findToast()).toHaveTextContent('Could not save');
    expect(screen.getByRole('alert')).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Saved earlier');
  });
});
