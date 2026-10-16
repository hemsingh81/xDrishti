import { Box, Typography } from '@mui/material';
import type { BillCoverage, BillDifference } from '@/shared/api';
import { formatDate, formatNumber } from '@/shared/lib/format';
import { sideLabel } from '../../lib/reportImport';
import { countOf } from '../../lib/tradeText';
import { PagedTable, type PagedColumn } from './PagedTable';

const differenceColumns: readonly PagedColumn<BillDifference>[] = [
  { header: 'Date', render: (row) => <time dateTime={row.date}>{formatDate(row.date)}</time> },
  { header: 'Security', render: (row) => `${row.symbol ?? row.isin}${row.symbol ? ` (${row.isin})` : ''}` },
  { header: 'Side', render: (row) => sideLabel(row.side) },
  { header: 'In the report', numeric: true, render: (row) => formatNumber(row.billQty) },
  { header: 'At Dhan', numeric: true, render: (row) => formatNumber(row.dhanQty) },
];

/**
 * What Dhan's own trade history already covers (ADR 0029): trades of the report for a day, security and side that Dhan has are
 * left out, and the days whose quantity differs are listed - shown, not hidden; Dhan's figure is the one that counts.
 */
export function BillCoverageView({ coverage }: { coverage: BillCoverage }) {
  return (
    <Box>
      <Typography component="h5" variant="h4" sx={{ mb: 0.5 }}>
        Covered by Dhan
      </Typography>
      <Typography variant="body2" sx={{ mb: coverage.differencesTotal > 0 ? 1 : 0 }}>
        {coverage.covered === 0
          ? 'Dhan’s own trade history covers none of the trades in this report.'
          : `${countOf(coverage.covered, 'trade')} of the report ${coverage.covered === 1 ? 'is' : 'are'} left out: Dhan’s own trade history has the same day, security and side, and its trades are the ones that count.`}
      </Typography>
      {coverage.differencesTotal > 0 && (
        <>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            {`${countOf(coverage.differencesTotal, 'day')} where the quantity in the report differs from Dhan’s. Dhan’s quantity is used.`}
          </Typography>
          <PagedTable
            label="Quantity differences from Dhan"
            noun="days"
            rows={coverage.differences}
            total={coverage.differencesTotal}
            columns={differenceColumns}
            rowKey={(row, index) => `${row.date}-${row.isin}-${row.side}-${String(index)}`}
          />
        </>
      )}
    </Box>
  );
}
