import PlaylistAddIcon from '@mui/icons-material/PlaylistAddOutlined';
import { Box, Button, Typography } from '@mui/material';
import { formatDate } from '@/shared/lib/format';

interface EmptyMembersProps {
  /** The look-back date, if one is shown. */
  asOf: string | null;
  /** A portfolio basket: its members are the account's holdings, nothing to add. */
  readOnly?: boolean;
  onAdd: () => void;
}

/** Nothing in the list: an empty basket invites adding; a look-back date says there was nothing then. */
export function EmptyMembers({ asOf, readOnly = false, onAdd }: EmptyMembersProps) {
  if (readOnly) {
    return (
      <Box sx={{ p: 4, textAlign: 'center' }}>
        <Typography color="text.secondary">
          No holdings in this bucket yet — they arrive when the account&apos;s portfolio is imported
        </Typography>
      </Box>
    );
  }
  if (asOf) {
    return (
      <Box sx={{ p: 4, textAlign: 'center' }}>
        <Typography color="text.secondary">{`This basket had no instruments at the end of ${formatDate(asOf)}.`}</Typography>
      </Box>
    );
  }
  return (
    <Box sx={{ p: 4, textAlign: 'center' }}>
      <Typography variant="h3" component="p" sx={{ mb: 0.5 }}>
        This basket is empty — add instruments
      </Typography>
      <Typography color="text.secondary" sx={{ mb: 2 }}>
        Search the master list or your tracked instruments. Instruments that are not tracked yet are tracked for you.
      </Typography>
      <Button variant="contained" startIcon={<PlaylistAddIcon />} onClick={onAdd}>
        Add instruments
      </Button>
    </Box>
  );
}
