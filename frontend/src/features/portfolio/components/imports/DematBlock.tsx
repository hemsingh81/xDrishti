import { Alert, Stack, Typography } from '@mui/material';
import type { DematDifference, DematHeld, DematPreview, DematSynced } from '@/shared/api';
import { formatDate, formatNumber } from '@/shared/lib/format';
import { Figure } from '../dividends/Figure';
import { DifferenceList } from './DifferenceList';
import type { PagedColumn } from './PagedTable';

const security = (name: string, isin: string | null) => (
  <>
    {name}
    {isin && (
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {isin}
      </Typography>
    )}
  </>
);

const differenceColumns: readonly PagedColumn<DematDifference>[] = [
  { header: 'Security', render: (row) => security(row.name, row.isin) },
  { header: 'In the statement', numeric: true, render: (row) => formatNumber(row.statement) },
  { header: 'Synced from Dhan', numeric: true, render: (row) => formatNumber(row.synced) },
  { header: 'Difference', numeric: true, render: (row) => formatNumber(row.statement - row.synced) },
];

const heldColumns: readonly PagedColumn<DematHeld>[] = [
  { header: 'Security', render: (row) => security(row.name, row.isin) },
  { header: 'In the statement', numeric: true, render: (row) => formatNumber(row.quantity) },
];

const syncedColumns: readonly PagedColumn<DematSynced>[] = [
  { header: 'Symbol', render: (row) => row.symbol },
  { header: 'Security', render: (row) => security(row.name, row.isin) },
  { header: 'Synced from Dhan', numeric: true, render: (row) => formatNumber(row.quantity) },
];

/**
 * A demat holding statement: a check of the depository's quantities against the holdings synced from Dhan, and a source of
 * names. Informational: nothing is changed - the broker's holdings stay authoritative.
 */
export function DematBlock({ demat }: { demat: DematPreview }) {
  return (
    <Stack spacing={1.5}>
      <Alert severity="info">
        Informational: nothing is changed. The statement never creates or changes a holding; it is compared with the
        holdings synced from Dhan and teaches the app the names and ISINs of your securities.
      </Alert>
      <Stack component="dl" direction="row" sx={{ m: 0, columnGap: 3, rowGap: 1.5, flexWrap: 'wrap' }}>
        <Figure label="As of" value={demat.asOf ? formatDate(demat.asOf) : '—'} />
        <Figure label="Securities in the statement" value={formatNumber(demat.securities)} />
        <Figure label="Matching your holdings" value={formatNumber(demat.matching)} />
        <Figure label="Quantity differences" value={formatNumber(demat.quantityDifferencesTotal)} />
        <Figure label="Held, not synced" value={formatNumber(demat.heldNotSyncedTotal)} />
        <Figure label="Synced, not in the statement" value={formatNumber(demat.syncedNotInStatementTotal)} />
      </Stack>
      <DifferenceList
        title="Quantity differences"
        empty="Every security in both agrees on the quantity."
        rows={demat.quantityDifferences}
        total={demat.quantityDifferencesTotal}
        columns={differenceColumns}
        rowKey={(row) => row.isin}
      />
      <DifferenceList
        title="Held at Dhan, not synced"
        empty="Every security of the statement is among your synced holdings."
        rows={demat.heldNotSynced}
        total={demat.heldNotSyncedTotal}
        columns={heldColumns}
        rowKey={(row) => row.isin}
      />
      <DifferenceList
        title="Synced, not in the statement"
        empty="Every synced holding is in the statement."
        rows={demat.syncedNotInStatement}
        total={demat.syncedNotInStatementTotal}
        columns={syncedColumns}
        rowKey={(row) => `${row.symbol}-${row.isin ?? ''}`}
      />
      <Typography variant="body2" color="text.secondary">
        {`Importing remembers ${formatNumber(demat.names.new)} new names (${formatNumber(demat.names.known)} are known already), so transaction reports find their ISIN.`}
      </Typography>
    </Stack>
  );
}
