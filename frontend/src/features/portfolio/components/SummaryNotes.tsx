import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Chip, Stack, Typography } from '@mui/material';
import type { IncomeView } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';

interface SummaryNotesProps {
  /** Open lots that still carry the import's default date. */
  defaultDateLots: number;
  /** Open holdings without a usable price: left out of value, P&L and XIRR. */
  unpricedCount: number;
  /** Open holdings left out of the day change for lack of a previous close. */
  dayChangeExcluded: number;
  /** Show those holdings first: oldest purchase date first. */
  onReviewDates: () => void;
  /** While the live feed runs: how many holdings have no live tick (unmapped, beyond the cap, not traded yet); undefined when not live. */
  notLive?: { count: number; truncated: boolean };
  /** The scope's dividends and costs; null when none is recorded (every figure is then a price return). */
  income: IncomeView | null;
}

/** Under the tiles: the lots whose date was never chosen (a way to find them), and what the figures leave out. */
export function SummaryNotes({
  defaultDateLots,
  unpricedCount,
  dayChangeExcluded,
  onReviewDates,
  notLive,
  income,
}: SummaryNotesProps) {
  return (
    <Stack direction="row" sx={{ mb: 2, alignItems: 'center', flexWrap: 'wrap', columnGap: 1.5, rowGap: 0.5 }}>
      {defaultDateLots > 0 && (
        <Chip
          clickable
          size="small"
          color="warning"
          variant="outlined"
          icon={<WarningIcon />}
          label={`${formatNumber(defaultDateLots)} ${defaultDateLots === 1 ? 'lot uses' : 'lots use'} the default date`}
          title="Show the oldest purchase dates first, where the default dates are"
          onClick={onReviewDates}
        />
      )}
      {unpricedCount > 0 && (
        <Typography variant="caption" color="text.secondary">
          {unpricedCount === 1
            ? '1 holding without a price is left out of value, P&L and XIRR.'
            : `${formatNumber(unpricedCount)} holdings without a price are left out of value, P&L and XIRR.`}
        </Typography>
      )}
      {dayChangeExcluded > 0 && (
        <Typography variant="caption" color="text.secondary">
          {`Day change excludes ${formatNumber(dayChangeExcluded)} ${dayChangeExcluded === 1 ? 'holding' : 'holdings'} without a previous close`}
        </Typography>
      )}
      {notLive !== undefined && notLive.count > 0 && (
        <Typography variant="caption" color="text.secondary">
          {`${formatNumber(notLive.count)} ${notLive.count === 1 ? 'holding is' : 'holdings are'} not live: ${notLive.count === 1 ? 'it keeps' : 'they keep'} the last quote or synced price${notLive.truncated ? ' (a connection follows at most 1,000 prices)' : ''}`}
        </Typography>
      )}
      <Typography variant="caption" color="text.secondary">
        {income === null
          ? 'Dividends and charges are not recorded yet, so these are price returns. Corporate actions are not included.'
          : 'Net figures count dividends and costs only from the dates on their tiles. Corporate actions are not included.'}
      </Typography>
      {income !== null && notLive !== undefined && (
        <Typography variant="caption" color="text.secondary">
          Net total return moves with the live prices; XIRR and net XIRR are worked out at each read.
        </Typography>
      )}
    </Stack>
  );
}
