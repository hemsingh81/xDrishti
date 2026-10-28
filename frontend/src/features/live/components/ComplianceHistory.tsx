import { Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import { formatDate, formatIstDateTime } from '@/shared/lib/format';
import type { ComplianceRecord } from '../api/complianceQueries';
import { kindLabels, outcomeLabel } from '../lib/labels';
import { OverdueChip } from './ComplianceCards';

/** Every record, newest first. Text is shown as text; the sources are named, never opened by the app. */
export function ComplianceHistory({ items }: { items: readonly ComplianceRecord[] }) {
  if (items.length === 0) return <Typography color="text.secondary">No record in the history yet.</Typography>;
  return (
    <Table size="small" aria-label="Compliance history">
      <TableHead>
        <TableRow>
          <TableCell>Kind</TableCell>
          <TableCell>Outcome</TableCell>
          <TableCell>Decided</TableCell>
          <TableCell>Next review</TableCell>
          <TableCell>Note</TableCell>
          <TableCell>Written</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {items.map((r) => (
          <TableRow key={r.id}>
            <TableCell>{kindLabels[r.kind] ?? r.kind}</TableCell>
            <TableCell>{outcomeLabel(r.outcome)}</TableCell>
            <TableCell>{formatDate(r.decidedOn)}</TableCell>
            <TableCell>
              {formatDate(r.nextReviewOn)} {r.overdue && <OverdueChip />}
            </TableCell>
            <TableCell sx={{ maxWidth: 420, overflowWrap: 'anywhere' }}>
              {r.summary}
              {r.sources.length > 0 && (
                <Typography variant="caption" component="div" color="text.secondary">
                  Sources: {r.sources.join(' · ')}
                </Typography>
              )}
            </TableCell>
            <TableCell>{formatIstDateTime(r.createdAt)}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
