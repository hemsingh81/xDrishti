import { Typography } from '@mui/material';
import type { RampView } from '../api/rampQueries';
import { gateNames, gateStates, num } from '../lib/rampLabels';

/** The evidence gates of the current step: state (word and symbol), the number, the threshold and n. A number that could not be measured is a word, never a digit. */
export function RampGatesTable({ gates }: { gates: RampView['gates'] }) {
  if (gates.length === 0) {
    return <Typography>No evidence yet: the gates appear once a step is set.</Typography>;
  }
  return (
    <table aria-label="Evidence gates">
      <thead>
        <tr>
          <th scope="col">Gate</th>
          <th scope="col">State</th>
          <th scope="col">Measured</th>
          <th scope="col">Needed</th>
          <th scope="col">n</th>
          <th scope="col">Why</th>
        </tr>
      </thead>
      <tbody>
        {gates.map((g) => {
          const state = gateStates[g.state] ?? { word: g.state, icon: '?' };
          return (
            <tr key={g.code}>
              <th scope="row">{gateNames[g.code] ?? g.code}</th>
              <td>
                <span aria-hidden="true">{state.icon} </span>
                {state.word}
              </td>
              <td>{num(g.number)}</td>
              <td>{num(g.threshold)}</td>
              <td>{g.n ?? 'not known'}</td>
              <td>{g.reason}</td>
            </tr>
          );
        })}
      </tbody>
    </table>
  );
}
