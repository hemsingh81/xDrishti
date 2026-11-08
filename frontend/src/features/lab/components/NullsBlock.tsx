import { Box, Stack, Typography } from '@mui/material';
import type { Schemas } from '@/shared/api';
import { fin, known } from './nullsFormat';
import { PlaceboTable, PlateauTable } from './NullsTables';
import { StatusMark } from './StatusMark';

function Line({ title, status, reason }: { title: string; status: string; reason: string | null | undefined }) {
  return (
    <Typography variant="body2">
      {`${title}: `}
      <StatusMark status={status} />
      {known(status) === 'unavailable' && reason ? ` (${reason})` : reason ? `. ${reason}` : ''}
    </Typography>
  );
}

/** The header follows both switches: the null's and the plateau's. */
function enforcement(b: Schemas['NullsView']): string {
  const p = b.plateau;
  if (b.capped || p?.capped) return 'Enforced: the outcome was capped at MARGINAL.';
  if (b.enforce || p?.enforce)
    return b.enforce && (p?.enforce ?? true) ? 'Enforced.' : 'Partly enforced: see each line.';
  return 'Report only: the outcome is not changed.';
}

/**
 * Chance and plateau (CF-07, ADR 0231): the random-entry null, the 20 placebo shapes, the mirror and the plateau of the in-sample sweep.
 * Report-only unless enforced. `unavailable` shows the server's reason and is never a pass. Absent in a result stored before it.
 */
export function NullsBlock({ block }: { block?: Schemas['NullsView'] | null }) {
  if (!block) {
    return (
      <Typography variant="body2" sx={{ mt: 1 }} aria-label="Chance and plateau">
        Chance and plateau: not computed.
      </Typography>
    );
  }
  const r = block.randomEntry;
  return (
    <Box component="section" aria-label="Chance and plateau" sx={{ mt: 1 }}>
      <Typography variant="subtitle2">Chance and plateau</Typography>
      <Typography variant="body2">{enforcement(block)}</Typography>
      <Stack spacing={0.5} sx={{ mt: 0.5 }}>
        <Line title="Random entry" status={r.status} reason={r.reason} />
        <Typography variant="body2" color="text.secondary">
          {`Real mean ${fin(r.realMeanR)} R against random ${fin(r.randomMeanR)} R (5th ${fin(r.p05)}, median ${fin(r.p50)}, 95th ${fin(r.p95)}, 99th ${fin(r.p99)}). p = ${fin(r.pValue)}, needs ${r.comparison} ${fin(r.threshold)}; ${r.countAtLeast} of ${r.validSeeds} seeds at least as good, ${r.draws} drawn. ${r.trades} of ${r.oosTrades} out-of-sample trades restated; ${fin(r.droppedShare * 100)} % dropped, ${r.redraws} redraws; seed ${r.seed}.`}
        </Typography>
        {block.placebos ? (
          <>
            <Line
              title="Placebo shapes"
              status={block.placebos.status}
              reason={`${block.placebos.reason} Rank ${block.placebos.realRank ?? 'n/a'}, p = ${fin(block.placebos.pValue)}, needs ${block.placebos.comparison} ${fin(block.placebos.threshold)}; ${block.placebos.comparable} of ${block.placebos.shapes} comparable.`}
            />
            <PlaceboTable rows={block.placebos.rows} />
          </>
        ) : (
          <Typography variant="body2">Placebo shapes: not computed.</Typography>
        )}
        {block.mirror ? (
          <Line
            title="Mirror"
            status={block.mirror.status}
            reason={`${block.mirror.reason} Mirror ${fin(block.mirror.mirrorMeanR)} R against real ${fin(block.mirror.realMeanR)} R over ${block.mirror.trades} trades, needs ${block.mirror.comparison} ${fin(block.mirror.threshold)}.`}
          />
        ) : (
          <Typography variant="body2">Mirror: not computed.</Typography>
        )}
        {block.plateau ? (
          <>
            <Line
              title="Plateau (in-sample sweep)"
              status={block.plateau.status}
              reason={`${block.plateau.reason} Each neighbour keeps ${block.plateau.comparison} ${fin(block.plateau.retention)} of the base (at least ${block.plateau.minPoints} points). ${block.plateau.enforce ? (block.plateau.capped ? 'Enforced: capped at MARGINAL.' : 'Enforced.') : 'Report only.'}`}
            />
            <PlateauTable params={block.plateau.parameters} />
          </>
        ) : (
          <Typography variant="body2">Plateau (in-sample sweep): not computed.</Typography>
        )}
      </Stack>
      <Typography variant="caption" component="p" color="text.secondary">
        {`${block.basis} Settings: ${block.settings}.`}
      </Typography>
      {block.notes.map((n, i) => (
        <Typography key={`${i}${n}`} variant="caption" component="p" color="text.secondary">
          {n}
        </Typography>
      ))}
    </Box>
  );
}
