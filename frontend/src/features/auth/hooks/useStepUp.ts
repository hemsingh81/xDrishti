import { useCallback, useContext } from 'react';
import { StepUpContext } from '../lib/stepUpContext';
import { isStepUpRequired, StepUpCancelledError } from '../lib/stepUpErrors';

export type WithStepUp = <TArgs extends unknown[], TResult>(
  action: (...args: TArgs) => Promise<TResult>,
) => (...args: TArgs) => Promise<TResult>;

/**
 * Password re-entry for sensitive changes. `withStepUp(action)` returns the same function, except that when the API
 * answers 403 `auth.step_up_required` it asks the owner for the password (dialog), and — once accepted — runs the
 * action once more. Cancelling rejects with a StepUpCancelledError (see `isStepUpCancelled`).
 */
export function useStepUp(): { withStepUp: WithStepUp } {
  const context = useContext(StepUpContext);
  if (!context) {
    throw new Error('useStepUp must be used inside <StepUpProvider>.');
  }
  const { requestStepUp } = context;

  const withStepUp = useCallback<WithStepUp>(
    (action) =>
      async (...args) => {
        try {
          return await action(...args);
        } catch (error) {
          if (!isStepUpRequired(error)) {
            throw error;
          }
          if (!(await requestStepUp())) {
            throw new StepUpCancelledError();
          }
          return action(...args); // the retry happens once; a second 403 is a real error
        }
      },
    [requestStepUp],
  );

  return { withStepUp };
}
