import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import EditNoteIcon from '@mui/icons-material/EditNoteOutlined';
import ScheduleIcon from '@mui/icons-material/ScheduleOutlined';
import { Chip } from '@mui/material';
import type { ReactElement } from 'react';
import { csvStateLabels } from '../lib/csvReview';

const looks: Readonly<Record<string, { color: 'success' | 'warning' | 'default'; icon: ReactElement }>> = {
  pending: { color: 'default', icon: <ScheduleIcon /> },
  loaded: { color: 'success', icon: <CheckCircleIcon /> },
  changed: { color: 'warning', icon: <EditNoteIcon /> },
};

const labels: Readonly<Record<string, string>> = csvStateLabels;

/** The state of a file as a chip with an icon and a word. `changed`: loaded, but the file's size or date differ now. */
export function CsvStateChip({ state }: { state: string }) {
  const look = looks[state];
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look?.color ?? 'default'}
      icon={look?.icon}
      label={labels[state] ?? state}
    />
  );
}
