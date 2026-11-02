import { Box, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Grid, type GridColumn } from '@/shared/ui/grid';
import { DataTable, EvidenceBlock, formatCount, type Column } from '@/shared/analytics';
import { resultsCalendarQuery } from '@/shared/api/eventsRead';
import type { components } from '@/shared/api/schema';
import { formatIstDate, formatWeekdayDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';

type Meeting = components['schemas']['ResultsMeetingView'];

const columns: readonly Column<Meeting>[] = [
  { kind: 'text', id: 'symbol', header: 'Symbol', render: (m) => m.symbol },
  { kind: 'text', id: 'period', header: 'Period end', render: (m) => formatWeekdayDate(m.periodEnd) },
  { kind: 'text', id: 'meeting', header: 'Meeting date', render: (m) => formatWeekdayDate(m.meetingDate) },
  { kind: 'text', id: 'session', header: 'Event session', render: (m) => formatWeekdayDate(m.eventSession) },
  {
    kind: 'metric',
    id: 'days',
    header: 'Sessions away',
    metric: (m) => ({ kind: 'value', value: m.days }),
    format: formatCount,
  },
  { kind: 'text', id: 'intimated', header: 'Intimated', render: (m) => formatIstDate(m.intimatedAt) },
  {
    kind: 'metric',
    id: 'revision',
    header: 'Revision',
    metric: (m) => ({ kind: 'value', value: m.revision }),
    format: formatCount,
  },
];

/** Above this many rows the list is a grid that loads further rows as it is scrolled; the API sends at most 2000 meetings. */
const longList = 500;
const pageRows = 200;

const gridColumns: readonly GridColumn<Meeting>[] = [
  { id: 'symbol', header: 'Symbol', accessor: (m) => m.symbol },
  { id: 'period', header: 'Period end', accessor: (m) => formatWeekdayDate(m.periodEnd) },
  { id: 'meeting', header: 'Meeting date', accessor: (m) => formatWeekdayDate(m.meetingDate) },
  { id: 'session', header: 'Event session', accessor: (m) => formatWeekdayDate(m.eventSession) },
  { id: 'days', header: 'Sessions away', numeric: true, accessor: (m) => formatCount(m.days) },
  { id: 'intimated', header: 'Intimated', accessor: (m) => formatIstDate(m.intimatedAt) },
  { id: 'revision', header: 'Revision', numeric: true, accessor: (m) => formatCount(m.revision) },
];
const meetingId = (m: Meeting) => `${m.instrumentId}-${String(m.revision)}-${m.meetingDate}`;

/** The results meetings the exchange has published (as known now), a symbol filter, and the tracked instruments without an entry. */
export function ResultsCalendarSection() {
  const query = useQuery(resultsCalendarQuery);
  const [symbol, setSymbol] = useState('');
  const [shown, setShown] = useState(pageRows);
  const data = query.data;
  const evidence = {
    n: data?.meetings.length ?? null,
    range: data ? { from: data.from, to: data.to } : null,
    basis: 'unknown' as const,
    basisText: 'Meetings published by the exchange; no estimate',
    limits: data?.truncated ? ['The list is cut at 2000 meetings; narrow the range to see the rest.'] : [],
  };
  const needle = symbol.trim().toUpperCase();
  const rows = (data?.meetings ?? []).filter((m) => m.symbol.includes(needle));
  return (
    <Box component="section" aria-labelledby="results-calendar-title">
      <Typography id="results-calendar-title" variant="h3" component="h2" sx={{ mb: 1 }}>
        Results calendar
      </Typography>
      {query.isPending && <LoadingState label="Loading the results calendar" />}
      {query.isError && <ErrorState error={query.error} />}
      {data && (
        <>
          <TextField
            size="small"
            label="Filter by symbol"
            value={symbol}
            onChange={(event) => {
              setSymbol(event.target.value);
            }}
            sx={{ mb: 1 }}
          />
          {rows.length > longList ? (
            <>
              <Box sx={{ height: 480, display: 'flex', flexDirection: 'column', mb: 1 }}>
                <Grid
                  label="Results meetings"
                  columns={gridColumns}
                  rows={rows.slice(0, shown)}
                  getRowId={meetingId}
                  resetKey={needle}
                  infinite={{
                    total: rows.length,
                    hasMore: shown < rows.length,
                    loadingMore: false,
                    onLoadMore: () => {
                      setShown((n) => n + pageRows);
                    },
                  }}
                />
              </Box>
              <EvidenceBlock {...evidence} />
            </>
          ) : (
            <DataTable
              label="Results meetings"
              columns={columns}
              rows={rows}
              rowKey={meetingId}
              status={
                data.meetings.length === 0
                  ? { kind: 'empty', reason: 'no results meetings known for this range' }
                  : rows.length === 0
                    ? { kind: 'empty', reason: 'no meeting matches the filter' }
                    : undefined
              }
              evidence={evidence}
            />
          )}
          <Typography variant="body2" sx={{ mt: 1 }}>
            {formatCount(data.instrumentsWithoutEntry.count)} without a calendar entry
            {data.instrumentsWithoutEntry.symbols.length > 0
              ? `: ${data.instrumentsWithoutEntry.symbols.join(', ')}`
              : ''}
            {data.instrumentsWithoutEntry.count > data.instrumentsWithoutEntry.symbols.length
              ? ` (first ${String(data.instrumentsWithoutEntry.symbols.length)} shown)`
              : ''}
          </Typography>
        </>
      )}
    </Box>
  );
}
