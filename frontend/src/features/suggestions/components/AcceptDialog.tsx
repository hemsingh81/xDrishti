import { Alert, Button, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { ApiError } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useAcceptProposal, type Proposal } from '../api/proposalApi';
import { words } from '../lib/wording';

function problemText(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  switch (error.status) {
    case 401:
      return 'The password was not accepted. Nothing was changed.';
    case 423:
      return 'Too many wrong passwords; this is locked for now.';
    default:
      switch (error.problem?.code) {
        case 'assistant.capability_off':
          return words.switchOff;
        case 'assistant.proposal_stale':
          return `${words.stale} ${error.problem.title ?? ''}`.trim();
        case 'assistant.proposal_closed':
          return words.closed;
        default:
          return `${error.problem?.title ?? 'The request failed.'} Nothing was changed.`;
      }
  }
}

/** The owner's accept: says exactly what will happen and asks the password again for this accept. */
export function AcceptDialog({ proposal, onClose }: { proposal: Proposal; onClose: () => void }) {
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const accept = useAcceptProposal();
  const ready = password.length > 0 && !accept.isPending;

  const submit = () => {
    setProblem(null);
    accept.mutate(
      { id: proposal.id, password },
      {
        onSuccess: onClose,
        onError: (error) => {
          setProblem(problemText(error));
        },
        // Cleared after every try, right or wrong; never kept (the mutation state would hold the variables, password included).
        onSettled: () => {
          setPassword('');
          accept.reset();
        },
      },
    );
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={accept.isPending}
      onSubmit={(event) => {
        event.preventDefault();
        if (ready) submit();
      }}
    >
      <AppDialogHeader>Accept this suggestion</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <Typography>{proposal.effect}</Typography>
          <Typography variant="body2" color="text.secondary">
            The checks run again on today&apos;s data before anything is written.
          </Typography>
          <TextField
            label="Your password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => {
              setPassword(e.target.value);
            }}
            required
            helperText="Asked again for every accept."
          />
          {problem !== null && <Alert severity="error">{problem}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={accept.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready}>
          Accept
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
