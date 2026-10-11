import { render, screen } from '@testing-library/react';
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
});
