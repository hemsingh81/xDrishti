import InfoIcon from '@mui/icons-material/InfoOutlined';
import { Box, Stack, Typography } from '@mui/material';
import type { BrokerKindStatus } from '@/shared/api';
import { formatAgo } from '@/shared/lib/format';
import { healthText } from '@/shared/lib/syncHealth';
import { numeric } from '@/theme';
import { summariseKind, type BrokerKind } from '../../lib/brokerText';
import { toneLook } from '../toneLook';
import { BrokerDropDetails } from './BrokerDropDetails';

/**
 * "Trades read up to 11 Oct 2026 5 min ago — 4 new" / "Reading ledger… — 3 of 12 windows done" / the reason a read failed,
 * and, when the read is not healthy, "retrying in 5 minutes (attempt 2 of 3)" or "failed for 2 days: Dhan could not be reached".
 */
export function BrokerKindLine({ kind, status }: { kind: BrokerKind; status: BrokerKindStatus }) {
  const summary = summariseKind(kind, status);
  const health = healthText(status);
  const look = summary.tone === 'idle' ? null : toneLook[summary.tone];
  const { drops } = summary;
  return (
    <Stack>
      <Stack
        direction="row"
        spacing={0.75}
        role={summary.tone === 'error' ? 'alert' : summary.tone === 'info' ? 'status' : undefined}
        sx={{ alignItems: 'flex-start' }}
      >
        <Stack component="span" sx={{ color: look?.color ?? 'text.secondary', pt: '1px' }} aria-hidden>
          {look?.icon ?? <InfoIcon fontSize="small" />}
        </Stack>
        <Typography sx={{ ...numeric, overflowWrap: 'anywhere' }}>
          <strong>{summary.title}</strong>
          {summary.at && (
            <>
              {' '}
              <time dateTime={summary.at}>{formatAgo(summary.at)}</time>
            </>
          )}
          {summary.detail && <> — {summary.detail}</>}
          {health && <> · {health}</>}
          {drops && (
            <>
              {' '}
              ·{' '}
              <Box component="span" sx={drops.quiet ? { color: 'text.secondary' } : undefined}>
                {drops.text}
              </Box>
            </>
          )}
        </Typography>
      </Stack>
      {drops && !drops.quiet && <BrokerDropDetails name={kind} drops={drops} />}
    </Stack>
  );
}
