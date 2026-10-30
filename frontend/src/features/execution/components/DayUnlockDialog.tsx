import { Alert, Button, Dialog, DialogActions, DialogContent, DialogTitle, TextField } from '@mui/material';
import { useState, type SyntheticEvent } from 'react';
import { problemText, useDayUnlock } from '../api/confirmApi';

interface Props {
  accountId: string;
  accountName: string;
  open: boolean;
  onClose: () => void;
}

/** Unlocks one account for today with a fresh password. Not part of the confirm card. */
export function DayUnlockDialog({ accountId, accountName, open, onClose }: Props) {
  const unlock = useDayUnlock();
  const [password, setPassword] = useState('');

  const submit = (e: SyntheticEvent) => {
    e.preventDefault();
    if (password === '' || unlock.isPending) return;
    unlock.mutate(
      { accountId, password },
      {
        onSuccess: () => {
          setPassword('');
          onClose();
        },
      },
    );
  };

  return (
    <Dialog open={open} onClose={onClose} aria-labelledby="unlock-title">
      <form onSubmit={submit}>
        <DialogTitle id="unlock-title">Unlock today for {accountName}</DialogTitle>
        <DialogContent>
          <TextField
            label="Password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => {
              setPassword(e.target.value);
            }}
            margin="dense"
            fullWidth
            required
          />
          {unlock.isError && <Alert severity="error">{problemText(unlock.error)}</Alert>}
        </DialogContent>
        <DialogActions>
          <Button onClick={onClose}>Cancel</Button>
          <Button type="submit" variant="contained" disabled={unlock.isPending || password === ''}>
            Unlock today
          </Button>
        </DialogActions>
      </form>
    </Dialog>
  );
}
