import {
  Button,
  Card,
  CardContent,
  CardHeader,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { ledgerQuery } from '../api/runQueries';

/** The test ledger of a family: every test counts, and the gates tighten with the number of variants. */
export function LedgerCard({ strategyId, onSelectRun }: { strategyId: string; onSelectRun: (runId: string) => void }) {
  const { data, error, isPending } = useQuery(ledgerQuery(strategyId));
  return (
    <Card>
      <CardHeader
        title={<Typography variant="h2">Test ledger - {strategyId}</Typography>}
        subheader={
          data &&
          `${data.variantCount} ${data.variantCount === 1 ? 'variant' : 'variants'} counted; thresholds are tightened x${formatNumber(data.adjustmentFactor)}.`
        }
      />
      <CardContent>
        {isPending && <LoadingState label="Loading the ledger" />}
        {error && <ErrorState error={error} />}
        {data?.rows.length === 0 && <Typography color="text.secondary">No tests of this strategy yet.</Typography>}
        {data && data.rows.length > 0 && (
          <Table size="small" aria-label="Test ledger">
            <TableHead>
              <TableRow>
                <TableCell>When</TableCell>
                <TableCell>Version</TableCell>
                <TableCell>Method</TableCell>
                <TableCell>Universe</TableCell>
                <TableCell>Window</TableCell>
                <TableCell>Run</TableCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {data.rows.map((row) => (
                <TableRow key={`${row.runId}${row.strategyHash}${row.method}`}>
                  <TableCell>{formatIstDateTime(row.createdAt)}</TableCell>
                  <TableCell>v{row.version}</TableCell>
                  <TableCell>{row.method}</TableCell>
                  <TableCell>{row.universe}</TableCell>
                  <TableCell>
                    {row.window.from} to {row.window.to}
                  </TableCell>
                  <TableCell>
                    {/* A sweep point has a derived id that is not a run: no link. */}
                    {row.method === 'sweep' ? (
                      'parameter sweep point'
                    ) : (
                      <Button
                        size="small"
                        onClick={() => {
                          onSelectRun(row.runId);
                        }}
                      >
                        Open run
                      </Button>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        )}
      </CardContent>
    </Card>
  );
}
