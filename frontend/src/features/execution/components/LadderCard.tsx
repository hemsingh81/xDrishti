import { Card, CardContent, Stack, Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { formatIstDateTime } from '@/shared/lib/format';
import { toOutcome } from '../outcome';
import { modeWord } from '../lib/stateWords';
import { CheckStatus } from './CheckStatus';
import { Ladder } from './Ladder';
import { LimitsForm } from './LimitsForm';
import { PromotionControls } from './PromotionControls';

type Account = Schemas['ExecutionModeAccountView'];

/** One account on the Modes tab: its rung, the next gate with its checks, the controls, the trail and the limits. */
export function LadderCard({ account }: { account: Account }) {
  const next = account.next;
  return (
    <Card component="section" aria-label={`${account.name} modes`}>
      <CardContent sx={{ display: 'grid', gap: 2 }}>
        <Typography variant="h2" sx={{ fontSize: 18 }}>
          {account.name}
        </Typography>
        <Ladder mode={account.effectiveMode} />
        {account.configuredMode !== account.effectiveMode && (
          <Typography variant="body2">Configured: {modeWord(account.configuredMode)}</Typography>
        )}
        {account.pending && (
          <Typography variant="body2">
            Changes to {modeWord(account.pending.to)} from {formatIstDateTime(account.pending.effectiveFrom)} IST.
          </Typography>
        )}
        {next ? (
          <section aria-label="Next gate">
            <Typography variant="subtitle2">
              Next gate: {modeWord(next.from)} to {modeWord(next.to)}, {next.verdict}
            </Typography>
            <Stack component="ul" sx={{ gap: 0.5, listStyle: 'none', p: 0, m: 0 }}>
              {next.checks.map((c) => (
                <Stack
                  component="li"
                  key={c.code}
                  direction="row"
                  sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}
                >
                  <CheckStatus outcome={toOutcome(c.outcome)} />
                  <Typography variant="body2">
                    {c.text}
                    {c.n !== null && c.needed !== null ? ` (${c.n} of ${c.needed})` : ''}
                  </Typography>
                </Stack>
              ))}
            </Stack>
          </section>
        ) : (
          <Typography variant="body2">There is no further step in this build.</Typography>
        )}
        <PromotionControls account={account} />
        {account.trail.length > 0 && (
          <section aria-label="Recent changes">
            <Typography variant="subtitle2">Recent changes</Typography>
            <ul style={{ margin: 0, paddingLeft: '1.2em' }}>
              {account.trail.slice(0, 5).map((t) => (
                <li key={t.id}>
                  {formatIstDateTime(t.changedAt)} IST: {modeWord(t.from)} to {modeWord(t.to)} ({t.kind}, {t.actor}):{' '}
                  {t.reason}
                </li>
              ))}
            </ul>
          </section>
        )}
        <section aria-label="Limits">
          <Typography variant="subtitle2" sx={{ mb: 1 }}>
            Limits
          </Typography>
          <LimitsForm accountId={account.accountId} />
        </section>
      </CardContent>
    </Card>
  );
}
