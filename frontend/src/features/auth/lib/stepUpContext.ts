import { createContext } from 'react';

export interface StepUpContextValue {
  /** Opens the password dialog (one at a time); resolves true once the password was accepted, false when cancelled. */
  requestStepUp: () => Promise<boolean>;
}

export const StepUpContext = createContext<StepUpContextValue | null>(null);
