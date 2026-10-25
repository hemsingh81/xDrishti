import { Stack, TextField } from '@mui/material';
import type { DateRange } from '../api/learningQueries';

interface RangeFilterProps {
  range: DateRange;
  onChange: (range: DateRange) => void;
}

/** The planned-session range (`from`, `to` in the address). An empty field is an open end; the server refuses a reversed range. */
export function RangeFilter({ range, onChange }: RangeFilterProps) {
  return (
    <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap' }} role="group" aria-label="Date range">
      {(['from', 'to'] as const).map((end) => (
        <TextField
          key={end}
          type="date"
          size="small"
          label={end === 'from' ? 'From' : 'To'}
          value={range[end] ?? ''}
          onChange={(event) => {
            onChange({ ...range, [end]: event.target.value === '' ? null : event.target.value });
          }}
          slotProps={{ inputLabel: { shrink: true } }}
        />
      ))}
    </Stack>
  );
}
