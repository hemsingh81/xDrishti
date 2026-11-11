import { Box, Link, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { EvidenceBlock, NonValue } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import type { BasketRanking } from '../api/profileQueries';
import { countOf, fractionOf, ratioOf } from '../lib/format';
import { Fig } from './Fig';
import { Section } from './Section';

const NOT_SEPARATED =
  'Not separated: the gaps between neighbouring baskets are within their noise, so no order is shown. The rule is indicative, pairwise and uncorrected; it is not a significance test.';

/**
 * The baskets side by side. A rank is printed only when every neighbouring gap is larger than its noise; otherwise the board says
 * "not separated" and keeps the order the baskets were asked in. A basket with no value shows its state and no rank. No label "best".
 */
export function RankingBoard({ ranking }: { ranking: BasketRanking }) {
  const names = new Map(ranking.rows.map((r) => [r.basketId, r.name]));
  const rows = ranking.separated
    ? [...ranking.rows].sort(
        (a, b) => (a.rsRank.number ?? Number.POSITIVE_INFINITY) - (b.rsRank.number ?? Number.POSITIVE_INFINITY),
      )
    : ranking.rows;
  const overlapping = ranking.overlap.filter((o) => (o.jaccard.number ?? 0) > 0);
  return (
    <Section
      title="Baskets side by side"
      note={`Relative strength against ${ranking.index.name ?? ranking.index.symbol ?? 'no index'}, ${formatDate(ranking.window.from)} to ${formatDate(ranking.window.to)}. ${ranking.rankWindow ? `Ranked on ${String(ranking.rankWindow)} sessions.` : 'Fewer than two baskets have a relative strength return, so nothing is ranked.'}`}
    >
      {!ranking.separated && (
        <Typography role="status" sx={{ fontWeight: 600 }}>
          {NOT_SEPARATED}
        </Typography>
      )}
      <TableContainer role="region" aria-label="Ranking board, scrolls sideways" tabIndex={0}>
        <Table size="small" aria-label="Ranking board">
          <TableHead>
            <TableRow>
              <TableCell>Basket</TableCell>
              <TableCell align="right">Rank</TableCell>
              <TableCell align="right">21 sessions</TableCell>
              <TableCell align="right">63 sessions</TableCell>
              <TableCell align="right">126 sessions</TableCell>
              <TableCell align="right">252 sessions</TableCell>
              <TableCell align="right">Return over volatility</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((r) => (
              <TableRow key={r.basketId}>
                <TableCell component="th" scope="row" sx={{ position: 'sticky', left: 0, bgcolor: 'background.paper' }}>
                  <Link
                    component={RouterLink}
                    to={`/baskets/${r.basketId}/profile`}
                    sx={{ display: 'inline-block', minHeight: 44, lineHeight: '44px' }}
                  >
                    {r.name}
                  </Link>
                </TableCell>
                <TableCell align="right">
                  {r.rsRank.state === 'value' && !ranking.separated ? (
                    <NonValue state={{ kind: 'not_evaluable', reason: 'Not separated' }} wordOnly />
                  ) : (
                    <Fig view={r.rsRank} format={countOf} />
                  )}
                </TableCell>
                <TableCell align="right">
                  <Fig view={r.rsReturn21} format={fractionOf} tone="gain-loss" />
                </TableCell>
                <TableCell align="right">
                  <Fig view={r.rsReturn63} format={fractionOf} tone="gain-loss" />
                </TableCell>
                <TableCell align="right">
                  <Fig view={r.rsReturn126} format={fractionOf} tone="gain-loss" />
                </TableCell>
                <TableCell align="right">
                  <Fig view={r.rsReturn252} format={fractionOf} tone="gain-loss" />
                </TableCell>
                <TableCell align="right">
                  <Fig view={r.riskAdjusted} format={ratioOf} />
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      {overlapping.length > 0 && (
        <Box>
          <Typography sx={{ fontWeight: 600 }}>Baskets overlap; results are not independent.</Typography>
          <Box component="ul" aria-label="Shared members" sx={{ m: 0, pl: 2.5 }}>
            {overlapping.map((o) => (
              <li key={`${o.a}-${o.b}`}>
                {`${names.get(o.a) ?? 'A basket'} and ${names.get(o.b) ?? 'a basket'} share `}
                <Fig view={o.jaccard} format={fractionOf} />
                {' of their combined members.'}
              </li>
            ))}
          </Box>
        </Box>
      )}
      <EvidenceBlock
        n={ranking.evidence.n}
        range={{ from: ranking.window.from, to: ranking.window.to }}
        basis="unknown"
        basisText={ranking.evidence.basis}
        limits={[...ranking.evidence.limits, ...ranking.evidence.unavailableInputs]}
      />
    </Section>
  );
}
