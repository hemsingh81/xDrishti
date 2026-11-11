import { Typography } from '@mui/material';
import { useCallback, useState } from 'react';
import { HeatGrid, NonValue, type HeatGridCell } from '@/shared/analytics';
import type { BasketProfile } from '../api/profileQueries';
import { ratioOf } from '../lib/format';
import { Section } from './Section';

/** Correlations of the most liquid members (at most 40), in sector order. A pair without enough common sessions is a word, not a number. */
export function CorrelationPanel({ profile }: { profile: BasketProfile }) {
  const { correlation } = profile;
  const matrix = correlation.matrix;
  const [open, setOpen] = useState<string | null>(null);
  const cell = useCallback(
    (rowId: string, columnId: string): HeatGridCell | null => {
      if (!matrix) return null;
      const r = matrix.symbols.indexOf(rowId);
      const c = matrix.symbols.indexOf(columnId);
      const rho = matrix.rho[r]?.[c];
      const name = `${rowId} with ${columnId}`;
      if (typeof rho !== 'number') {
        return {
          key: `${rowId}|${columnId}`,
          name: `${name}: not enough common sessions`,
          state: { kind: 'unavailable', reason: 'Not enough common sessions' },
          content: <NonValue state={{ kind: 'unavailable', reason: 'Not enough common sessions' }} wordOnly />,
        };
      }
      return {
        key: `${rowId}|${columnId}`,
        name: `${name}: correlation ${ratioOf(rho)}`,
        state: { kind: 'value', value: rho },
        shade: { tone: rho >= 0 ? 'success' : 'error', alpha: Math.min(0.6, Math.abs(rho) * 0.6) },
        content: ratioOf(rho),
      };
    },
    [matrix],
  );
  if (!matrix) {
    return (
      <Section title="Correlation inside the basket">
        <NonValue
          state={{ kind: 'unavailable', reason: correlation.rhoBar.reason ?? 'No correlation figure for this basket.' }}
        />
      </Section>
    );
  }
  const rows = matrix.symbols.map((s) => ({ id: s, label: s }));
  const [a, b] = open?.split('|') ?? [];
  return (
    <Section
      title="Correlation inside the basket"
      note={
        matrix.total > matrix.symbols.length
          ? `Too many members for a matrix: showing the ${String(matrix.symbols.length)} most liquid of ${String(matrix.total)}.`
          : 'Pairwise correlation of daily returns over the last 60 sessions.'
      }
    >
      <HeatGrid
        label="Correlation matrix"
        regionLabel="Correlation matrix, scrolls sideways"
        rowHeader="Member"
        rows={rows}
        columns={rows}
        cell={cell}
        selectedKey={open}
        onOpen={setOpen}
      />
      {a && b && (
        <Typography role="status" variant="body2">
          {(() => {
            const rho = matrix.rho[matrix.symbols.indexOf(a)]?.[matrix.symbols.indexOf(b)];
            return typeof rho === 'number'
              ? `${a} and ${b}: correlation ${ratioOf(rho)}.`
              : `${a} and ${b}: not enough common sessions.`;
          })()}
        </Typography>
      )}
    </Section>
  );
}
