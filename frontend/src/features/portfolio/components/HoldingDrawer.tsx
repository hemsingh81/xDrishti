import CloseIcon from '@mui/icons-material/Close';
import { Box, Chip, Drawer, IconButton, Stack, Typography } from '@mui/material';
import type { HoldingRow } from '@/shared/api';
import {
  formatDate,
  formatDateTime,
  formatInr,
  formatInrPrecise,
  formatIstDateTime,
  formatNumber,
} from '@/shared/lib/format';
import { Facts, type Fact } from '@/shared/ui';
import { monoFontFamily } from '@/theme';
import { noDayChangeReasonOf } from '../lib/figures';
import { DayChangeAmount, Pnl, PnlPercent, Xirr } from './Figures';
import { LotsSection } from './LotsSection';

const priceSourceLabel = (source: string): string => (source === 'quote' ? 'Quote' : 'Synced');

function factsOf(h: HoldingRow): Fact[] {
  const { returns } = h;
  return [
    { label: 'Account', value: h.accountName },
    { label: 'Bucket', value: h.bucketName },
    { label: 'Quantity', value: formatNumber(h.qty) },
    { label: 'Average cost', value: formatInrPrecise(h.avgCost) },
    { label: 'Last price', value: formatInrPrecise(h.lastPrice) },
    { label: 'Previous close', value: h.previousClose === null ? '—' : formatInrPrecise(h.previousClose) },
    { label: 'Day change', value: <DayChangeAmount value={h.dayChange} reason={noDayChangeReasonOf(h)} focusable /> },
    { label: 'Day change %', value: h.dayChange === null ? '—' : <PnlPercent value={h.dayChangePct} /> },
    { label: 'Price time (IST)', value: h.priceAt === null ? '—' : formatIstDateTime(h.priceAt) },
    { label: 'Price source', value: priceSourceLabel(h.priceSource) },
    { label: 'Invested', value: formatInr(returns.invested) },
    { label: 'Value', value: returns.value === null ? '—' : formatInr(returns.value) },
    { label: 'Profit / loss', value: <Pnl value={returns.pnl} focusable /> },
    { label: 'Profit / loss %', value: <PnlPercent value={returns.pnlPct} /> },
    { label: 'XIRR', value: <Xirr returns={returns} focusable /> },
    {
      label: returns.realisedEstimated ? 'Realised (est.)' : 'Realised',
      value: <Pnl value={returns.realisedPnl} />,
    },
    { label: 'First seen', value: formatDate(h.firstSeenAt) },
    { label: 'Last synced', value: formatDateTime(h.syncedAt) },
    ...(h.isin ? [{ label: 'ISIN', value: <span>{h.isin}</span> }] : []),
  ];
}

interface HoldingDrawerProps {
  /** The open holding; null keeps the drawer closed. */
  id: string | null;
  /** Its row in the list, when it is on the current page (the summary needs it; the lots do not). */
  holding: HoldingRow | undefined;
  onClose: () => void;
}

/** Side panel for one holding: its figures, then its lots with their dates. */
export function HoldingDrawer({ id, holding, onClose }: HoldingDrawerProps) {
  return (
    <Drawer
      anchor="right"
      open={id !== null}
      onClose={onClose}
      slotProps={{
        paper: { 'aria-label': 'Holding details', sx: { width: { xs: '100%', md: 760 }, maxWidth: '100%' } },
      }}
    >
      <Stack direction="row" sx={{ p: 2, gap: 1, alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h2" sx={{ fontFamily: monoFontFamily, overflowWrap: 'anywhere' }}>
            {holding?.symbol ?? 'Holding'}
          </Typography>
          {holding && <Typography color="text.secondary">{holding.name}</Typography>}
          {holding && (!holding.mapped || holding.status === 'closed' || !holding.priced) && (
            <Stack direction="row" spacing={0.75} sx={{ mt: 0.75 }}>
              {!holding.mapped && <Chip size="small" variant="outlined" color="warning" label="Not in master list" />}
              {holding.status === 'open' && !holding.priced && (
                <Chip size="small" variant="outlined" color="warning" label="No price yet" />
              )}
              {holding.status === 'closed' && <Chip size="small" variant="outlined" label="Closed" />}
            </Stack>
          )}
        </Box>
        <IconButton aria-label="Close details" edge="end" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Box sx={{ px: 2, pb: 3, overflowY: 'auto' }}>
        <Stack spacing={3}>
          {holding && <Facts items={factsOf(holding)} />}
          {id !== null && <LotsSection holdingId={id} />}
        </Stack>
      </Box>
    </Drawer>
  );
}
