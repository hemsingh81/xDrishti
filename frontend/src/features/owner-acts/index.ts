// Public API of the owner-acts feature. Other code imports only from here. The pages load on demand, so a feature that
// only asks `useCapability` (the Learning link) does not carry them.
export { useCapability } from './api/capabilitiesApi';
export const loadOwnerActsPage = async () => (await import('./components/OwnerActsPage')).OwnerActsPage;
export const loadExperimentPage = async () => (await import('./components/ExperimentPage')).ExperimentPage;
