import { Alert, Button, MenuItem, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiError } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { basketsForDraftQuery, useConfirmDraft, type Draft } from '../api/draftApi';
import { draftWords } from '../lib/draftWording';

function problemText(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was stored.';
  switch (error.status) {
    case 401:
      return 'The password was not accepted. Nothing was stored.';
    case 423:
      return 'Too many wrong passwords; this is locked for now.';
    default:
      return error.problem?.code === 'assistant.capability_off'
        ? draftWords.switchOff
        : `${error.problem?.title ?? 'The request failed.'} Nothing was stored.`;
  }
}

/** The owner's confirm: the universe of the quick test is picked here (never taken from the idea) and the password is asked again for this confirm. */
export function ConfirmDraftDialog({
  draft,
  text,
  onClose,
}: {
  draft: Draft;
  text: string | null;
  onClose: () => void;
}) {
  const baskets = useQuery(basketsForDraftQuery);
  const [run, setRun] = useState(true);
  const [basketId, setBasketId] = useState('');
  const [symbols, setSymbols] = useState('');
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const confirm = useConfirmDraft();
  const list = symbols
    .split(/[\s,]+/)
    .map((s) => s.trim().toUpperCase())
    .filter((s) => s.length > 0);
  const universeOk = !run || (basketId !== '') !== list.length > 0;
  const ready = password.length > 0 && universeOk && !confirm.isPending;

  const submit = () => {
    setProblem(null);
    confirm.mutate(
      {
        id: draft.id,
        text,
        basketId: run && basketId ? basketId : null,
        symbols: run && list.length > 0 ? list : null,
        run,
        password,
      },
      {
        onSuccess: onClose,
        onError: (error) => {
          setProblem(problemText(error));
        },
        // Cleared after every try, right or wrong; never kept (the mutation state would hold the variables, password included).
        onSettled: () => {
          setPassword('');
          confirm.reset();
        },
      },
    );
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={confirm.isPending}
      onSubmit={(event) => {
        event.preventDefault();
        if (ready) submit();
      }}
    >
      <AppDialogHeader>Confirm the draft</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography variant="body2">
            This stores the text as a new library version{run ? ' and starts a quick test' : ''}.{' '}
            {run ? draftWords.quickTest : ''}
          </Typography>
          <TextField
            select
            label="Quick test"
            value={run ? 'yes' : 'no'}
            onChange={(e) => {
              setRun(e.target.value === 'yes');
            }}
          >
            <MenuItem value="yes">Store and run a quick test</MenuItem>
            <MenuItem value="no">Store only</MenuItem>
          </TextField>
          {run && (
            <>
              <TextField
                select
                label="Basket"
                value={basketId}
                onChange={(e) => {
                  setBasketId(e.target.value);
                }}
                helperText="Pick a basket, or give symbols below, not both."
              >
                <MenuItem value="">None</MenuItem>
                {(baskets.data ?? [])
                  .filter((b) => b.archivedAt === null)
                  .map((b) => (
                    <MenuItem key={b.id} value={b.id}>
                      {b.name}
                    </MenuItem>
                  ))}
              </TextField>
              <TextField
                label="Symbols"
                value={symbols}
                onChange={(e) => {
                  setSymbols(e.target.value);
                }}
                helperText="Comma-separated, for example RELIANCE, TCS"
              />
            </>
          )}
          <TextField
            label="Your password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => {
              setPassword(e.target.value);
            }}
            required
            helperText="Asked again for every confirm."
          />
          {problem !== null && <Alert severity="error">{problem}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={confirm.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready}>
          Confirm
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
