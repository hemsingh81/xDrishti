import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import type { ServiceStatus } from '@/shared/api';
import { formatAgo, formatDateTime } from '@/shared/lib/format';
import { StatusChip } from '@/shared/ui';

export function ServiceTable({ services }: { services: readonly ServiceStatus[] }) {
  if (services.length === 0) {
    return (
      <Typography color="text.secondary" sx={{ p: 2 }}>
        No background service has reported yet.
      </Typography>
    );
  }

  return (
    <TableContainer>
      <Table size="small" aria-label="Background services">
        <TableHead>
          <TableRow>
            <TableCell>Service</TableCell>
            <TableCell>Status</TableCell>
            <TableCell>Version</TableCell>
            <TableCell>Instance</TableCell>
            <TableCell>Started</TableCell>
            <TableCell>Last seen</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {services.map((s) => (
            <TableRow key={s.name}>
              <TableCell sx={{ fontWeight: 600 }}>{s.name}</TableCell>
              <TableCell>
                <StatusChip ok={s.isAlive} okLabel="Running" badLabel="Not reporting" />
              </TableCell>
              <TableCell>{s.version}</TableCell>
              <TableCell sx={{ fontFamily: 'monospace' }}>{s.instance}</TableCell>
              <TableCell>{formatDateTime(s.startedAt)}</TableCell>
              <TableCell>{formatAgo(s.lastSeenAt)}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
  );
}
