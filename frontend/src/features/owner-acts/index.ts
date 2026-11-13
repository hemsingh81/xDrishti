// Public API of the owner-acts feature. Other code imports only from here. The pages load on demand, so a feature that
// only asks `useCapability` (the Learning link) does not carry them.
export { useCapability } from './api/capabilitiesRead';
export const loadOwnerActsPage = async () => (await import('./components/OwnerActsPage')).OwnerActsPage;
export const loadExperimentPage = async () => (await import('./components/ExperimentPage')).ExperimentPage;
// CF-10 10d-2b: the owner acts of the Learning screen's Models tab (the screen itself holds no write).
export const loadMlSwitchDialog = async () => (await import('./components/MlSwitchDialog')).MlSwitchDialog;
export const loadApplyRollbackDialog = async () =>
  (await import('./components/ApplyRollbackDialog')).ApplyRollbackDialog;
export const loadHoldOutScoreDialog = async () => (await import('./components/HoldOutScoreDialog')).HoldOutScoreDialog;
export const loadMovePage = async () => (await import('./components/MovePage')).MovePage;
