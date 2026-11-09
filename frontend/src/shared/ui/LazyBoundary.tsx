import { Alert, Button } from '@mui/material';
import { Component, Suspense, type ReactNode } from 'react';

interface Props {
  /** Shown while the lazy code loads. */
  pending: ReactNode;
  /** Shown when the code failed to load; defaults to a message with a Reload button. */
  failed?: ReactNode;
  children: ReactNode;
}

/** The default failure: a lazy chunk is cached as rejected, so the only retry is a page reload. */
export function ReloadAlert({ label = 'This part' }: { label?: string }) {
  return (
    <Alert
      severity="error"
      sx={{ my: 1 }}
      action={
        <Button
          color="inherit"
          size="small"
          onClick={() => {
            window.location.reload();
          }}
        >
          Reload
        </Button>
      }
    >
      {label} could not load.
    </Alert>
  );
}

/**
 * Suspense plus an error boundary for lazy code, so a failed chunk download stays local instead of reaching the route
 * error page.
 */
export class LazyBoundary extends Component<Props, { failed: boolean }> {
  override state = { failed: false };

  static getDerivedStateFromError() {
    return { failed: true };
  }

  override render() {
    if (this.state.failed) {
      return this.props.failed ?? <ReloadAlert />;
    }
    return <Suspense fallback={this.props.pending}>{this.props.children}</Suspense>;
  }
}
