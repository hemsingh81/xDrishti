import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Chip } from '@mui/material';
import type { DataCoverageRow } from '@/shared/api';
import { rowStatusText } from '../lib/labels';

/** Said in words and with an icon: "Current", "Behind 2 d" or "Empty" (tracked, no bars yet). */
export function RowStatus({ row }: { row: DataCoverageRow }) {
  const text = rowStatusText(row);
  if (row.status === 'behind') {
    return <Chip size="small" variant="outlined" color="warning" icon={<WarningIcon />} label={text} />;
  }
  if (row.status === 'current') {
    return <Chip size="small" variant="outlined" color="success" icon={<CheckCircleIcon />} label={text} />;
  }
  return <Chip size="small" variant="outlined" label={text} />;
}
