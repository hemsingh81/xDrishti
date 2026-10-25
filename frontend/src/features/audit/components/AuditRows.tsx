import { Box, Link, Paper, Stack, Typography } from '@mui/material';
import { DataTable, type Column, type TableStatus } from '@/shared/analytics';
import { formatIstDateTime } from '@/shared/lib/format';
import type { AuditEntry } from '../api/auditQueries';
import { actorText } from '../lib/auditParams';
import { OutcomeChip, SourceChip } from './AuditChips';

interface Props {
  rows: readonly AuditEntry[];
  phone: boolean;
  status?: TableStatus;
  onOpen: (id: string) => void;
}

/** Server text is rendered as text only; a summary that holds markup shows the markup. */
function Open({ row, onOpen }: { row: AuditEntry; onOpen: (id: string) => void }) {
  return (
    <Link
      component="button"
      type="button"
      underline="hover"
      sx={{ textAlign: 'left' }}
      aria-label={`Details of ${row.id}`}
      onClick={() => {
        onOpen(row.id);
      }}
    >
      {row.summary}
    </Link>
  );
}

/** The rows: a table from 768 px, cards below. */
export function AuditRows({ rows, phone, status, onOpen }: Props) {
  if (phone && !status) {
    return (
      <Stack component="ul" aria-label="Audit entries" sx={{ gap: 1, listStyle: 'none', p: 0, m: 0 }}>
        {rows.map((row) => (
          <Paper key={row.id} component="li" variant="outlined" sx={{ p: 1.5 }}>
            <Stack sx={{ gap: 0.75 }}>
              <Typography variant="caption" color="text.secondary">
                {formatIstDateTime(row.at)}
              </Typography>
              <Open row={row} onOpen={onOpen} />
              <Stack direction="row" sx={{ gap: 0.75, flexWrap: 'wrap', alignItems: 'center' }}>
                <SourceChip source={row.source} />
                <OutcomeChip outcome={row.outcome} />
                <Typography variant="caption">{actorText(row)}</Typography>
              </Stack>
            </Stack>
          </Paper>
        ))}
      </Stack>
    );
  }

  const columns: Column<AuditEntry>[] = [
    {
      kind: 'text',
      id: 'at',
      header: 'When (IST)',
      render: (r) => <Box sx={{ whiteSpace: 'nowrap' }}>{formatIstDateTime(r.at)}</Box>,
    },
    { kind: 'text', id: 'source', header: 'Record', render: (r) => <SourceChip source={r.source} /> },
    { kind: 'text', id: 'summary', header: 'What happened', render: (r) => <Open row={r} onOpen={onOpen} /> },
    { kind: 'text', id: 'outcome', header: 'Outcome', render: (r) => <OutcomeChip outcome={r.outcome} /> },
    { kind: 'text', id: 'actor', header: 'By', render: actorText },
    { kind: 'text', id: 'account', header: 'Account', render: (r) => r.accountName ?? '' },
  ];
  return <DataTable label="Audit entries" columns={columns} rows={rows} rowKey={(r) => r.id} status={status} />;
}
