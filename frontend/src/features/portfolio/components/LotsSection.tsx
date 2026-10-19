import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { RetryAlert } from './RetryAlert';
import { useQuery } from '@tanstack/react-query';
import { formatDate } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { lotsQuery } from '../api/portfolioQueries';
import { isOpeningLot } from '../lib/tradeLots';
import { LotRowView } from './LotRowView';

const headings = ['Acquired', 'Qty', 'Unit cost', 'Invested', 'Value', 'P&L', 'XIRR', 'Status'] as const;

/** The lots of one holding with their own figures; each date can be corrected. */
export function LotsSection({ holdingId }: { holdingId: string }) {
  const query = useQuery(lotsQuery(holdingId));
  if (query.isPending) return <LoadingState label="Loading lots" />;
  if (query.isError) {
    return <RetryAlert error={query.error} fallback="The lots could not be loaded." onRetry={query.refetch} />;
  }
  const { lots, valuationDate } = query.data;
  return (
    <Box>
      <Typography variant="h3" sx={{ mb: 0.5 }}>
        Lots
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        Values as of {formatDate(valuationDate)}. A lot on the default date was not dated by you: set its real purchase
        date for a true XIRR. Lots from trades take their dates from the trades.
      </Typography>
      <TableContainer>
        <Table size="small" aria-label="Lots">
          <TableHead>
            <TableRow>
              {headings.map((heading) => (
                <TableCell
                  key={heading}
                  sx={{ fontWeight: 600, textAlign: heading === 'Acquired' || heading === 'Status' ? 'left' : 'right' }}
                >
                  {heading}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {lots.map((lot) => (
              <LotRowView key={lot.id} holdingId={holdingId} lot={lot} opening={isOpeningLot(lots, lot)} />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
    </Box>
  );
}
