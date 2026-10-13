import type { ServiceView } from '@/shared/api';

/** The services a service can run after: any other that is not set elsewhere and is not always on (it never finishes). */
export const followable = (service: ServiceView, services: readonly ServiceView[]): ServiceView[] =>
  services.filter((s) => s.id !== service.id && !s.readOnly && s.schedule.type !== 'continuous');
