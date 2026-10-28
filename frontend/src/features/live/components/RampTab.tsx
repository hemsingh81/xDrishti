import { Alert, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { lazy, Suspense, useState } from 'react';
import { accountsQuery } from '@/features/accounts';
import { ErrorState, LoadingState } from '@/shared/ui';
import { liveSettingsQuery } from '../api/rampQueries';
import { gatesSentence, noOrderSentence } from '../lib/rampLabels';
import { RampAccount, type RampAct } from './RampAccount';
import { RampSwitches } from './RampSwitches';

// The dialogs hold the writes of this tab, so they load only when the owner opens one.
const RampDialog = lazy(() => import('./RampDialog').then((m) => ({ default: m.RampDialog })));
const RampActDialog = lazy(() => import('./RampActDialog').then((m) => ({ default: m.RampActDialog })));
const LiveSettingsDialog = lazy(() => import('./LiveSettingsDialog').then((m) => ({ default: m.LiveSettingsDialog })));

type Open = RampAct | { kind: 'switches' } | null;

/**
 * The Ramp tab of the Live screen (spec P11-04): the step of a live account, the risk it gives, the evidence gates, today's loss, the halts and the history, and the two live switches (both OFF). It reads
 * only; the owner's moves are made in the dialogs, with the password typed again. Nothing here calls a broker.
 */
export function RampTab() {
  const accounts = useQuery(accountsQuery);
  const settings = useQuery(liveSettingsQuery);
  const [picked, setPicked] = useState<string | null>(null);
  const [open, setOpen] = useState<Open>(null);
  if (accounts.isPending || settings.isPending) return <LoadingState label="Loading the ramp…" />;
  if (accounts.isError) return <ErrorState error={accounts.error} />;
  if (settings.isError) return <ErrorState error={settings.error} />;
  const trading = accounts.data.filter((a) => a.roles.trading.enabled);
  const account =
    trading.find((a) => a.id === picked) ?? trading.find((a) => a.roles.trading.mode === 'manual_live') ?? trading[0];
  const close = () => {
    setOpen(null);
  };
  return (
    <Stack sx={{ gap: 2 }}>
      <Typography>
        A live account trades at a fraction of its profile&apos;s risk. You raise the fraction on evidence; the system
        lowers it by itself when the evidence turns.
      </Typography>
      <Alert severity="info">{noOrderSentence}</Alert>
      {account ? (
        <>
          <TextField
            select
            label="Account"
            value={account.id}
            onChange={(e) => {
              setPicked(e.target.value);
            }}
            sx={{ maxWidth: 360 }}
          >
            {trading.map((a) => (
              <MenuItem key={a.id} value={a.id}>
                {a.name}
              </MenuItem>
            ))}
          </TextField>
          <RampAccount accountId={account.id} onAct={setOpen} />
        </>
      ) : (
        <Typography>No account has the Trading role on, so there is no ramp to show.</Typography>
      )}
      <Typography variant="body2" color="text.secondary">
        {gatesSentence}
      </Typography>
      <RampSwitches
        settings={settings.data}
        onChange={() => {
          setOpen({ kind: 'switches' });
        }}
      />
      <Suspense fallback={null}>
        {open?.kind === 'step' && account && (
          <RampDialog
            accountId={account.id}
            accountName={account.name}
            step={open.step}
            raiseTo={open.raise && open.step !== null && open.step < 3 ? open.step + 1 : null}
            onClose={close}
          />
        )}
        {(open?.kind === 'clear-hold' || open?.kind === 'exit-confirmed') && account && (
          <RampActDialog accountId={account.id} accountName={account.name} act={open.kind} onClose={close} />
        )}
        {open?.kind === 'switches' && <LiveSettingsDialog current={settings.data} onClose={close} />}
      </Suspense>
    </Stack>
  );
}
