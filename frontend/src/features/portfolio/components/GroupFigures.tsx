import { Box, Stack, Typography } from '@mui/material';
import type { HoldingGroupTotal } from '@/shared/api';
import { formatInr, formatShare } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { noPriceReason, xirrOf } from '../lib/figures';
import { unpricedText } from '../lib/groups';
import { DayChangeAmount, Pnl, PnlPercent, Unavailable, Xirr } from './Figures';

/** The figures of a group or of the scope total (a group has the same ones, plus its key, name and parts). */
export type GroupFigures = HoldingGroupTotal;

/** Table cells line figures up on the right, the labelled pairs of a phone card on the left. */
type Align = 'flex-end' | 'flex-start';

const noDayChange =
  'No previous close yet for any open holding here, so there is no day change. It arrives with the next quote.';

const noInvested =
  'None of these holdings has a price yet, so they are left out of the totals, invested included. It appears with the first price.';

/** What was put in; "—" with the reason when no holding of the group has a price (the API leaves them out: 0 would mislead). */
export function GroupInvested({ group }: { group: GroupFigures }) {
  if (group.holdings > 0 && group.unpriced === group.holdings) return <Unavailable label="—" reason={noInvested} />;
  return <>{formatInr(group.invested)}</>;
}

/** The value of the priced holdings; "—" with the reason when none is priced. */
export function GroupValue({ group }: { group: GroupFigures }) {
  return group.value === null ? <Unavailable label="—" reason={noPriceReason} /> : <>{formatInr(group.value)}</>;
}

/** Profit or loss in rupees over its percentage, like the day change of a holding. */
export function GroupPnl({ group, align = 'flex-end' }: { group: GroupFigures; align?: Align }) {
  return (
    <Stack sx={{ alignItems: align, lineHeight: 1.25 }}>
      <Pnl value={group.pnl} />
      {group.pnl !== null && (
        <Typography component="span" variant="caption" sx={numeric}>
          <PnlPercent value={group.pnlPct} />
        </Typography>
      )}
    </Stack>
  );
}

/** XIRR from the union of the group's cash flows; the reason in words under "—" when there is none. */
export function GroupXirr({ group, align = 'flex-end' }: { group: GroupFigures; align?: Align }) {
  const xirr = xirrOf(group);
  return (
    <Stack sx={{ alignItems: align, lineHeight: 1.25 }}>
      <Xirr returns={group} />
      {'short' in xirr && (
        <Typography component="span" variant="caption" color="text.secondary" sx={{ whiteSpace: 'normal' }}>
          {xirr.short}
        </Typography>
      )}
    </Stack>
  );
}

export function GroupDayChange({ group, align = 'flex-end' }: { group: GroupFigures; align?: Align }) {
  return (
    <Stack sx={{ alignItems: align, lineHeight: 1.25 }}>
      <DayChangeAmount value={group.dayChange} reason={noDayChange} />
      {group.dayChange !== null && (
        <Typography component="span" variant="caption" sx={numeric}>
          <PnlPercent value={group.dayChangePct} />
        </Typography>
      )}
      {group.dayChangeExcluded > 0 && (
        <Typography
          component="span"
          variant="caption"
          color="text.secondary"
          title={`${String(group.dayChangeExcluded)} ${group.dayChangeExcluded === 1 ? 'holding has' : 'holdings have'} no previous close yet and ${group.dayChangeExcluded === 1 ? 'is' : 'are'} left out of the day change.`}
        >
          {`${String(group.dayChangeExcluded)} excluded`}
        </Typography>
      )}
    </Stack>
  );
}

/** The share of the scope's value as a bar and a number. The bar is decoration; the number is the information. */
export function GroupWeight({ weightPct }: { weightPct: number | null }) {
  if (weightPct === null) return <>—</>;
  return (
    <Stack direction="row" sx={{ alignItems: 'center', gap: 1, justifyContent: 'flex-end' }}>
      <Box
        aria-hidden
        sx={{ flex: '1 1 auto', minWidth: 48, maxWidth: 96, height: 8, borderRadius: 4, bgcolor: 'action.selected' }}
      >
        <Box
          sx={{
            width: `${String(Math.min(100, Math.max(0, weightPct)))}%`,
            height: '100%',
            borderRadius: 4,
            bgcolor: 'primary.main',
          }}
        />
      </Box>
      <Box component="span" sx={{ minWidth: '4.5ch', textAlign: 'right' }}>{`${formatShare(weightPct)}%`}</Box>
    </Stack>
  );
}

/** "2 without a price" (or "3 holdings without a price" when none of the group is priced): why a figure is short. */
export function UnpricedFlag({ holdings, unpriced }: { holdings: number; unpriced: number }) {
  const text = unpricedText(holdings, unpriced);
  return text === null ? null : (
    <Typography component="span" variant="caption" color="warning.main" sx={{ display: 'block', whiteSpace: 'normal' }}>
      {text}
    </Typography>
  );
}
