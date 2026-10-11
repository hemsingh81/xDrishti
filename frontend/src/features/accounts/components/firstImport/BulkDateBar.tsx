import { Button, Stack, TextField } from '@mui/material';
import { useState } from 'react';
import { minAcquiredOn, todayIso, validateAcquiredOn } from '@/shared/lib/dates';

interface BulkDateBarProps {
  /** Rows ticked in the table. */
  selectedCount: number;
  rowCount: number;
  initialDate: string;
  onApply: (date: string, to: 'all' | 'selected') => void;
}

/** One date for many rows: "Apply date to all" gives it to every row, "Apply date to selected" to the ticked ones. Every row is imported either way. */
export function BulkDateBar({ selectedCount, rowCount, initialDate, onApply }: BulkDateBarProps) {
  const [date, setDate] = useState(initialDate);
  const error = validateAcquiredOn(date);

  return (
    <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1.5, alignItems: 'flex-start' }}>
      <TextField
        size="small"
        type="date"
        label="Date to apply"
        value={date}
        error={error !== undefined}
        helperText={error}
        onChange={(event) => {
          setDate(event.target.value);
        }}
        slotProps={{ inputLabel: { shrink: true }, htmlInput: { min: minAcquiredOn, max: todayIso() } }}
        sx={{ minWidth: 170 }}
      />
      <Button
        variant="outlined"
        disabled={error !== undefined || rowCount === 0}
        onClick={() => {
          onApply(date, 'all');
        }}
        sx={{ mt: 0.25 }}
      >
        Apply date to all
      </Button>
      <Button
        variant="outlined"
        disabled={error !== undefined || selectedCount === 0}
        onClick={() => {
          onApply(date, 'selected');
        }}
        sx={{ mt: 0.25 }}
      >
        {selectedCount > 0 ? `Apply date to selected (${String(selectedCount)})` : 'Apply date to selected'}
      </Button>
    </Stack>
  );
}
