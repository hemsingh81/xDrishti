import PauseIcon from '@mui/icons-material/PauseCircleOutlineOutlined';
import ScheduleIcon from '@mui/icons-material/ScheduleOutlined';
import { Box, Link, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import type { DhanOverview, EodOverview } from '@/shared/api';
import { formatIstDayAndTime } from '@/shared/lib/format';
import { lastRunLook, lastRunReason } from '../lib/eod';
import { runIcons, toneColour } from './autoUpdateLook';

interface AutomaticUpdateDetailsProps {
  eod: EodOverview;
  /** The Data account: a skipped run that could not fetch leads to reconnecting it. */
  dhan: DhanOverview;
}

/**
 * The scheduled end-of-day run in full, for the popover of the ribbon's Auto item: when it runs, when it runs next and how
 * the last automatic run ended. A failed or skipped run says why (and a skip for the Data account links to reconnecting
 * it); a switched-off schedule says so. The outcome is a word and an icon as well as a colour.
 */
export function AutomaticUpdateDetails({ eod, dhan }: AutomaticUpdateDetailsProps) {
  const run = eod.lastRun;
  const look = run ? lastRunLook(run) : null;
  const reason = run ? lastRunReason(run) : null;
  const Icon = !eod.enabled ? PauseIcon : run ? (runIcons[run.status] ?? ScheduleIcon) : ScheduleIcon;
  const iconColour = !eod.enabled || !look ? 'text.secondary' : toneColour[look.tone];
  const reconnect = run?.status === 'skipped' && !dhan.canFetch;
  const title = (
    <Typography component="span" variant="body2" sx={{ fontWeight: 700 }}>
      Automatic update
    </Typography>
  );

  return (
    <Stack direction="row" spacing={1.25} sx={{ alignItems: 'flex-start' }}>
      <Icon fontSize="small" aria-hidden sx={{ color: iconColour, mt: 0.25 }} />
      <Box sx={{ minWidth: 0, overflowWrap: 'anywhere' }}>
        {eod.enabled ? (
          <Typography variant="body2" component="p">
            {title}
            {': Daily at '}
            {eod.timeIst} IST on trading days
            {eod.nextRunAt && (
              <>
                {' · next '}
                <time dateTime={eod.nextRunAt}>{formatIstDayAndTime(eod.nextRunAt)}</time>
              </>
            )}
            {' · '}
            {run && look ? (
              <>
                last automatic run {look.word} <time dateTime={look.iso}>{look.at}</time>
              </>
            ) : (
              'no automatic run yet'
            )}
          </Typography>
        ) : (
          <Typography variant="body2" component="p">
            {title} is switched off
          </Typography>
        )}
        {eod.enabled && reason && (
          <Typography
            variant="body2"
            sx={{ color: look?.tone === 'error' ? 'error.main' : 'text.secondary', mt: 0.25 }}
          >
            {reason}{' '}
            {reconnect && (
              <Link component={RouterLink} to={dhan.account ? `/accounts?account=${dhan.account.id}` : '/accounts'}>
                {dhan.account ? 'Reconnect' : 'Open Accounts'}
              </Link>
            )}
          </Typography>
        )}
      </Box>
    </Stack>
  );
}
