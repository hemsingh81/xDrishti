import { Table, TableBody, TableCell, TableHead, TableRow, Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { fin } from './nullsFormat';
import { StatusMark } from './StatusMark';

/** The placebo shapes with the strategy's rank among them; empty says so. */
export function PlaceboTable({ rows }: { rows: Schemas['PlaceboRowView'][] }) {
  if (rows.length === 0) return <Typography variant="body2">No placebo shape could be walked.</Typography>;
  return (
    <Table size="small" aria-label="Placebo shapes">
      <TableHead>
        <TableRow>
          <TableCell>Shape</TableCell>
          <TableCell align="right">Trades</TableCell>
          <TableCell align="right">Net expectancy R</TableCell>
          <TableCell align="right">Rank</TableCell>
          <TableCell>Status</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((r) => (
          <TableRow key={r.shape}>
            <TableCell>{r.shape}</TableCell>
            <TableCell align="right">{r.trades}</TableCell>
            <TableCell align="right">{fin(r.meanR)}</TableCell>
            <TableCell align="right">{r.rank ?? 'n/a'}</TableCell>
            <TableCell>{r.comparable ? 'compared' : `not compared${r.reason ? `: ${r.reason}` : ''}`}</TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

/** One row per swept parameter: neighbours' retention, the median and the centre (reported, never applied). */
export function PlateauTable({ params }: { params: Schemas['PlateauParameterView'][] }) {
  if (params.length === 0) return <Typography variant="body2">No parameter was swept.</Typography>;
  return (
    <Table size="small" aria-label="Plateau by parameter">
      <TableHead>
        <TableRow>
          <TableCell>Parameter</TableCell>
          <TableCell align="right">Declared</TableCell>
          <TableCell align="right">Lower kept</TableCell>
          <TableCell align="right">Upper kept</TableCell>
          <TableCell align="right">Median R</TableCell>
          <TableCell align="right">Centre</TableCell>
          <TableCell>Status</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {params.map((p) => (
          <TableRow key={p.param}>
            <TableCell>
              {p.param}
              <Typography variant="caption" component="div" color="text.secondary">
                {`${p.reading}: ${p.reason}`}
              </Typography>
            </TableCell>
            <TableCell align="right">{fin(p.declared)}</TableCell>
            <TableCell align="right">{fin(p.lowerRetention)}</TableCell>
            <TableCell align="right">{fin(p.upperRetention)}</TableCell>
            <TableCell align="right">{fin(p.median)}</TableCell>
            <TableCell align="right">{fin(p.centreValue)}</TableCell>
            <TableCell>
              <StatusMark status={p.status} />
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}
