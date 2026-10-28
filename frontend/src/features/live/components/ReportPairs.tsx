import { Typography } from '@mui/material';
import type { LiveVsPaperView } from '../api/reportQueries';
import { reasonWords } from '../lib/reportLabels';

/** The pairs (plan item ascending) and the tickets that could not be paired, each with its one reason. */
export function ReportPairs({ view }: { view: LiveVsPaperView }) {
  return (
    <>
      <section aria-label="Pairs">
        <Typography variant="h3" component="h3">
          Pairs
        </Typography>
        {view.pairs.length === 0 ? (
          <Typography>No live round trip has a paper twin yet.</Typography>
        ) : (
          <table aria-label="Paired tickets">
            <thead>
              <tr>
                <th scope="col">Ticket</th>
                <th scope="col">Symbol</th>
                <th scope="col">Date</th>
                <th scope="col">Live R</th>
                <th scope="col">Paper R</th>
                <th scope="col">Shortfall R</th>
                <th scope="col">Entry</th>
                <th scope="col">Exit</th>
              </tr>
            </thead>
            <tbody>
              {view.pairs.map((p) => (
                <tr key={p.planItemId}>
                  <th scope="row">{p.planItemId}</th>
                  <td>{`${p.symbol} ${p.side}`}</td>
                  <td>{p.date}</td>
                  <td>{p.liveR.toFixed(4)}</td>
                  <td>{p.paperR.toFixed(4)}</td>
                  <td>{p.shortfallR.toFixed(4)}</td>
                  <td>{p.entryDiffR.toFixed(4)}</td>
                  <td>{p.exitDiffR.toFixed(4)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        {view.pairsTotal > view.pairs.length && (
          <Typography variant="body2">{`Showing ${String(view.pairs.length)} of ${String(view.pairsTotal)} pairs.`}</Typography>
        )}
      </section>
      <section aria-label="Left out">
        <Typography variant="h3" component="h3">
          Left out
        </Typography>
        {view.excluded.length === 0 ? (
          <Typography>No ticket was left out.</Typography>
        ) : (
          <ul>
            {view.excluded.map((e) => (
              <li key={`${String(e.planItemId)}|${e.reason}`}>
                <Typography variant="body2">{`${e.symbol} ${e.date}, ticket ${String(e.planItemId)}: ${reasonWords[e.reason] ?? e.reason}. ${e.detail}`}</Typography>
              </li>
            ))}
          </ul>
        )}
      </section>
    </>
  );
}
