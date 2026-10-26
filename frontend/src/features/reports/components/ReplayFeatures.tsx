import { Alert, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { NonValue } from '@/shared/analytics';
import { numeric } from '@/theme';
import type { TradeReplay } from '../api/reportQueries';
import { Panel } from './Panel';

const groups = [
  ['bars', 'Bars'],
  ['context', 'Context'],
  ['plan', 'Plan'],
] as const;

/** The decision-time features stored for the trade (catalogue tf-1), by group. An unknown is a word and its reason, never a zero. */
export function ReplayFeatures({ features }: { features: TradeReplay['features'] }) {
  return (
    <Panel title="Decision-time features">
      {features.state !== 'value' ? (
        <Alert severity="info">{features.reason ?? 'No decision-time features stored for this trade.'}</Alert>
      ) : (
        <>
          <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
            {`${String(features.items.length)} values stored at the decision (catalogue ${features.version}).`}
          </Typography>
          <TableContainer
            tabIndex={0}
            role="region"
            aria-label="Decision-time features, scrolls"
            sx={{ border: 1, borderColor: 'divider', borderRadius: 1, maxHeight: 420 }}
          >
            <Table size="small" stickyHeader aria-label="Decision-time features">
              <TableHead>
                <TableRow>
                  <TableCell>Feature</TableCell>
                  <TableCell align="right">Value</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {groups.flatMap(([source, title]) => [
                  <TableRow key={source}>
                    <TableCell colSpan={2} sx={{ fontWeight: 600 }}>
                      {title}
                    </TableCell>
                  </TableRow>,
                  ...features.items
                    .filter((item) => item.source === source)
                    .map((item) => (
                      <TableRow key={item.name}>
                        <TableCell component="th" scope="row">
                          {item.name}
                        </TableCell>
                        <TableCell align="right" sx={numeric}>
                          {item.state === 'value' && item.value !== null ? (
                            String(Math.round(item.value * 10_000) / 10_000)
                          ) : (
                            <NonValue state={{ kind: 'unavailable', reason: item.reason ?? 'unavailable' }} />
                          )}
                        </TableCell>
                      </TableRow>
                    )),
                ])}
              </TableBody>
            </Table>
          </TableContainer>
        </>
      )}
    </Panel>
  );
}
