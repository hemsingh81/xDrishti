import HistoryIcon from '@mui/icons-material/History';
import TuneIcon from '@mui/icons-material/Tune';
import { Alert, Box, Button, Drawer, Stack, Typography, useMediaQuery } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useInfiniteQuery, useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import type { TableStatus } from '@/shared/analytics';
import { useUrlState } from '@/shared/analytics';
import { PageHeader } from '@/shared/ui';
import { entriesQuery, kindsQuery, summaryQuery, type AuditEntry } from '../api/auditQueries';
import { hasFilters, presets, toQuery, type AuditFilter } from '../lib/auditParams';
import { AuditDrawer } from './AuditDrawer';
import { AuditFilterControls } from './AuditFilterControls';
import { AuditRows } from './AuditRows';

const dayMs = 86_400_000;

/** The URL holds the view: a link reproduces it and Back works. */
function useFilter(): [AuditFilter, ReturnType<typeof useUrlState>['update']] {
  const url = useUrlState();
  const filter: AuditFilter = {
    preset: url.choice('range', presets, '30d'),
    from: url.text('from'),
    to: url.text('to'),
    source: url.text('source'),
    category: url.text('category'),
    kind: url.text('kind'),
    outcome: url.text('outcome'),
    actor: url.text('actor'),
    account: url.text('account'),
    q: url.text('q'),
  };
  return [filter, url.update];
}

/** Failed or refused rows of the last 24 hours among the rows loaded: the thing to look at first. */
function recentTrouble(rows: readonly AuditEntry[]): number {
  const since = Date.now() - dayMs;
  return rows.filter((r) => r.outcome !== 'ok' && new Date(r.at).getTime() >= since).length;
}

/**
 * The Audit log: what happened, when, and who or what did it, across sign-ins, security, configuration, service actions, plan
 * decisions, autonomy and learning changes. Read only: no export, no copy-all. It shows only what is recorded, and says when a
 * record could not be read.
 */
export function AuditPage() {
  const [filter, update] = useFilter();
  const query = toQuery(filter);
  const phone = useMediaQuery(useTheme().breakpoints.down('md'));
  const [filtersOpen, setFiltersOpen] = useState(false);
  const entry = useUrlState().text('entry');
  const entries = useInfiniteQuery(entriesQuery(query));
  const summary = useQuery(summaryQuery(query.from, query.to));
  const kinds = useQuery(kindsQuery);

  const pages = entries.data?.pages ?? [];
  const rows = pages.flatMap((p) => p.items);
  const unavailable = new Set(
    pages.flatMap((p) => p.sources.filter((s) => s.state === 'unavailable').map((s) => s.source)),
  );
  const limits = [...new Set(pages.flatMap((p) => p.limits))];
  const status: TableStatus | undefined = entries.isPending
    ? { kind: 'loading' }
    : entries.isError
      ? { kind: 'error' }
      : rows.length === 0
        ? { kind: 'empty' }
        : undefined;
  const trouble = recentTrouble(rows);
  const controls = <AuditFilterControls filter={filter} kinds={kinds.data} seen={rows} onChange={update} />;

  return (
    <Box>
      <PageHeader
        title="Audit log"
        description="What happened, when, and who or what did it. Read only: nothing here changes anything."
        icon={HistoryIcon}
        hue="audit"
        actions={
          phone && (
            <Button
              startIcon={<TuneIcon />}
              variant="outlined"
              onClick={() => {
                setFiltersOpen(true);
              }}
            >
              Filters
            </Button>
          )
        }
      />
      {phone ? (
        <Drawer
          anchor="bottom"
          open={filtersOpen}
          onClose={() => {
            setFiltersOpen(false);
          }}
        >
          <Box sx={{ p: 2 }}>
            <Typography variant="h2" sx={{ fontSize: '1.1rem', mb: 1.5 }}>
              Filters
            </Typography>
            {controls}
          </Box>
        </Drawer>
      ) : (
        <Box sx={{ mb: 2 }}>{controls}</Box>
      )}

      {unavailable.size > 0 && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {unavailable.size} of 6 records could not be read ({[...unavailable].join(', ')}). This list is not complete.
        </Alert>
      )}
      {trouble > 0 && (
        <Alert severity="warning" sx={{ mb: 1.5 }}>
          {trouble} failed or refused {trouble === 1 ? 'entry' : 'entries'} in the last 24 hours among the rows shown.
        </Alert>
      )}
      {entries.isError && (
        <Alert
          severity="error"
          sx={{ mb: 1.5 }}
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void entries.refetch();
              }}
            >
              Retry
            </Button>
          }
        >
          The audit could not be read.
        </Alert>
      )}

      {status?.kind === 'empty' && (
        <Typography sx={{ py: 3 }} color="text.secondary">
          No entries in this range{hasFilters(filter) ? ' with these filters' : ''}.
        </Typography>
      )}
      {status?.kind !== 'empty' && (
        <AuditRows
          rows={rows}
          phone={phone}
          status={status}
          onOpen={(id) => {
            update({ entry: id });
          }}
        />
      )}

      {rows.length > 0 && (
        <Stack direction="row" sx={{ mt: 1.5, gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="body2" color="text.secondary">
            Showing {rows.length}
            {summary.data ? ` of ${summary.data.total} in this range` : ''}
            {hasFilters(filter) ? ' (filters narrow the rows, not the total)' : ''}.
          </Typography>
          {entries.hasNextPage && (
            <Button
              variant="outlined"
              disabled={entries.isFetchingNextPage}
              onClick={() => {
                void entries.fetchNextPage();
              }}
            >
              Load more
            </Button>
          )}
        </Stack>
      )}
      {limits.length > 0 && (
        <Stack component="ul" sx={{ mt: 2, pl: 2.5, gap: 0.25 }}>
          {limits.map((l) => (
            <Typography key={l} component="li" variant="caption" color="text.secondary">
              {l}
            </Typography>
          ))}
        </Stack>
      )}
      <AuditDrawer
        id={entry}
        onClose={() => {
          update({ entry: null });
        }}
      />
    </Box>
  );
}
