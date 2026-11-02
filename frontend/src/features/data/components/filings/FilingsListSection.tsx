import { Box, Button, MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { DataTable, NonValue, type Column } from '@/shared/analytics';
import type { components } from '@/shared/api/schema';
import { formatIstDateTime, formatWeekdayDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { filingKinds, filingsQuery, type FilingKindFilter } from '../../api/filingsQueries';
import { FilingFactsDialog } from './FilingFactsDialog';
import { reasonText } from './words';

type Filing = components['schemas']['FilingView'];

/** The stored filings, newest broadcast first: metadata only. For a results filing the parsed facts open in a dialog. */
export function FilingsListSection() {
  const [kind, setKind] = useState<FilingKindFilter>('all');
  const [open, setOpen] = useState<Filing | null>(null);
  const query = useQuery(filingsQuery(kind));
  const columns: readonly Column<Filing>[] = [
    { kind: 'text', id: 'symbol', header: 'Symbol', render: (f) => f.symbol },
    { kind: 'text', id: 'kind', header: 'Kind', render: (f) => f.kind },
    {
      kind: 'text',
      id: 'period',
      header: 'Period end',
      render: (f) => (f.periodEnd ? formatWeekdayDate(f.periodEnd) : 'not stated'),
    },
    {
      kind: 'text',
      id: 'broadcast',
      header: 'Broadcast',
      render: (f) =>
        f.broadcastTs ? (
          formatIstDateTime(f.broadcastTs)
        ) : (
          <NonValue state={{ kind: 'unavailable', reason: reasonText(f.unavailableReason) }} />
        ),
    },
    { kind: 'text', id: 'ingested', header: 'Stored', render: (f) => formatIstDateTime(f.ingestedAt) },
    {
      kind: 'text',
      id: 'revision',
      header: 'Revision',
      render: (f) => (f.supersedes ? 'supersedes an earlier filing' : 'first'),
    },
    {
      kind: 'text',
      id: 'facts',
      header: 'Facts',
      render: (f) =>
        f.kind === 'results' ? (
          <Button
            size="small"
            aria-label={`Show facts of the ${f.symbol} results filing`}
            onClick={() => {
              setOpen(f);
            }}
          >
            Show facts
          </Button>
        ) : (
          'not parsed for this kind'
        ),
    },
  ];
  return (
    <Box component="section" aria-labelledby="filings-list-title">
      <Typography id="filings-list-title" variant="h3" component="h2" sx={{ mb: 1 }}>
        Stored filings
      </Typography>
      <TextField
        select
        size="small"
        label="Kind"
        value={kind}
        onChange={(event) => {
          setKind(event.target.value as FilingKindFilter);
        }}
        sx={{ mb: 1, minWidth: 200 }}
      >
        <MenuItem value="all">All kinds</MenuItem>
        {filingKinds.map((k) => (
          <MenuItem key={k} value={k}>
            {k}
          </MenuItem>
        ))}
      </TextField>
      {query.isPending && <LoadingState label="Loading filings" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && (
        <DataTable
          label="Stored filings"
          columns={columns}
          rows={query.data.items}
          rowKey={(f) => f.id}
          status={query.data.items.length === 0 ? { kind: 'empty', reason: 'no filings stored yet' } : undefined}
          evidence={{
            n: query.data.items.length,
            range: null,
            basis: 'unknown',
            basisText: 'Envelope metadata only: no file content, no path',
            limits: query.data.truncated ? ['Only the newest 200 are listed.'] : [],
          }}
        />
      )}
      <FilingFactsDialog
        id={open?.id ?? null}
        symbol={open?.symbol ?? ''}
        onClose={() => {
          setOpen(null);
        }}
      />
    </Box>
  );
}
