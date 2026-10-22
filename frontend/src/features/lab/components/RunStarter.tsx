import { Alert, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
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
  const symbols = symbolsText.split(/[\s,;]+/).filter((s) => s !== '');
  const usingSymbols = basketId === '';
  const ready = target !== null && (usingSymbols ? symbols.length > 0 : true);

  const start = useMutation({
    mutationFn: () => {
      if (!target) throw new Error('Open a stored version first.');
      return startRun({
        strategyId: target.id,
        version: target.version,
        method,
        basketId: basketId || null,
        symbols,
      });
    },
    onSuccess: (run) => {
      void queryClient.invalidateQueries({ queryKey: runKeys.all });
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
      {start.error && <Alert severity="error">{start.error.message}</Alert>}
      <Button type="submit" variant="contained" disabled={!ready || start.isPending}>
        Run test
      </Button>
    </Stack>
  );
}
