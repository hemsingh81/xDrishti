import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect, useEffectEvent, useRef, useState } from 'react';
import type { RefreshRun } from '@/shared/api';
import { useToast } from '@/shared/ui/toastContext';
import { useStartRefresh } from '../api/refreshMutation';
import { instrumentsKeys, refreshStatusQuery } from '../api/instrumentsQueries';
import { isRefreshRunning } from '../lib/errors';
import { outcomeText } from '../lib/labels';
import { refreshPollMs, startGraceMs } from '../lib/polling';

interface Started {
  at: number;
  lastId: string | null;
}

/**
 * The refresh status plus the "start" action. Polls only while a refresh is running or was just started; when a new
 * finished run appears it reloads the search results and counts and shows a toast with the outcome.
 */
export function useRefreshWatch() {
  const queryClient = useQueryClient();
  const toast = useToast();
  const start = useStartRefresh();
  const [started, setStarted] = useState<Started | null>(null);

  const status = useQuery({
    ...refreshStatusQuery,
    refetchInterval: (query) => {
      const data = query.state.data;
      if (data?.current) return refreshPollMs;
      const waiting = started !== null && (data?.last?.id ?? null) === started.lastId;
      return waiting && Date.now() - started.at < startGraceMs ? refreshPollMs : false;
    },
  });

  const announce = useEffectEvent((run: RefreshRun) => {
    void queryClient.invalidateQueries({ queryKey: instrumentsKeys.search });
    void queryClient.invalidateQueries({ queryKey: instrumentsKeys.facets });
    void queryClient.invalidateQueries({ queryKey: [...instrumentsKeys.all, 'detail'] });
    toast.notify(outcomeText(run), run.status === 'failed' ? 'error' : 'success');
  });

  // A run that finished since the last look: reload what depends on the list and tell the owner.
  const seenLastId = useRef<string | null | undefined>(undefined);
  const data = status.data;
  useEffect(() => {
    if (!data) return;
    const id = data.last?.id ?? null;
    const previous = seenLastId.current;
    seenLastId.current = id;
    if (previous === undefined || previous === id || !data.last) return;
    announce(data.last);
  }, [data]);

  function startRefresh(force: boolean) {
    const watch = () => {
      setStarted({
        at: Date.now(),
        lastId: queryClient.getQueryData(refreshStatusQuery.queryKey)?.last?.id ?? null,
      });
    };
    start.mutate(
      { force },
      {
        onSuccess: watch,
        onError: (error) => {
          if (isRefreshRunning(error)) watch();
        },
      },
    );
  }

  return {
    status,
    startRefresh,
    /** True from the click until the status shows the run (or the run is over). */
    busy: Boolean(data?.current) || start.isPending || (start.isSuccess && status.isFetching),
    startError: start.error && !isRefreshRunning(start.error) ? start.error : null,
  };
}
