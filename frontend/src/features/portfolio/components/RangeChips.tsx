import { ToggleButton, ToggleButtonGroup } from '@mui/material';
import { historyRanges, type HistoryRange } from '../lib/historyRange';

/** 1M · 3M · 6M · 1Y · All: how far back the history goes. */
export function RangeChips({ value, onChange }: { value: HistoryRange; onChange: (range: HistoryRange) => void }) {
  return (
    <ToggleButtonGroup
      exclusive
      size="small"
      aria-label="History range"
      value={value}
      onChange={(_event, next: HistoryRange | null) => {
        if (next) onChange(next);
      }}
    >
      {historyRanges.map((range) => (
        <ToggleButton key={range} value={range} sx={{ px: 1.5 }}>
          {range}
        </ToggleButton>
      ))}
    </ToggleButtonGroup>
  );
}
