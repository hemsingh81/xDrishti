import { Box } from '@mui/material';
import type { ReactNode } from 'react';
import type { CalendarDay } from '@/shared/api';
import { Grid } from '@/shared/ui/grid';
import { calendarColumns } from './calendarColumns';
import { ProposalActions } from './ProposalActions';
import { RemoveDayButton } from './RemoveDayButton';

const getRowId = (day: CalendarDay) => day.date;
const upcomingColumns = calendarColumns((day) => <RemoveDayButton day={day} />);
const proposalColumns = calendarColumns((day) => <ProposalActions day={day} />);

interface CalendarListProps {
  label: string;
  days: readonly CalendarDay[];
  /** Proposals get Confirm / Dismiss; confirmed days get Remove / Dismiss. */
  proposed?: boolean;
  empty?: ReactNode;
}

/** One list of the Calendar tab, in a bordered frame; the tab scrolls as a whole. */
export function CalendarList({ label, days, proposed = false, empty }: CalendarListProps) {
  return (
    <Box
      sx={{
        display: 'flex',
        flexDirection: 'column',
        border: 1,
        borderColor: 'divider',
        borderRadius: 1,
        overflow: 'hidden',
      }}
    >
      <Grid
        label={label}
        columns={proposed ? proposalColumns : upcomingColumns}
        rows={days}
        getRowId={getRowId}
        emptyState={empty}
      />
    </Box>
  );
}
