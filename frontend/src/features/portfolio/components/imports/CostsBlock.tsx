import { Box, Stack, Typography } from '@mui/material';
import type { DpLine, DpPreview } from '@/shared/api';
import { formatDate, formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { costStatusLabel, sideLabel } from '../../lib/reportImport';
import { Figure } from '../dividends/Figure';
import { PagedTable, type PagedColumn } from './PagedTable';

const columns: readonly PagedColumn<DpLine>[] = [
  { header: 'Line', numeric: true, render: (line) => String(line.line) },
  { header: 'Date', render: (line) => <time dateTime={line.date}>{formatDate(line.date)}</time> },
  {
    header: 'Security',
    render: (line) => (
      <>
        {line.name}
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {line.isin}
        </Typography>
      </>
    ),
  },
  { header: 'Quantity', numeric: true, render: (line) => formatNumber(line.quantity) },
  { header: 'Side', render: (line) => sideLabel(line.side) },
  { header: 'Type', render: (line) => line.type },
  { header: 'Charge', numeric: true, render: (line) => formatInrPrecise(line.charge) },
  { header: 'What happens', render: (line) => costStatusLabel(line.status) },
];

/**
 * A depository-charges sheet: how many lines are stored and why the others are not, the rupees an import adds to the
 * account's costs (they count in the net returns), the lines (50 at a time) and the names the file teaches.
 */
export function CostsBlock({ costs }: { costs: DpPreview }) {
  const { counts } = costs;
  return (
    <Stack spacing={1.5}>
      <Stack component="dl" direction="row" sx={{ m: 0, columnGap: 3, rowGap: 1.5, flexWrap: 'wrap' }}>
        <Figure label="Lines read" value={formatNumber(counts.read)} />
        <Figure label="To store" value={formatNumber(counts.toStore)} />
        <Figure label="Amount to store" value={formatInrPrecise(costs.amountToStore)} />
        <Figure label="Cost nothing" value={formatNumber(counts.zero)} />
        <Figure label="Buy side" value={formatNumber(counts.buySide)} />
        <Figure label="Already imported" value={formatNumber(counts.alreadyImported)} />
        <Figure label="Already in the ledger" value={formatNumber(counts.alreadyInLedger)} />
        <Figure label="Off-market" value={formatNumber(counts.offMarket)} />
      </Stack>
      <Typography variant="body2" color="text.secondary">
        Stored lines become costs of the account (class charges, marked as imported) and count in the net returns. Lines
        that cost nothing, buy-side lines and lines Dhan’s ledger has already are listed here and not stored.
      </Typography>
      <Box>
        <Typography component="h5" variant="h4" sx={{ mb: 0.5 }}>
          Lines
        </Typography>
        {costs.lines.length === 0 ? (
          <Typography color="text.secondary">The file has no charge lines.</Typography>
        ) : (
          <PagedTable
            label="Depository charge lines"
            noun="lines"
            rows={costs.lines}
            total={costs.linesTotal}
            columns={columns}
            rowKey={(line) => String(line.line)}
            minWidth={760}
          />
        )}
      </Box>
      {costs.names.new + costs.names.known > 0 && (
        <Typography variant="body2" color="text.secondary">
          {`The file names ${formatNumber(costs.names.new + costs.names.known)} securities: ${formatNumber(costs.names.new)} new to the app, ${formatNumber(costs.names.known)} known. Importing remembers the new ones, so later transaction reports find their ISIN.`}
        </Typography>
      )}
    </Stack>
  );
}
