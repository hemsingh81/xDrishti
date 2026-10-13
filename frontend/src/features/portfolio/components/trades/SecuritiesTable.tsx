import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import type { TradeSecurity } from '@/shared/api';
import { rebuildStatusMeaning } from '../../lib/tradeText';
import { SecurityRow } from './SecurityRow';
import { stickyFirst } from './stickyFirst';

const headings = [
  { label: 'Security', align: 'left' },
  { label: 'Status', align: 'left' },
  { label: 'Trades', align: 'left' },
  { label: 'Lots', align: 'right' },
  { label: 'Opening qty', align: 'right' },
  { label: 'Realised P&L', align: 'right' },
] as const;

/**
 * The securities of the file that the account holds or that are listed shares and ETFs: for each, how many trades, over
 * which dates, lots and opening quantity now → after, the profit the sells realised, and the status. The table scrolls
 * sideways on a narrow screen.
 */
export function SecuritiesTable({ securities }: { securities: readonly TradeSecurity[] }) {
  if (securities.length === 0) {
    return (
      <Typography color="text.secondary">
        No security of the file can be matched to a holding or a listed share.
      </Typography>
    );
  }
  return (
    <Box>
      <TableContainer role="region" aria-label="Securities in the file, scrolls sideways" tabIndex={0}>
        <Table size="small" aria-label="Securities in the file" sx={{ minWidth: 700 }}>
          <TableHead>
            <TableRow>
              {headings.map((heading) => (
                <TableCell
                  key={heading.label}
                  sx={{
                    fontWeight: 600,
                    textAlign: heading.align,
                    ...(heading.label === 'Security' ? stickyFirst : {}),
                  }}
                >
                  {heading.label}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {securities.map((security) => (
              <SecurityRow key={`${security.symbol}-${security.isin ?? ''}`} security={security} />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.75 }}>
        {rebuildStatusMeaning}
      </Typography>
    </Box>
  );
}
