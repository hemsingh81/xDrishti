import { MenuItem, Stack, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { runsQuery, type RunItem } from '../api/experimentApi';

const label = (r: RunItem): string =>
  `${r.strategyId}, ${r.universeLabel}, ${r.windowStart} to ${r.windowEnd}, ${r.costed ? 'costed' : 'gross'}${
    /experiment candidate/i.test(r.defaultSetNote ?? '') ? ', experiment candidate' : ''
  }`;

/** The baseline: a stored run from the list, or its 64-character key typed or pasted. */
export function RunPicker({ value, onChange }: { value: string; onChange: (key: string) => void }) {
  const query = useQuery(runsQuery());
  const runs = query.data?.runs ?? [];
  return (
    <Stack spacing={2}>
      {query.isPending && <LoadingState label="Loading the stored runs…" />}
      {query.isError && <ErrorState error={query.error} />}
      {runs.length > 0 && (
        <TextField
          select
          label="Stored run"
          value={runs.some((r) => r.runKey === value) ? value : ''}
          onChange={(event) => {
            onChange(event.target.value);
          }}
        >
          {runs.map((r) => (
            <MenuItem key={r.runKey} value={r.runKey}>
              {label(r)}
            </MenuItem>
          ))}
        </TextField>
      )}
      <TextField
        label="Baseline run key"
        value={value}
        onChange={(event) => {
          onChange(event.target.value.trim().toLowerCase());
        }}
        helperText="64 hexadecimal characters; picked above or pasted."
        slotProps={{ htmlInput: { spellCheck: false } }}
      />
    </Stack>
  );
}
