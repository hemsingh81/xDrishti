import { Box, Table, TableBody, TableCell, TableHead, TableRow } from '@mui/material';
import { useState } from 'react';
import { NonValue } from '@/shared/analytics';
import type { BasketProfile } from '../api/profileQueries';
import { fractionOf, percentOf } from '../lib/format';
import { Section } from './Section';

const ROW = 44;
const VIEW = 400;
const OVER = 8;
/** Above this many rows only the rows in view are drawn. */
export const VIRTUAL_ABOVE = 100;

/** Every member the figures rest on, by share of the return. Text only: a symbol is never drawn as markup. */
export function MemberTable({ profile }: { profile: BasketProfile }) {
  const rows = profile.contribution.rows;
  const [top, setTop] = useState(0);
  if (rows.length === 0) {
    return (
      <Section title="Members">
        <NonValue
          state={{
            kind: 'unavailable',
            reason: 'The members are listed with their shares only when the basket has a nonzero hold return.',
          }}
        />
      </Section>
    );
  }
  const virtual = rows.length > VIRTUAL_ABOVE;
  const first = virtual ? Math.max(0, Math.floor(top / ROW) - OVER) : 0;
  const last = virtual ? Math.min(rows.length, Math.ceil((top + VIEW) / ROW) + OVER) : rows.length;
  return (
    <Section title="Members" note={`${String(rows.length)} members, largest share of the return first.`}>
      <Box
        role="region"
        aria-label="Members, scrolls"
        tabIndex={0}
        onScroll={(e) => {
          if (virtual) setTop(e.currentTarget.scrollTop);
        }}
        sx={{ overflow: 'auto', maxHeight: virtual ? VIEW : undefined, minWidth: 0 }}
      >
        <Table size="small" aria-label="Members" stickyHeader>
          <TableHead>
            <TableRow>
              <TableCell>Symbol</TableCell>
              <TableCell align="right">Hold return</TableCell>
              <TableCell align="right">Share of the return</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {first > 0 && (
              <TableRow aria-hidden style={{ height: first * ROW }}>
                <TableCell colSpan={3} sx={{ p: 0, border: 0 }} />
              </TableRow>
            )}
            {rows.slice(first, last).map((r) => (
              <TableRow key={r.symbol} style={{ height: ROW }}>
                <TableCell component="th" scope="row" sx={{ position: 'sticky', left: 0, bgcolor: 'background.paper' }}>
                  {r.symbol}
                </TableCell>
                <TableCell align="right">{percentOf(r.returnPct)}</TableCell>
                <TableCell align="right">{fractionOf(r.share)}</TableCell>
              </TableRow>
            ))}
            {last < rows.length && (
              <TableRow aria-hidden style={{ height: (rows.length - last) * ROW }}>
                <TableCell colSpan={3} sx={{ p: 0, border: 0 }} />
              </TableRow>
            )}
          </TableBody>
        </Table>
      </Box>
    </Section>
  );
}
