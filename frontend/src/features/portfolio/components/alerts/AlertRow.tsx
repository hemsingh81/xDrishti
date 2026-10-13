import ArrowDownIcon from '@mui/icons-material/SouthOutlined';
import ArrowUpIcon from '@mui/icons-material/NorthOutlined';
import { Box, Button, Stack, Typography } from '@mui/material';
import type { PriceAlert } from '@/shared/api';
import { formatInrPrecise, formatIstDate, formatIstDateTime } from '@/shared/lib/format';
import { monoFontFamily } from '@/theme';
import { conditionText } from '../../lib/alerts';

interface AlertRowProps {
  alert: PriceAlert;
  onCancel: (alert: PriceAlert) => void;
}

/**
 * One alert: the instrument, the condition in words with an arrow, the note (plain text), when it was set and, if it
 * triggered, at which price and when. Only an active alert can be cancelled (no confirmation: setting it again is easy).
 */
export function AlertRow({ alert, onCancel }: AlertRowProps) {
  const above = alert.direction === 'at_or_above';
  const Arrow = above ? ArrowUpIcon : ArrowDownIcon;
  return (
    <Stack
      component="li"
      direction="row"
      sx={{ gap: 1.5, py: 1.5, alignItems: 'flex-start', justifyContent: 'space-between' }}
    >
      <Box sx={{ minWidth: 0, flex: 1 }}>
        <Stack direction="row" sx={{ alignItems: 'baseline', columnGap: 1, flexWrap: 'wrap' }}>
          <Typography component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600, overflowWrap: 'anywhere' }}>
            {alert.symbol}
          </Typography>
          <Typography component="span" variant="caption" color="text.secondary">
            {alert.name}
          </Typography>
        </Stack>
        <Stack direction="row" sx={{ alignItems: 'center', gap: 0.5, mt: 0.25 }}>
          <Arrow fontSize="inherit" aria-hidden />
          <Typography component="span" className="numeric" sx={{ fontWeight: 600 }}>
            {conditionText(alert)}
          </Typography>
        </Stack>
        {alert.note && (
          <Typography variant="body2" color="text.secondary" sx={{ mt: 0.25, overflowWrap: 'anywhere' }}>
            {alert.note}
          </Typography>
        )}
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.25 }}>
          {`Set ${formatIstDate(alert.createdAt)}`}
        </Typography>
        {alert.status === 'triggered' && alert.triggeredAt && (
          <Typography variant="body2" className="numeric" sx={{ mt: 0.25, fontWeight: 600 }}>
            {alert.triggerPrice === null
              ? `Triggered ${formatIstDateTime(alert.triggeredAt)}`
              : `Triggered at ${formatInrPrecise(alert.triggerPrice)}, ${formatIstDateTime(alert.triggeredAt)}`}
          </Typography>
        )}
        {alert.status === 'cancelled' && (
          <Typography variant="body2" sx={{ mt: 0.25, fontWeight: 600 }}>
            Cancelled
          </Typography>
        )}
      </Box>
      {alert.status === 'active' && (
        <Button
          size="small"
          variant="outlined"
          aria-label={`Cancel alert on ${alert.symbol} ${conditionText(alert)}`}
          onClick={() => {
            onCancel(alert);
          }}
        >
          Cancel
        </Button>
      )}
    </Stack>
  );
}
