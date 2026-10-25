import { Typography } from '@mui/material';
import type { PlanItem } from '../api/planQueries';

const money = (n: number) => n.toFixed(2);

/** The indicative numbers of a ticket as the server states them (P7-19); plain text, no write. */
export function LevelsRow({ levels }: { levels: PlanItem['levels'] }) {
  if (!levels) {
    return <Typography variant="body2">Levels: rules only (see the stop and target text)</Typography>;
  }
  const stop =
    levels.stop === null
      ? 'stop: rule only'
      : `stop ${money(levels.stop)} (${levels.stopAnchor === 'level' ? 'level' : 'from the close'})`;
  const target =
    levels.target === null
      ? 'target: rule only'
      : `target ${money(levels.target)}${levels.rewardRisk === null ? '' : ` (RR ${levels.rewardRisk})`}`;
  return (
    <Typography variant="body2">
      Indicative levels, read at the close of {levels.session}: entry {money(levels.entryRef)} · {stop} · {target}.
      Indicative from the close of {levels.session}, not tomorrow&apos;s price. No order is placed.
    </Typography>
  );
}
