import AddIcon from '@mui/icons-material/Add';
import CloseIcon from '@mui/icons-material/Close';
import { Box, Button, Divider, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useId } from 'react';
import type { PriceAlert } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { useToast } from '@/shared/ui/toastContext';
import { useCancelAlert } from '../../api/alertMutations';
import { alertCountQuery, alertsList } from '../../api/alertQueries';
import { alertPanelId, alertTabId, conditionText, defaultAlertLimit, type AlertStatus } from '../../lib/alerts';
import { problemText } from '../../lib/errors';
import { AlertsList } from './AlertsList';
import { AlertTabs } from './AlertTabs';

interface AlertsDrawerProps {
  tab: AlertStatus;
  onTabChange: (tab: AlertStatus) => void;
  onClose: () => void;
  /** New alert: the owner picks an instrument. */
  onCreate: () => void;
}

/**
 * The price alerts in a side panel: Active · Triggered · Cancelled (each with its count), "N of 200 active", how often
 * they are checked and a New alert button. Cancelling is immediate (the row goes at once and comes back if the server
 * refuses).
 */
export function AlertsDrawer({ tab, onTabChange, onClose, onCreate }: AlertsDrawerProps) {
  const titleId = useId();
  const { notify } = useToast();
  const cancel = useCancelAlert();
  const active = useQuery(alertCountQuery('active'));
  const triggered = useQuery(alertCountQuery('triggered'));
  const cancelled = useQuery(alertCountQuery('cancelled'));
  const counts: Record<AlertStatus, number | undefined> = {
    active: active.data?.total,
    triggered: triggered.data?.total,
    cancelled: cancelled.data?.total,
  };
  const list = useInfiniteRows(alertsList(tab));
  const limit = active.data?.activeLimit ?? defaultAlertLimit;

  async function onCancel(alert: PriceAlert) {
    try {
      const result = await cancel.mutateAsync(alert.id);
      notify(
        result.status === 'triggered'
          ? `The ${alert.symbol} alert had already triggered, so it stays in the Triggered list.`
          : `Cancelled the ${alert.symbol} alert (${conditionText(alert)}).`,
      );
    } catch (error) {
      notify(problemText(error, `Could not cancel the ${alert.symbol} alert. Nothing was changed.`), 'error');
    }
  }

  return (
    <Drawer
      anchor="right"
      open
      onClose={onClose}
      slotProps={{
        paper: { role: 'dialog', 'aria-labelledby': titleId, sx: { width: 520, maxWidth: '100vw' } },
      }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1, px: 2.5, py: 1.5 }}>
        <Typography id={titleId} component="h2" variant="h2" sx={{ flex: 1 }}>
          Price alerts
        </Typography>
        <Button size="small" variant="contained" startIcon={<AddIcon />} onClick={onCreate}>
          New alert
        </Button>
        <IconButton aria-label="Close alerts" size="small" edge="end" onClick={onClose}>
          <CloseIcon fontSize="small" />
        </IconButton>
      </Stack>
      <Divider />
      <Box sx={{ px: 2.5, pt: 1.5, overflowY: 'auto', flex: 1, pb: 3 }}>
        <Typography variant="body2" className="numeric" sx={{ fontWeight: 600 }}>
          {active.data
            ? `${formatNumber(active.data.activeCount)} of ${formatNumber(limit)} active`
            : `Up to ${formatNumber(limit)} active alerts`}
        </Typography>
        <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
          Checked every 5 minutes while the market is open. A move between two checks is not seen.
        </Typography>
        <AlertTabs value={tab} counts={counts} onChange={onTabChange} />
        <Box
          role="tabpanel"
          tabIndex={0}
          aria-busy={list.isPending}
          id={alertPanelId(tab)}
          aria-labelledby={alertTabId(tab)}
        >
          <AlertsList status={tab} result={list} onCancel={(alert) => void onCancel(alert)} />
        </Box>
      </Box>
    </Drawer>
  );
}
