import { Box, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { CalendarDay } from '@/shared/api';
import type { GridColumn } from '@/shared/ui/grid';
import { calendarSourceLabel, dayName, replacesText, sessionText } from '../lib/calendar';
import { KindChip } from './KindChip';

/**
 * The columns of both lists (upcoming days and proposals): date with weekday, kind, session window, where the day came
 * from and the note; then the actions of the list. The window, source and note are hidden on narrow screens.
 */
export function calendarColumns(actions: (day: CalendarDay) => ReactNode): readonly GridColumn<CalendarDay>[] {
  return [
    {
      id: 'date',
      header: 'Date',
      minWidth: 140,
      render: (day) => {
        const replaces = replacesText(day);
        return (
          <Box sx={{ minWidth: 0 }}>
            <time dateTime={day.date}>{dayName(day)}</time>
            {replaces && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', whiteSpace: 'normal' }}>
                {replaces}
              </Typography>
            )}
          </Box>
        );
      },
    },
    { id: 'kind', header: 'Kind', minWidth: 130, render: (day) => <KindChip day={day} /> },
    { id: 'session', header: 'Session (IST)', hideBelow: 'sm', accessor: (day) => sessionText(day) },
    { id: 'source', header: 'Source', hideBelow: 'md', accessor: (day) => calendarSourceLabel(day.source) },
    { id: 'note', header: 'Note', grow: true, minWidth: 140, hideBelow: 'md', accessor: (day) => day.note ?? '—' },
    { id: 'actions', header: 'Actions', kind: 'handle', align: 'right', minWidth: 130, render: actions },
  ];
}
