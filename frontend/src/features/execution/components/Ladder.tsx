import { Chip, Stack } from '@mui/material';
import { rungs } from '../lib/rungs';
import { modeWord } from '../lib/stateWords';

/** The ladder with the account's rung marked by a word, not only by colour. */
export function Ladder({ mode }: { mode: string }) {
  return (
    <Stack
      component="ol"
      aria-label="Mode ladder"
      direction="row"
      sx={{ gap: 0.5, flexWrap: 'wrap', listStyle: 'none', p: 0, m: 0 }}
    >
      {rungs.map((r) => (
        <li key={r}>
          <Chip
            size="small"
            color={r === mode ? 'primary' : 'default'}
            variant={r === mode ? 'filled' : 'outlined'}
            label={r === mode ? `${modeWord(r)} (current)` : modeWord(r)}
          />
        </li>
      ))}
    </Stack>
  );
}
