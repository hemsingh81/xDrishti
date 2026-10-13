import { useQuery } from '@tanstack/react-query';
import type { SchedulePreview, ServiceScheduleInput } from '@/shared/api';
import { useDebouncedValue } from '@/shared/lib/useDebouncedValue';
import { previewQuery } from '../api/servicesQueries';

/** How long the draft must stay as it is before the checks are asked for. */
export const previewDelayMs = 400;

export interface SchedulePreviewState {
  /** The answer for the draft the owner sees; undefined until there is one (or while the draft is not complete). */
  preview: SchedulePreview | undefined;
  /** The draft changed and its answer is not here yet: the preview shown is of an earlier draft. */
  checking: boolean;
  /** The checks could not be asked for (the server did not answer). */
  failed: boolean;
}

/**
 * The checks and the next three runs for a draft, asked for `previewDelayMs` after the owner stops typing. `input` is
 * null while the draft is not complete (a field is empty): nothing is asked, the dialog says what is missing.
 */
export function useSchedulePreview(serviceId: string, input: ServiceScheduleInput | null): SchedulePreviewState {
  const settled = useDebouncedValue(input, previewDelayMs);
  const query = useQuery(previewQuery(serviceId, settled));
  const waiting = JSON.stringify(settled) !== JSON.stringify(input);
  return {
    preview: settled === null ? undefined : query.data,
    checking: input !== null && (waiting || query.isFetching),
    failed: settled !== null && query.isError,
  };
}
