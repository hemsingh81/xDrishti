import type { ServiceView } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';

/** What the Next run column says: the time in India time, a reason it has none ("After …", "Always on"), else null. */
export function nextRunText(service: ServiceView, names: ReadonlyMap<string, string>): string | null {
  if (service.nextRun && service.enabled) return formatIstDayAndTime(service.nextRun);
  if (!service.enabled) return null;
  switch (service.schedule.type) {
    case 'after':
      return `After ${names.get(service.schedule.after ?? '') ?? 'another service'}`;
    case 'continuous':
      return 'Always on';
    default:
      return null;
  }
}
