import InfoIcon from '@mui/icons-material/InfoOutlined';
import { Stack, Typography } from '@mui/material';
import type { BrokerKindStatus } from '@/shared/api';
import { formatAgo } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { summariseKind, type BrokerKind } from '../../lib/brokerText';
import { toneLook } from '../toneLook';

/** "Trades read up to 11 Oct 2026 5 min ago — 4 new" / "Reading ledger… — 3 of 12 windows done" / the reason a read failed. */
export function BrokerKindLine({ kind, status }: { kind: BrokerKind; status: BrokerKindStatus }) {
  const summary = summariseKind(kind, status);
  const look = summary.tone === 'idle' ? null : toneLook[summary.tone];
  return (
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
      </Typography>
    </Stack>
  );
}
