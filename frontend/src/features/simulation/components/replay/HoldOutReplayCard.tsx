import { Alert, Box, Button, Chip, FormControlLabel, Stack, Switch, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { formatDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { useToast } from '@/shared/ui/toastContext';
import {
  holdOutRegistrationsQuery,
  holdOutReplayKey,
  holdOutReplayProblem,
  holdOutSettingsQuery,
  registerHoldOutReplay,
  runHoldOutReplay,
  setHoldOutSwitch,
  stateWords,
  type HoldOutRegistration,
} from '../../api/holdOutReplayApi';
import { PasswordActDialog } from './PasswordActDialog';

const pct = (value: number | null | undefined) =>
  value === null || value === undefined ? 'unavailable' : `${value.toFixed(1)} %`;

function Rule({ rule }: { rule: HoldOutRegistration['rule'] }) {
  return (
    <Typography variant="body2" sx={{ mb: 1 }}>
      {`The rule, fixed before the block: a pass needs at least ${String(rule.minTrades)} closed trades, net of a checked charge sheet, CAGR above 0, a maximum drawdown of at most ${String(rule.maxDrawdownPct)} %, and the no-selection baseline not doing better. Fewer trades is inconclusive.`}
    </Typography>
  );
}

function Verdict({ registration }: { registration: HoldOutRegistration }) {
  const result = registration.result;
  if (!result) return null;
  const { selection, baseline, comparison } = result.figures;
  return (
    <Box sx={{ mt: 1 }}>
      <Typography variant="subtitle2" component="p">
        {`Verdict: ${result.verdict} (${result.mode})`}
      </Typography>
      <Typography variant="body2">{result.reason}</Typography>
      {selection && (
        <Typography variant="body2">
          {`Selection: ${String(selection.closedTrades)} trades, CAGR ${pct(selection.cagrPct)}, max drawdown ${pct(selection.maxDrawdownPct)}${selection.netBasis ? ', net' : ', not net'}.`}
        </Typography>
      )}
      {baseline && (
        <Typography variant="body2">
          {`Baseline: ${String(baseline.closedTrades)} trades, CAGR ${pct(baseline.cagrPct)}, max drawdown ${pct(baseline.maxDrawdownPct)}.`}
        </Typography>
      )}
      {comparison && (
        <Typography variant="body2">{`Comparison: ${(comparison.answerCode ?? '').replaceAll('_', ' ')}.`}</Typography>
      )}
      {result.figures.engineChanged && <Typography variant="body2">The engine changed since registration.</Typography>}
    </Box>
  );
}

/**
 * The hold-out replay (SR-03): the registration made before a block begins, its frozen hash and rule first, the one run behind the password
 * dialog (the read is irreversible), the verdict with its hindsight label, and the two automatic switches (default on).
 */
export function HoldOutReplayCard() {
  const toast = useToast();
  const queryClient = useQueryClient();
  const registrations = useQuery(holdOutRegistrationsQuery);
  const settings = useQuery(holdOutSettingsQuery);
  const [sheet, setSheet] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const [act, setAct] = useState<{
    title: string;
    warning: string;
    confirm: string;
    run: (password: string) => Promise<unknown>;
  } | null>(null);
  const refresh = () => queryClient.invalidateQueries({ queryKey: holdOutReplayKey });
  const register = useMutation({
    mutationFn: () => registerHoldOutReplay(sheet.trim()),
    onSuccess: () => {
      toast.notify('Registered for the next block. The hash is fixed.', 'success');
      void refresh();
    },
    onError: (error) => {
      setProblem(holdOutReplayProblem(error));
    },
  });
  if (registrations.isPending || settings.isPending)
    return <LoadingState announce={false} label="Reading the hold-out replay" />;
  if (registrations.isError) return <ErrorState error={registrations.error} />;
  const current = registrations.data.find((r) => r.state !== 'superseded');
  const switches: ['autoRegister' | 'autoRun', string, boolean | null | undefined][] = [
    ['autoRegister', 'Register automatically on the last evening before each block', settings.data?.autoRegister],
    ['autoRun', 'Run automatically when the block seals', settings.data?.autoRun],
  ];
  return (
    <Box component="section" aria-label="Hold-out replay">
      <Typography variant="h2" component="h2">
        Hold-out replay
      </Typography>
      <Alert severity="info" role="note" aria-label="Hindsight label" sx={{ my: 1 }}>
        Unseen by the system; the owner may have seen these prices elsewhere. Research only: no readiness figure reads
        it.
      </Alert>
      {current ? (
        <Box>
          <Rule rule={current.rule} />
          <Typography variant="body2">
            {`Block from ${formatDate(current.blockStart)} to ${formatDate(current.blockEnd)}, registered ${formatDate(current.createdAt)} (${current.mode}).`}
          </Typography>
          <Typography variant="body2" sx={{ wordBreak: 'break-all' }}>{`Frozen hash ${current.configHash}`}</Typography>
          <Chip size="small" label={stateWords[current.state] ?? current.state} sx={{ my: 1 }} />
          {current.driftReason && <Alert severity="warning">{`A stored input changed: ${current.driftReason}.`}</Alert>}
          <Verdict registration={current} />
          {current.state === 'waiting' && (
            <Box sx={{ mt: 1 }}>
              <Button
                variant="contained"
                onClick={() => {
                  setAct({
                    title: 'Run the hold-out replay',
                    warning:
                      'This reads the block once and cannot be undone. The look is used up even if the run is abandoned, and a second run is refused.',
                    confirm: 'Run once',
                    run: (password) => runHoldOutReplay(current.id, password),
                  });
                }}
              >
                Run once (irreversible)
              </Button>
            </Box>
          )}
        </Box>
      ) : (
        <Typography variant="body2">
          No registration yet. The automatic one is stored the evening before the next block begins.
        </Typography>
      )}
      <Stack
        component="form"
        direction="row"
        spacing={1}
        sx={{ my: 2, alignItems: 'center' }}
        onSubmit={(event) => {
          event.preventDefault();
          setProblem(null);
          register.mutate();
        }}
      >
        <TextField
          size="small"
          label="Checked charge sheet id"
          value={sheet}
          onChange={(event) => {
            setSheet(event.target.value);
          }}
        />
        <Button type="submit" disabled={sheet.trim() === '' || register.isPending}>
          Register for the next block
        </Button>
      </Stack>
      {problem !== null && <Alert severity="error">{problem}</Alert>}
      <Stack>
        {switches.map(([name, label, value]) => (
          <FormControlLabel
            key={name}
            label={value === null || value === undefined ? `${label} (unavailable)` : label}
            control={
              <Switch
                checked={value === true}
                disabled={value === null || value === undefined}
                onChange={(event) => {
                  const enabled = event.target.checked;
                  setAct({
                    title: enabled ? 'Turn the automatic step on' : 'Turn the automatic step off',
                    warning: 'It never places an order. The change is recorded with you as the actor.',
                    confirm: enabled ? 'Turn on' : 'Turn off',
                    run: (password) => setHoldOutSwitch(name, enabled, password),
                  });
                }}
              />
            }
          />
        ))}
      </Stack>
      {act && (
        <PasswordActDialog
          title={act.title}
          warning={act.warning}
          confirmLabel={act.confirm}
          act={act.run}
          onDone={() => {
            setAct(null);
            void refresh();
          }}
          onClose={() => {
            setAct(null);
          }}
        />
      )}
    </Box>
  );
}
