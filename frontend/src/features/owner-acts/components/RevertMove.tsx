import { Typography } from '@mui/material';
import { useRevertAct, type ChangelogRecord } from '../api/movesApi';
import { ActForm } from './ActForm';

/** Reverting one changelog row: what it will change, and the form. The server decides whether the row can be reverted and lists every reason when not. */
export function RevertMove({ entry }: { entry: ChangelogRecord }) {
  const act = useRevertAct();
  if (entry.kind !== 'transition') {
    return (
      <Typography>
        Row {entry.id} is a {entry.kind}. Only a cell transition can be reverted; a revert is not reverted (record the
        move again) and a designation is changed by designating again.
      </Typography>
    );
  }
  return (
    <>
      <Typography sx={{ overflowWrap: 'anywhere' }}>
        Row {entry.id}: the cell {entry.subject} moved from <strong>{entry.fromState}</strong> to{' '}
        <strong>{entry.toState}</strong>. Reverting adds a new row that puts it back to{' '}
        <strong>{entry.fromState}</strong>. Plans that are already published are not changed.
      </Typography>
      <ActForm
        label="Revert this move"
        consequence="Nothing is applied. A revert is a new row and only restores a less trusted state: it never makes a cell active and never brings back a retired one. It works on the latest row of the cell only."
        submitLabel="Revert this move"
        run={(reason, password) => act({ id: entry.id, reason, password })}
        done={(outcome) => `Reverted: the cell ${outcome.subject} is back to ${outcome.to} (new row ${outcome.id}).`}
      />
    </>
  );
}
