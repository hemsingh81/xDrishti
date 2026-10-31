import { Alert, Chip, List, ListItem, ListItemText, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useUrlState } from '@/shared/analytics';
import { formatIstDateTime } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { activityQuery } from '../api/viewsApi';
import { causeWord } from '../lib/stateWords';
import { DateFilter, FilterBar, FilterSelect } from './Filters';
import { Pager } from './Pager';
import { RetryError } from './RetryError';

const kinds = [
  'auto_entry',
  'tap_entry',
  'auto_refused',
  'tap_refused',
  'auto_stop_tighten',
  'auto_exit',
  'auto_cancel',
  'loss_cap_halt',
  'loss_cap_notice',
  'alarm',
  'halt',
  'drill',
  'mode_change',
  'settings_change',
] as const;
const actors = ['auto', 'tap', 'system', 'owner'] as const;
const pageSize = 50;

/** The action log with alarms, halts, drills and changes, newest first. Read only. */
export function ActivityTab() {
  const url = useUrlState();
  const date = url.text('date') ?? '';
  const kind = url.text('kind') ?? '';
  const by = url.text('by') ?? '';
  const page = Number(url.text('page') ?? '1') || 1;
  const { data, error, isPending, refetch } = useQuery(
    activityQuery({ date, kind, by, page: page > 1 ? page : undefined }),
  );
  const set = (patch: Record<string, string>) => {
    url.update({ page: null, ...Object.fromEntries(Object.entries(patch).map(([k, v]) => [k, v === '' ? null : v])) });
  };
  return (
    <>
      <Typography variant="body2" sx={{ color: 'text.secondary', mb: 2 }}>
        This log is insert-only: nothing here can be edited or removed.
      </Typography>
      <FilterBar>
        <DateFilter
          value={date}
          onChange={(v) => {
            set({ date: v });
          }}
        />
        <FilterSelect
          label="Kind"
          value={kind}
          options={kinds.map((k) => ({ value: k, label: k.replace(/_/g, ' ') }))}
          onChange={(v) => {
            set({ kind: v });
          }}
        />
        <FilterSelect
          label="By"
          value={by}
          options={actors.map((a) => ({ value: a, label: causeWord(a) }))}
          onChange={(v) => {
            set({ by: v });
          }}
        />
      </FilterBar>
      {isPending && <LoadingState label="Loading activity" />}
      {error && <RetryError error={error} onRetry={() => void refetch()} />}
      {data?.length === 0 && (
        <Alert severity="info" role="status">
          Nothing has happened for this view.
        </Alert>
      )}
      {data && data.length > 0 && (
        <List aria-label="Activity" disablePadding>
          {data.map((row) => (
            <ListItem key={row.id} divider sx={{ alignItems: 'flex-start', gap: 1, flexWrap: 'wrap' }}>
              <ListItemText
                primary={row.text}
                secondary={`${formatIstDateTime(row.at)} IST${row.codes.length > 0 ? `, codes ${row.codes.join(', ')}` : ''}`}
              />
              <Chip size="small" label={row.kind.replace(/_/g, ' ')} />
              <Chip size="small" variant="outlined" label={causeWord(row.by)} />
            </ListItem>
          ))}
        </List>
      )}
      <Pager
        page={page}
        full={(data?.length ?? 0) >= pageSize}
        onPage={(p) => {
          url.update({ page: String(p) });
        }}
      />
    </>
  );
}
