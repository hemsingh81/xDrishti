import { Alert, Box, Typography } from '@mui/material';
import { EvidenceBlock, formatR } from '@/shared/analytics';
import { formatIstDateTime, formatPrice } from '@/shared/lib/format';
import type { TradeReplay } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { Panel } from './Panel';

const words = (text: string) => text.replaceAll('_', ' ');

/** The stored facts of the trade, the costing under its basis words, and the evidence block. Server text is rendered as text. */
export function ReplayFacts({ view }: { view: TradeReplay }) {
  const { trade, costing } = view;
  const rows: [string, string][] = [
    ['Strategy', `${trade.strategyId}, ${trade.triggerTimeframe}`],
    ['Side', trade.side],
    ['Decision', formatIstDateTime(trade.decisionAt)],
    [
      'Entry',
      `${formatPrice(trade.entryPrice)} (filled ${formatPrice(trade.entryFilledPrice)}), ${formatIstDateTime(trade.entryAt)}`,
    ],
    ['Stop', formatPrice(trade.stopPrice)],
    ['Target', trade.targetPrice === null ? 'none (no fixed reward-to-risk)' : formatPrice(trade.targetPrice)],
    [
      'Exit',
      `${formatPrice(trade.exitPrice)} (filled ${formatPrice(trade.exitFilledPrice)}), ${formatIstDateTime(trade.exitAt)}`,
    ],
    ['Exit reason', words(trade.exitReason)],
    ['R exact / filled', `${formatR(trade.rExact)} / ${formatR(trade.rFilled)} (slippage ${formatR(trade.slippageR)})`],
    ['MAE / MFE', `${formatR(-trade.maeR)} / ${formatR(trade.mfeR)}`],
    ['Sessions held', `${String(trade.sessionsHeld)}, ${String(trade.sessionsUnseen)} unseen`],
    ['Fills', `${trade.fills}, entry ${words(trade.entryFill)}${trade.tickFallback ? ', default tick used' : ''}`],
  ];
  return (
    <Panel title="The stored trade">
      <Box component="dl" sx={{ m: 0, display: 'grid', gridTemplateColumns: 'auto 1fr', columnGap: 2, rowGap: 0.75 }}>
        {rows.map(([name, text]) => (
          <Box key={name} sx={{ display: 'contents' }}>
            <Typography component="dt" variant="body2" color="text.secondary">
              {name}
            </Typography>
            <Typography component="dd" variant="body2" sx={{ m: 0, overflowWrap: 'anywhere' }}>
              {text}
            </Typography>
          </Box>
        ))}
      </Box>
      {costing &&
        (costing.confirmed ? (
          <Typography variant="body2" sx={{ mt: 1.5 }}>
            {`Net of charges (sheet ${costing.sheetId}, confirmed as yours): ${formatR(costing.rNet)}; charges ₹${String(costing.totalCharges)} on ${String(costing.quantity)} shares.`}
          </Typography>
        ) : (
          <Alert severity="info" sx={{ mt: 1.5 }}>
            {`Costed on sheet ${costing.sheetId}, not confirmed as yours. The figures above are gross.`}
          </Alert>
        ))}
      <Box sx={{ mt: 1.5 }}>
        <EvidenceBlock {...evidenceOf(view.evidence)} compact />
      </Box>
    </Panel>
  );
}
