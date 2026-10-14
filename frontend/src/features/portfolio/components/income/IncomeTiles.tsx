import PaymentsIcon from '@mui/icons-material/PaymentsOutlined';
import PercentIcon from '@mui/icons-material/PercentOutlined';
import QueryStatsIcon from '@mui/icons-material/QueryStatsOutlined';
import ReceiptIcon from '@mui/icons-material/ReceiptLongOutlined';
import { Box, Button, Card, CardContent, Stack, Typography } from '@mui/material';
import type { IncomeView } from '@/shared/api';
import { formatInr } from '@/shared/lib/format';
import { StatTile } from '@/shared/ui';
import { numeric } from '@/theme';
import { costsHint, coverageHint, dividendsHint, ledgerChargesNote, netXirrOf, unallocatedOf } from '../../lib/income';
import { NetXirr, ReturnPct } from './IncomeFigures';
import { ReturnBasisHelp } from './ReturnBasisHelp';

export const incomeRegionLabel = 'Dividends and costs';

/**
 * The money the portfolio received and paid besides its price return, with the net figures that include them. Each tile says
 * from which day its figure is counted - dividends from the first one entered, costs from each source (trade charges, account
 * costs) on its own day, the net figures from the later of those - so a total is never read as "all there was". The gross
 * figures above are not changed.
 */
export function IncomeTiles({ income }: { income: IncomeView }) {
  const unallocated = unallocatedOf(income);
  const xirr = netXirrOf(income);
  const accountCosts = ledgerChargesNote(income, formatInr);
  return (
    <Box
      component="section"
      aria-label={incomeRegionLabel}
      sx={(theme) => ({
        display: 'grid',
        gap: 2,
        mb: 1,
        gridTemplateColumns: 'minmax(0, 1fr)',
        [theme.breakpoints.up('sm')]: { gridTemplateColumns: 'repeat(2, minmax(0, 1fr))' },
        [theme.breakpoints.up('lg')]: { gridTemplateColumns: 'repeat(4, minmax(0, 1fr))' },
      })}
    >
      <StatTile
        label="Dividends"
        value={formatInr(income.dividends)}
        hint={`${dividendsHint(income)}${unallocated === null ? '' : ` · ${formatInr(unallocated)} unallocated`}`}
        icon={PaymentsIcon}
        hue="green"
      />
      <StatTile
        label="Costs"
        value={formatInr(income.charges)}
        hint={
          <>
            {accountCosts && <span style={{ display: 'block' }}>{accountCosts}</span>}
            <span>{costsHint(income)}</span>
          </>
        }
        icon={ReceiptIcon}
        hue="amber"
      />
      <StatTile
        label="Net total return"
        value={<ReturnPct value={income.netTotalReturnPct} />}
        hint={
          <>
            <span>Before costs </span>
            <ReturnPct value={income.totalReturnPct} />
            {` · ${coverageHint(income)}`}
            <ReturnBasisHelp />
          </>
        }
        icon={PercentIcon}
        hue="teal"
      />
      <StatTile
        label="Net XIRR"
        value={<NetXirr income={income} />}
        hint={'short' in xirr ? xirr.short : `with dividends and costs · ${coverageHint(income)}`}
        icon={QueryStatsIcon}
        hue="violet"
      />
    </Box>
  );
}

interface IncomeInviteProps {
  /** Opens the Cash & dividends panel on its Add dividend form. */
  onAddDividend: () => void;
  /** Takes the owner to the Sync trades & ledger control. */
  onReadLedger: () => void;
}

/**
 * Shown instead of the tiles while nothing is recorded: no dividend entered, no charge read. It says so and offers the two
 * ways to change that - zeros would claim there were none.
 */
export function IncomeInvite({ onAddDividend, onReadLedger }: IncomeInviteProps) {
  return (
    <Card component="section" aria-label={incomeRegionLabel} sx={{ mb: 1 }}>
      <CardContent sx={{ '&:last-child': { pb: 2 } }}>
        <Stack
          direction={{ xs: 'column', md: 'row' }}
          sx={{ gap: 1.5, alignItems: { md: 'center' }, justifyContent: 'space-between' }}
        >
          <Box sx={{ minWidth: 0 }}>
            <Typography component="h2" variant="h3">
              Dividends and costs are not in the figures yet
            </Typography>
            <Typography variant="body2" color="text.secondary" sx={numeric}>
              The figures above are price returns. Add the dividends you received, and read your trades and ledger from
              Dhan for the charges you paid, to see net return and net XIRR too.
            </Typography>
          </Box>
          <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', flexShrink: 0 }}>
            <Button variant="contained" startIcon={<PaymentsIcon />} onClick={onAddDividend}>
              Add a dividend
            </Button>
            <Button variant="outlined" startIcon={<ReceiptIcon />} onClick={onReadLedger}>
              Read the ledger
            </Button>
          </Stack>
        </Stack>
      </CardContent>
    </Card>
  );
}
