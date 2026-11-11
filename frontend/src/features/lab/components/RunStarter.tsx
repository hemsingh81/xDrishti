import { Alert, Button, Checkbox, FormControlLabel, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { useToast } from '@/shared/ui/toastContext';
import { runKeys, startRun, universeBasketsQuery, type LabRun, type RunMethod } from '../api/runQueries';

interface RunStarterProps {
  /** The stored version to test; null when none is open (a draft cannot be tested before it is saved). */
  target: { id: string; version: number } | null;
  onStarted: (run: LabRun) => void;
}

const methods: { value: RunMethod; label: string; hint: string }[] = [
  { value: 'quick', label: 'Quick test', hint: 'Last year, up to 30 symbols. Exploration, not a verdict.' },
  { value: 'full', label: 'Full test', hint: 'Walk-forward over timeframe, side and exit; only out-of-sample counts.' },
  {
    value: 'robust',
    label: 'Robust test',
    hint: 'Full test plus sensitivity, Monte Carlo, by year and a chance baseline.',
  },
];

/** Starts a run of the open stored version on a basket or a list of symbols. */
export function RunStarter({ target, onStarted }: RunStarterProps) {
  const toast = useToast();
  const queryClient = useQueryClient();
  const baskets = useQuery(universeBasketsQuery);
  const [method, setMethod] = useState<RunMethod>('quick');
  const [basketId, setBasketId] = useState('');
  const [symbolsText, setSymbolsText] = useState('');
  const [membersBasis, setMembersBasis] = useState<'single_snapshot' | 'dated'>('single_snapshot');
  const [conservative, setConservative] = useState(false);
  const [stress, setStress] = useState(false);
  const [skipNulls, setSkipNulls] = useState(false);
  const [seedsText, setSeedsText] = useState('');
  const robust = method === 'robust';
  const full = method === 'full';
  const seeds = robust && !skipNulls && seedsText.trim() !== '' ? Number(seedsText) : undefined;
  const seedsBad = seeds !== undefined && (!Number.isInteger(seeds) || seeds < 200);
  const symbols = symbolsText.split(/[\s,;]+/).filter((s) => s !== '');
  const usingSymbols = basketId === '';
  const ready = target !== null && !seedsBad && (usingSymbols ? symbols.length > 0 : true);

  const start = useMutation({
    mutationFn: () => {
      if (!target) throw new Error('Open a stored version first.');
      return startRun({
        strategyId: target.id,
        version: target.version,
        method,
        basketId: basketId || null,
        symbols,
        // the default is not sent: a post without a basis is the members at the time of the post, as before
        ...(!usingSymbols && membersBasis === 'dated' ? { membersBasis: 'dated' as const } : {}),
        ...(conservative ? { slippage: 'conservative' as const } : {}),
        // stress is already on for robust and refused for quick; only a full test takes it when asked
        ...(full && stress ? { stress: true } : {}),
        ...(robust && skipNulls ? { nulls: false as const } : {}),
        ...(seeds !== undefined ? { draws: seeds } : {}),
      });
    },
    onSuccess: (run) => {
      void queryClient.invalidateQueries({ queryKey: runKeys.all });
      void queryClient.invalidateQueries({ queryKey: ['lab', 'ledger'] });
      toast.notify(`Run ${run.state}: ${run.strategy.id} v${run.strategy.version}, ${run.method}.`, 'success');
      onStarted(run);
    },
    onError: (e) => {
      toast.notify(e.message, 'error');
    },
  });

  const hint = methods.find((m) => m.value === method)?.hint;
  return (
    <Stack
      component="form"
      spacing={1.5}
      onSubmit={(e) => {
        e.preventDefault();
        if (ready) start.mutate();
      }}
    >
      <Typography color="text.secondary">
        {target ? `Tests ${target.id} version ${target.version}.` : 'Open a stored version in the library to test it.'}
      </Typography>
      <TextField
        select
        label="Method"
        value={method}
        onChange={(e) => {
          setMethod(e.target.value as RunMethod);
        }}
        helperText={hint}
      >
        {methods.map((m) => (
          <MenuItem key={m.value} value={m.value}>
            {m.label}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        select
        label="Universe"
        value={basketId}
        onChange={(e) => {
          setBasketId(e.target.value);
        }}
        helperText={baskets.error ? 'Baskets could not be read; type symbols instead.' : undefined}
        error={Boolean(baskets.error)}
      >
        <MenuItem value="">Symbols typed below</MenuItem>
        {baskets.data?.map((b) => (
          <MenuItem key={b.id} value={b.id}>
            {b.name} ({b.memberCount})
          </MenuItem>
        ))}
      </TextField>
      {!usingSymbols && (
        <TextField
          select
          label="Members"
          value={method === 'quick' ? 'single_snapshot' : membersBasis}
          disabled={method === 'quick'}
          onChange={(e) => {
            setMembersBasis(e.target.value as 'single_snapshot' | 'dated');
          }}
          helperText={
            method === 'quick'
              ? 'The quick test samples the members of one day; the dated basis needs a full or robust test.'
              : membersBasis === 'dated'
                ? "Each day uses that day's members (point in time). Refused when the basket has no dated membership for the window start."
                : 'The members at the time of the post; survivorship applies.'
          }
        >
          <MenuItem value="single_snapshot">As of the run date (single snapshot)</MenuItem>
          <MenuItem value="dated">Dated (point in time)</MenuItem>
        </TextField>
      )}
      {usingSymbols && (
        <TextField
          label="Symbols"
          value={symbolsText}
          onChange={(e) => {
            setSymbolsText(e.target.value);
          }}
          placeholder="RELIANCE, TCS, INFY"
          multiline
          minRows={2}
        />
      )}
      <FormControlLabel
        control={
          <Checkbox
            checked={conservative}
            onChange={(e) => {
              setConservative(e.target.checked);
            }}
          />
        }
        label="Conservative slippage (what-if)"
      />
      {full && (
        <FormControlLabel
          control={
            <Checkbox
              checked={stress}
              onChange={(e) => {
                setStress(e.target.checked);
              }}
            />
          }
          label="Cost stress block (what-if)"
        />
      )}
      {robust && (
        <>
          <FormControlLabel
            control={
              <Checkbox
                checked={skipNulls}
                onChange={(e) => {
                  setSkipNulls(e.target.checked);
                }}
              />
            }
            label="Skip chance block (what-if)"
          />
          <TextField
            label="Seeds"
            type="number"
            value={seedsText}
            disabled={skipNulls}
            onChange={(e) => {
              setSeedsText(e.target.value);
            }}
            error={seedsBad}
            helperText={
              seedsBad
                ? 'At least 200, a whole number.'
                : 'Random-entry seeds (what-if): 200 up to the server limit; empty uses the configured number.'
            }
            slotProps={{ htmlInput: { min: 200, step: 1 } }}
          />
        </>
      )}
      {start.error && <Alert severity="error">{start.error.message}</Alert>}
      <Button type="submit" variant="contained" disabled={!ready || start.isPending}>
        Run test
      </Button>
    </Stack>
  );
}
