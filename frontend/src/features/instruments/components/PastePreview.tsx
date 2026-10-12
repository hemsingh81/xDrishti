import {
  Alert,
  Box,
  Chip,
  List,
  ListItem,
  ListItemText,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import type { ResolvedInstrument, ResolveResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { monoFontFamily } from '@/theme';
import { typeLabel } from '../lib/labels';
import { newlyTrackable, unmatchedReason } from '../lib/trackingText';

/** Longest list shown in full; a pasted list can have thousands of entries. */
const shownRows = 200;

function statusOf(m: ResolvedInstrument) {
  if (m.tracked) return <Chip size="small" variant="outlined" color="success" label="Already tracked" />;
  if (m.retired) return <Chip size="small" variant="outlined" color="warning" label="Retired, cannot be tracked" />;
  return <Chip size="small" variant="outlined" label="Will be tracked" />;
}

function Matched({ matched }: { matched: readonly ResolvedInstrument[] }) {
  const rows = matched.slice(0, shownRows);
  return (
    <Box>
      <Typography variant="h3" component="h3" sx={{ mb: 0.5 }}>
        Matched ({formatNumber(matched.length)})
      </Typography>
      <Box sx={{ maxHeight: 260, overflow: 'auto', border: 1, borderColor: 'divider', borderRadius: 1 }}>
        <Table size="small" stickyHeader aria-label="Matched instruments">
          <TableHead>
            <TableRow>
              <TableCell>Entry</TableCell>
              <TableCell>Symbol</TableCell>
              <TableCell>Name</TableCell>
              <TableCell>Type</TableCell>
              <TableCell>Result</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((m) => (
              <TableRow key={m.id}>
                <TableCell sx={{ fontFamily: monoFontFamily }}>{m.term}</TableCell>
                <TableCell sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}>{m.symbol}</TableCell>
                <TableCell>{m.name}</TableCell>
                <TableCell>{typeLabel(m.type)}</TableCell>
                <TableCell>{statusOf(m)}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </Box>
      {matched.length > shownRows && (
        <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
          {`Showing the first ${formatNumber(shownRows)}; all ${formatNumber(matched.length)} are used.`}
        </Typography>
      )}
    </Box>
  );
}

function Unmatched({ unmatched }: { unmatched: ResolveResult['unmatched'] }) {
  const rows = unmatched.slice(0, shownRows);
  return (
    <Box>
      <Typography variant="h3" component="h3">
        Not matched ({formatNumber(unmatched.length)})
      </Typography>
      <Typography variant="body2" color="text.secondary">
        These are never guessed. Fix them and paste again, or leave them out.
      </Typography>
      <List dense aria-label="Entries that were not matched" sx={{ maxHeight: 200, overflow: 'auto' }}>
        {rows.map((u) => (
          <ListItem key={u.term} disableGutters>
            <ListItemText
              primary={u.term}
              secondary={unmatchedReason(u.reason)}
              slotProps={{ primary: { sx: { fontFamily: monoFontFamily } } }}
            />
          </ListItem>
        ))}
      </List>
      {unmatched.length > shownRows && (
        <Typography variant="body2" color="text.secondary">
          {`…and ${formatNumber(unmatched.length - shownRows)} more.`}
        </Typography>
      )}
    </Box>
  );
}

/** The second step: what the pasted list resolved to, before anything is tracked. */
export function PastePreview({ result }: { result: ResolveResult }) {
  const fresh = newlyTrackable(result.matched).length;
  const already = result.matched.filter((m) => m.tracked).length;
  const retired = result.matched.filter((m) => !m.tracked && m.retired).length;
  const summary = [
    `${formatNumber(result.matched.length)} matched`,
    `${formatNumber(result.unmatched.length)} not matched`,
    result.duplicates > 0 ? `${formatNumber(result.duplicates)} repeated` : null,
  ]
    .filter((part): part is string => part !== null)
    .join(' · ');
  return (
    <Stack spacing={2}>
      <Typography role="status" sx={{ fontWeight: 600 }}>
        {summary}
      </Typography>
      <Typography color="text.secondary">
        {`${formatNumber(fresh)} will be tracked now${already > 0 ? `, ${formatNumber(already)} already are` : ''}${
          retired > 0 ? `, ${formatNumber(retired)} are no longer in Dhan's list and are skipped` : ''
        }.`}
      </Typography>
      {result.matched.length === 0 && <Alert severity="warning">None of the entries matched an instrument.</Alert>}
      {result.matched.length > 0 && <Matched matched={result.matched} />}
      {result.unmatched.length > 0 && <Unmatched unmatched={result.unmatched} />}
    </Stack>
  );
}
