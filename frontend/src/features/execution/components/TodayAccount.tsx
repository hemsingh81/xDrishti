import { Alert, Card, CardContent, Chip, Stack, Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { modeWord } from '../lib/stateWords';
import { CapsMeters } from './CapsMeters';

type Account = Schemas['OverviewAccount'];

/** One account on Today: its effective mode, the caps meters, the open halts and alarms. */
export function TodayAccount({ account }: { account: Account }) {
  return (
    <Card component="section" aria-label={`${account.name} today`}>
      <CardContent sx={{ display: 'grid', gap: 2 }}>
        <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="h2" sx={{ fontSize: 18 }}>
            {account.name}
          </Typography>
          <Chip label={`Mode: ${modeWord(account.mode)}`} size="small" />
          {account.effectiveFrom && (
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              since {formatIstDateTime(account.effectiveFrom)}
            </Typography>
          )}
          {account.pendingMode && (
            <Typography variant="caption" sx={{ color: 'text.secondary' }}>
              Next: {modeWord(account.pendingMode)}
              {account.pendingFrom ? ` from ${formatIstDateTime(account.pendingFrom)}` : ''}
            </Typography>
          )}
        </Stack>
        {!account.gate.open && (
          <Typography variant="body2" sx={{ color: 'text.secondary' }}>
            Gate closed: {account.gate.reason}
          </Typography>
        )}
        <CapsMeters caps={account.caps} />
        <Typography variant="body2">
          Orders waiting for you: {account.openTickets}. Positions at risk: {account.positionsAtRisk}.
        </Typography>
        {account.halts.map((h) => (
          <Alert key={h.id} severity="error">
            Halt {h.id} ({h.level}): {h.reason}
          </Alert>
        ))}
        {account.alarms
          .filter((a) => a.clearedAt === null)
          .map((a) => (
            <Alert key={a.id} severity={a.severity === 'info' ? 'info' : a.severity === 'warn' ? 'warning' : 'error'}>
              {a.code}: {a.detail}
            </Alert>
          ))}
      </CardContent>
    </Card>
  );
}
