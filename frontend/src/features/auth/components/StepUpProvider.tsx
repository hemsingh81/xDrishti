import { lazy, Suspense, useMemo, useRef, useState, type ReactNode } from 'react';
import { StepUpContext, type StepUpContextValue } from '../lib/stepUpContext';

// The dialog is only needed after a 403, so it stays out of the initial bundle.
const StepUpDialog = lazy(async () => ({ default: (await import('./StepUpDialog')).StepUpDialog }));

interface PendingRequest {
  promise: Promise<boolean>;
  settle: (accepted: boolean) => void;
}

/** Provides `useStepUp` to the tree and renders the password dialog while a step-up is requested. */
export function StepUpProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const pending = useRef<PendingRequest | null>(null);

  const value = useMemo<StepUpContextValue>(
    () => ({
      requestStepUp: () => {
        // Concurrent requests (e.g. several saves) share one dialog and one answer.
        if (pending.current) {
          return pending.current.promise;
        }
        let settle: (accepted: boolean) => void = () => undefined;
        const promise = new Promise<boolean>((resolve) => {
          settle = resolve;
        });
        pending.current = { promise, settle };
        setOpen(true);
        return promise;
      },
    }),
    [],
  );

  function finish(accepted: boolean) {
    pending.current?.settle(accepted);
    pending.current = null;
    setOpen(false);
  }

  return (
    <StepUpContext value={value}>
      {children}
      {open && (
        <Suspense fallback={null}>
          <StepUpDialog onDone={finish} />
        </Suspense>
      )}
    </StepUpContext>
  );
}
