import { Stack, Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import type { LivePhaseReport } from '../api/phaseCheckQueries';

function List({ title, items }: { title: string; items: readonly string[] }) {
  if (items.length === 0) return null;
  return (
    <section aria-label={title}>
      <Typography variant="h3" component="h2">
        {title}
      </Typography>
      <ul>
        {items.map((t) => (
          <li key={t}>{t}</li>
        ))}
      </ul>
    </section>
  );
}

/** Weeks, printed lines, setup, blockers and limits of the phase check. */
export function PhaseDetails({ report }: { report: LivePhaseReport }) {
  return (
    <Stack sx={{ gap: 2 }}>
      <section aria-label="Weeks">
        <Typography variant="h3" component="h2">
          Weeks
        </Typography>
        <Table size="small" aria-label="Week lines">
          <TableHead>
            <TableRow>
              {['Week', 'Sessions', 'Tickets', 'Round trips', 'Cumulative', 'Step', 'Halts', 'Holds', 'Breaches'].map(
                (h) => (
                  <TableCell key={h}>{h}</TableCell>
                ),
              )}
            </TableRow>
          </TableHead>
          <TableBody>
            {report.weeks.map((w) => (
              <TableRow key={w.weekStart}>
                <TableCell>{w.weekStart}</TableCell>
                <TableCell>{w.sessions}</TableCell>
                <TableCell>{w.tickets}</TableCell>
                <TableCell>{w.roundTrips}</TableCell>
                <TableCell>{w.cumulativeRoundTrips}</TableCell>
                <TableCell>{w.step}</TableCell>
                <TableCell>{w.halts}</TableCell>
                <TableCell>{w.holds}</TableCell>
                <TableCell>{w.breaches}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </section>
      <section aria-label="Printed">
        <Typography variant="h3" component="h2">
          Printed, not judged
        </Typography>
        <ul>
          {report.printed.map((p) => (
            <li key={p.label}>
              {p.label}: {p.value}
            </li>
          ))}
        </ul>
      </section>
      <section aria-label="Setup">
        <Typography variant="h3" component="h2">
          Setup
        </Typography>
        <ul>
          {report.setup.map((s) => (
            <li key={s.name}>
              {s.name}: {s.on ? 'on' : 'off'}
            </li>
          ))}
        </ul>
      </section>
      <List title="Blockers" items={report.blockers} />
      <List title="Limits" items={report.limits} />
    </Stack>
  );
}
