import {
  Alert,
  Box,
  Card,
  CardContent,
  CardHeader,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { formatIstDateTime } from '@/shared/lib/format';
import { configQuery } from '../api/governanceQueries';

/** The configuration in force, the differences not yet captured and the services that would restart the count (read only). */
export function ConfigCard() {
  const q = useQuery(configQuery);
  if (q.isPending) return <LoadingState label="Loading the configuration…" />;
  if (q.isError) return <ErrorState error={q.error} />;
  const v = q.data;
  return (
    <Stack sx={{ gap: 2 }}>
      {v.blockers.map((b) => (
        <Alert key={b.serviceId} severity="warning">
          Service {b.serviceId}: {b.text}
        </Alert>
      ))}
      {v.unreadable.length > 0 && (
        <Alert severity="warning">
          Could not be read: {v.unreadable.join(', ')}. A capture now would claim no change and set its sessions aside
          if the gap is long.
        </Alert>
      )}
      <Card variant="outlined">
        <CardHeader
          title="Differences not yet captured"
          subheader="A preview. Nothing was applied; applying a change starts a new epoch."
        />
        <CardContent>
          {v.pending.length === 0 ? (
            <Typography>None: the configuration equals the last snapshot.</Typography>
          ) : (
            <Table size="small" aria-label="Differences not yet captured">
              <TableHead>
                <TableRow>
                  <TableCell>Component</TableCell>
                  <TableCell>Change</TableCell>
                  <TableCell>Effect</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {v.pending.map((c) => (
                  <TableRow key={c.key}>
                    <TableCell sx={{ wordBreak: 'break-word' }}>{c.key}</TableCell>
                    <TableCell>
                      {c.before ?? '(added)'} → {c.after ?? '(removed)'}
                    </TableCell>
                    <TableCell>
                      <strong>{c.meaning}</strong> ({c.class})
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
      <Card variant="outlined">
        <CardHeader title="Configuration in force" />
        <CardContent>
          <Table size="small" aria-label="Configuration in force">
            <TableHead>
              <TableRow>
                <TableCell>Component</TableCell>
                <TableCell>Value</TableCell>
                <TableCell>If it changes</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {v.components.map((c) => (
                <TableRow key={c.key}>
                  <TableCell sx={{ wordBreak: 'break-word' }}>{c.key}</TableCell>
                  <TableCell sx={{ wordBreak: 'break-word' }}>{c.value}</TableCell>
                  <TableCell>{c.rule}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <Box sx={{ mt: 1 }}>
            <Typography variant="body2" color="text.secondary">
              Fingerprint now {v.currentHash}.{' '}
              {v.last
                ? `Last snapshot ${v.last.hash} for session ${v.last.sessionDate}, taken ${formatIstDateTime(v.last.takenAt)}.`
                : 'No snapshot has been taken yet.'}
            </Typography>
          </Box>
        </CardContent>
      </Card>
    </Stack>
  );
}
