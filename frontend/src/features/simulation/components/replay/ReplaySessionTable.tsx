import {
  Box,
  Button,
  Card,
  CardContent,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatDate, formatInr, formatPercent } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { replaySessionsQuery } from '../../api/replayApi';
import { SessionDrawer } from './SessionDrawer';

/** One row per session; a day opens its plan. */
export function ReplaySessionTable({ runId, active }: { runId: string; active: boolean }) {
  const sessions = useQuery(replaySessionsQuery(runId, active));
  const [open, setOpen] = useState<string | null>(null);
  return (
    <Card component="section" aria-labelledby="replay-sessions-title">
      <CardContent sx={{ minWidth: 0 }}>
        <Typography id="replay-sessions-title" variant="h2" component="h2" sx={{ mb: 1 }}>
          Sessions
        </Typography>
        {sessions.isPending && <LoadingState label="Loading the sessions…" />}
        {sessions.isError && <ErrorState error={sessions.error} />}
        {sessions.data &&
          (sessions.data.sessions.length === 0 ? (
            <Typography color="text.secondary">No session is stored yet.</Typography>
          ) : (
            <TableContainer sx={{ maxHeight: 420 }}>
              <Table size="small" stickyHeader aria-label="Replay sessions">
                <TableHead>
                  <TableRow>
                    <TableCell>Day</TableCell>
                    <TableCell align="right">Candidates</TableCell>
                    <TableCell align="right">Taken</TableCell>
                    <TableCell align="right">Blocked</TableCell>
                    <TableCell align="right">P&amp;L</TableCell>
                    <TableCell align="right">Equity</TableCell>
                    <TableCell align="right">Drawdown</TableCell>
                  </TableRow>
                </TableHead>
                <TableBody>
                  {sessions.data.sessions.map((s) => {
                    const blocked = Object.values(s.blockedByReason).reduce((a, b) => a + b, 0);
                    return (
                      <TableRow key={s.session}>
                        <TableCell>
                          <Button
                            size="small"
                            onClick={() => {
                              setOpen(s.session);
                            }}
                            aria-label={`Open the plan of ${formatDate(s.session)}`}
                            sx={{ minHeight: 44, whiteSpace: 'nowrap' }}
                          >
                            {formatDate(s.session)}
                          </Button>
                        </TableCell>
                        <TableCell align="right">{s.candidates}</TableCell>
                        <TableCell align="right">{s.taken}</TableCell>
                        <TableCell align="right">{blocked}</TableCell>
                        <TableCell align="right">{formatInr(s.pnl)}</TableCell>
                        <TableCell align="right">{formatInr(s.equity)}</TableCell>
                        <TableCell align="right">
                          {s.drawdownPct === null ? 'n/a' : `${formatPercent(s.drawdownPct)}%`}
                        </TableCell>
                      </TableRow>
                    );
                  })}
                </TableBody>
              </Table>
            </TableContainer>
          ))}
        <Box>
          <SessionDrawer
            runId={runId}
            session={open}
            onClose={() => {
              setOpen(null);
            }}
          />
        </Box>
      </CardContent>
    </Card>
  );
}
