import { Alert, Button, DialogContentText, Stack, TextField } from '@mui/material';
import { useRef, useState, type SyntheticEvent } from 'react';
import { ApiError } from '@/shared/api';
import { AppDialog, AppDialogBody, AppDialogFooter, AppDialogHeader } from '@/shared/ui/dialog';
import { useReplaceEvidenceSet } from '../api/shadowQueries';

const maxReason = 500;

/** The words of a refused replace; every one says nothing changed. */
function replaceProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was changed.';
  const p = error.problem;
  if (error.status === 401) return 'The password was not accepted. Nothing was changed.';
  if (error.status === 423) return `Too many wrong passwords; sign-in is locked for now. ${p?.detail ?? ''}`.trim();
  if (error.status === 429) return 'Too many attempts. Wait a little and try again. Nothing was changed.';
  if (p?.code === 'paper.evidence_set_frozen')
    return 'A shadow row of this epoch is already stored, so the set is frozen and cannot be replaced. Nothing was changed.';
  const text = [p?.title, p?.detail].filter(Boolean).join(' ') || 'The request failed.';
  return `${text}${p?.code ? ` (${p.code})` : ''} Nothing was changed.`;
}

/** Replace the evidence set before the epoch's first shadow write: version hashes, a reason and a fresh password (cleared after every try). */
export function EvidenceSetDialog({ initial, onClose }: { initial: readonly string[]; onClose: () => void }) {
  const replace = useReplaceEvidenceSet();
  const [versions, setVersions] = useState(initial.join('\n'));
  const [reason, setReason] = useState('');
  const [password, setPassword] = useState('');
  const [problem, setProblem] = useState<string | null>(null);
  const passwordRef = useRef<HTMLInputElement>(null);
  const list = versions
    .split(/[\s,]+/)
    .map((v) => v.trim())
    .filter(Boolean);
  const ready =
    list.length > 0 &&
    password.length > 0 &&
    reason.trim().length > 0 &&
    reason.length <= maxReason &&
    !replace.isPending;

  const submit = async (event: SyntheticEvent) => {
    event.preventDefault();
    if (!ready) return;
    setProblem(null);
    try {
      await replace.mutateAsync({ versions: list, reason: reason.trim(), password });
      onClose();
    } catch (error) {
      setProblem(replaceProblem(error));
      passwordRef.current?.focus();
    } finally {
      setPassword('');
    }
  };

  return (
    <AppDialog
      open
      onClose={onClose}
      busy={replace.isPending}
      maxWidth="sm"
      initialFocusRef={passwordRef}
      onSubmit={(e) => void submit(e)}
    >
      <AppDialogHeader>Replace the evidence set</AppDialogHeader>
      <AppDialogBody>
        <Stack spacing={2}>
          <DialogContentText component="div">
            The set is the strategy versions whose shadow rows count for the edge figures. You may replace it until the
            first shadow write of this epoch; after that it is frozen for good. Rows of other versions are still written
            and graded, labelled outside the set. No order is placed.
          </DialogContentText>
          <TextField
            label="Strategy version hashes"
            value={versions}
            onChange={(event) => {
              setVersions(event.target.value);
            }}
            required
            multiline
            minRows={3}
            helperText="64-character hashes of stored versions, one per line."
          />
          <TextField
            label="Reason"
            value={reason}
            onChange={(event) => {
              setReason(event.target.value);
            }}
            required
            multiline
            slotProps={{ htmlInput: { maxLength: maxReason } }}
            helperText="Why, up to 500 characters. It is stored with the change."
          />
          <TextField
            label="Your password for this change"
            type="password"
            autoComplete="current-password"
            value={password}
            inputRef={passwordRef}
            onChange={(event) => {
              setPassword(event.target.value);
            }}
            required
            helperText="Asked again every time. A wrong one counts toward the sign-in lock."
          />
          {problem && <Alert severity="error">{problem}</Alert>}
        </Stack>
      </AppDialogBody>
      <AppDialogFooter>
        <Button onClick={onClose} disabled={replace.isPending}>
          Cancel
        </Button>
        <Button type="submit" variant="contained" disabled={!ready} loading={replace.isPending}>
          Replace
        </Button>
      </AppDialogFooter>
    </AppDialog>
  );
}
