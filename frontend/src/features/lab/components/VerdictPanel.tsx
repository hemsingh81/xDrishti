import {
  Alert,
  Box,
  Chip,
  List,
  ListItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { formatNumber } from '@/shared/lib/format';
import type { LabVerdict } from '../api/runQueries';

const outcomeSeverity: Record<string, 'success' | 'warning' | 'error' | undefined> = {
  PASS: 'success',
  MARGINAL: 'warning',
  FAIL: 'error',
};

/** Shown when the server lists no limits: a result is never presented without saying it has some. */
export const noLimits = 'No limits were listed; treat any result as exploration, not proof.';

const num = (v: number | null): string => (v === null ? 'n/a' : formatNumber(v));

/** The verdict: outcome, how many variants tightened it, every gate with its adjusted threshold, and the honest limits. */
export function VerdictPanel({ verdict }: { verdict: LabVerdict }) {
  const severity = outcomeSeverity[verdict.outcome] ?? 'info';
  return (
    <Box>
      <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap', mb: 1 }}>
        <Typography variant="h3" component="h3">
          Verdict
        </Typography>
        <Chip color={severity} label={verdict.outcome} />
        <Typography color="text.secondary">
          {verdict.variantCount} {verdict.variantCount === 1 ? 'variant' : 'variants'} of this family tested; thresholds
          x{formatNumber(verdict.adjustmentFactor)}.
        </Typography>
      </Stack>
      <Table size="small" aria-label="Gates">
        <TableHead>
          <TableRow>
            <TableCell>Gate</TableCell>
            <TableCell align="right">Required</TableCell>
            <TableCell align="right">Adjusted</TableCell>
            <TableCell align="right">Actual</TableCell>
            <TableCell>Result</TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {verdict.gates.map((g, i) => (
            <TableRow key={`${i}${g.name}`}>
              <TableCell>
                {g.name}
                <Typography variant="caption" component="div" color="text.secondary">
                  {g.reason}
                </Typography>
              </TableCell>
              <TableCell align="right">{formatNumber(g.required)}</TableCell>
              <TableCell align="right">{formatNumber(g.adjusted)}</TableCell>
              <TableCell align="right">{num(g.actual)}</TableCell>
              <TableCell>{g.passed ? 'Met' : 'Missed'}</TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
      <Alert severity="info" sx={{ mt: 1 }}>
        <Typography variant="subtitle2">Honest limits</Typography>
        {verdict.limits.length > 0 ? (
          <List dense disablePadding aria-label="Honest limits of the verdict">
            {verdict.limits.map((l, i) => (
              <ListItem key={`${i}${l}`} disableGutters>
                {l}
              </ListItem>
            ))}
          </List>
        ) : (
          <Typography>{noLimits}</Typography>
        )}
      </Alert>
      {verdict.labels.map((l, i) => (
        <Typography key={`${i}${l}`} variant="caption" component="p" color="text.secondary">
          {l}
        </Typography>
      ))}
    </Box>
  );
}
