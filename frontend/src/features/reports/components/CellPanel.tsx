import { Box, Drawer, IconButton, Link as MuiLink, Typography } from '@mui/material';
import { Link } from 'react-router';
import CloseIcon from '@mui/icons-material/Close';
import {
  EvidenceBlock,
  formatCount,
  formatR,
  formatRate,
  formatRatio,
  MetricValue,
  type MetricState,
} from '@/shared/analytics';
import type { EvidenceView, GroupView } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { cellHref, type CellPart } from '../lib/cellLink';
import { metricFromApi } from '../lib/metricFromApi';
import type { ReportQuery } from '../lib/reportFilter';

/** What a heat cell or table row opens: its figure, n, and (for a group) all its KPIs. */
export interface CellInfo {
  key: string;
  title: string;
  n: number;
  state: MetricState;
  format: (value: number) => string;
  evidence: EvidenceView;
  group?: GroupView;
  min?: number | null;
  /** What the cell is made of; without it (or with a part that is not a filter) the cell has no trade list. */
  parts?: readonly CellPart[];
}

/**
 * The side panel of a cell: its KPIs, the runs behind it and, where the cell is a set of filters, the link to its detail and trades (P8-11).
 * Every figure goes through the kit, so a non-value prints its word.
 */
export function CellPanel({
  info,
  onClose,
  query = {},
}: {
  info: CellInfo | null;
  onClose: () => void;
  query?: ReportQuery;
}) {
  const href = info?.parts ? cellHref(query, info.parts, info.title) : null;
  const group = info?.group;
  const kpis = group
    ? ([
        ['Win rate', group.winRate, formatRate],
        ['Expectancy', group.expectancyR, formatR],
        ['Expectancy lower bound', group.expectancyLowerR, formatR],
        ['Total R', group.totalR, formatR],
        ['Profit factor', group.profitFactor, formatRatio],
        ['Max drawdown', group.maxDrawdownR, formatR],
      ] as const)
    : [];
  return (
    <Drawer
      anchor="right"
      open={info !== null}
      onClose={onClose}
      slotProps={{
        paper: { role: 'dialog', 'aria-modal': true, 'aria-label': info ? `Cell ${info.title}` : undefined },
      }}
    >
      {info && (
        <Box sx={{ width: { xs: '100vw', sm: 380 }, p: 2, display: 'grid', gap: 1.5 }}>
          <Box sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
            <Typography variant="h2" component="h2">
              {info.title}
            </Typography>
            <IconButton aria-label="Close" onClick={onClose} sx={{ minWidth: 44, minHeight: 44 }}>
              <CloseIcon />
            </IconButton>
          </Box>
          <Typography variant="body2">Trades: {formatCount(info.n)}</Typography>
          {group ? (
            <Box
              component="dl"
              sx={{ m: 0, display: 'grid', gridTemplateColumns: 'auto 1fr', columnGap: 2, rowGap: 1 }}
            >
              {kpis.map(([name, metric, format]) => (
                <Box key={name} sx={{ display: 'contents' }}>
                  <Typography component="dt" variant="body2" color="text.secondary">
                    {name}
                  </Typography>
                  <Typography component="dd" variant="body2" sx={{ m: 0 }}>
                    <MetricValue state={metricFromApi(metric, group.n, info.min)} format={format} />
                  </Typography>
                </Box>
              ))}
            </Box>
          ) : (
            <Typography variant="body2" component="div">
              <MetricValue state={info.state} format={info.format} />
            </Typography>
          )}
          <EvidenceBlock {...evidenceOf(info.evidence, { n: info.n })} />
          {href ? (
            <MuiLink component={Link} to={href} variant="body2">
              Open the cell and its trades
            </MuiLink>
          ) : (
            <Typography variant="body2" color="text.secondary">
              This cell cannot be said as filters of a report, so its trades are not listed here.
            </Typography>
          )}
        </Box>
      )}
    </Drawer>
  );
}
