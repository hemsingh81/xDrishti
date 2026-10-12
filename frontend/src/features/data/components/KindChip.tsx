import { Chip } from '@mui/material';
import type { CalendarDay } from '@/shared/api';
import { isSpecial, kindLabel } from '../lib/calendar';

/** Said in words, as a chip: "Holiday" or "Special session". */
export function KindChip({ day }: { day: CalendarDay }) {
  return <Chip size="small" variant="outlined" color={isSpecial(day) ? 'info' : 'default'} label={kindLabel(day)} />;
}
