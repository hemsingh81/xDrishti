import { Box, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

const num = (v: number | null): string => (v === null ? 'n/a' : formatNumber(v));

/** Status is a word, never a colour alone (CF-05). */
const word = (status: string): string => (status === 'pass' ? 'pass' : status === 'fail' ? 'fail' : 'unavailable');

/**
 * Costs and delay (CF-05): the out-of-sample edge with charges and slippage at 1x, 1.5x and 2x and the fill-one-minute-later line, beside the outcome.
 * Report-only unless the owner has enforced it; says so, and says when the costs are not checked against a bill. Absent in a result stored before it.
 */
export function CostStressBlock({ block }: { block?: Schemas['CostStressView'] | null }) {
  if (!block) {
    return null;
  }
  return (
    <Box component="section" aria-label="Costs and delay" sx={{ mt: 1 }}>
      <Typography variant="subtitle2">Costs and delay</Typography>
      <Typography variant="body2">
        {`Tradable at the worst of 1x, 1.5x and 2x costs: ${word(block.tradable)}. ${block.enforce ? (block.capped ? 'Enforced: the outcome was capped at MARGINAL.' : 'Enforced.') : 'Report only: the outcome is not changed.'}`}
      </Typography>
      {!block.costsChecked && (
        <Typography variant="body2" color="warning.main">
          Costs not checked against a bill: stress gates cannot PASS.
        </Typography>
      )}
      <Table size="small" aria-label="Cost stress">
        <TableHead>
          <TableRow>
            <TableCell>Costs</TableCell>
            <TableCell align="right">Trades</TableCell>
            <TableCell align="right">Net expectancy R</TableCell>
            <TableCell align="right">Needs</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>Reason</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {block.rows.map((r) => (
            <TableRow key={r.multiplier}>
              <TableCell>{`${formatNumber(r.multiplier)}x`}</TableCell>
              <TableCell align="right">{r.trades}</TableCell>
              <TableCell align="right">{num(r.expectancyR)}</TableCell>
              <TableCell align="right">{`${r.comparison} ${formatNumber(r.threshold)}`}</TableCell>
              <TableCell>{word(r.status)}</TableCell>
              <TableCell>{r.reason}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      {block.delay && (
        <Typography variant="body2" sx={{ mt: 1 }}>
          {`Fill ${block.delay.minutes} minute later: ${word(block.delay.status)}. ${block.delay.signals} signals, ${block.delay.delayedFilled} still fill; expectancy per signal ${num(block.delay.baseMeanR)} R to ${num(block.delay.delayedMeanR)} R${block.delay.retention === null ? '' : ` (${formatNumber(block.delay.retention * 100)} % kept, needs ${formatNumber(block.delay.required * 100)} %)`}. ${block.delay.reason}`}
        </Typography>
      )}
      <Typography variant="caption" component="p" color="text.secondary">
        {`${block.limit} ${block.formula}.`}
      </Typography>
    </Box>
  );
}
