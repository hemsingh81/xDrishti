import GridViewIcon from '@mui/icons-material/GridViewOutlined';
import { Box, Button, Link as MuiLink, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { NonValue, useUrlState } from '@/shared/analytics';
import { PageHeader } from '@/shared/ui';
import { dimensionReasonQuery } from '../api/reportQueries';
import { useReportFilter } from '../hooks/useReportFilter';
import { toQuery } from '../lib/reportFilter';
import { DistributionPanel } from './DistributionPanel';
import { EquityPanel } from './EquityPanel';
import { KpiTiles } from './KpiTiles';
import { Panel } from './Panel';
import { TradeList } from './TradeList';

/** A link into a report view of Reports with the cell's own filters (they are in the address of this page). */
function ViewLink({ view, children }: { view: string; children: string }) {
  const [params] = useSearchParams();
  const search = new URLSearchParams(params);
  search.delete('cell');
  search.set('view', view);
  return (
    <MuiLink component={Link} to={`/reports?${search.toString()}`}>
      {children}
    </MuiLink>
  );
}

/**
 * The detail of one cell (P8-11): the filters that define it are in the address (so Back and a link work), the P8-06 figures are asked
 * with exactly those filters, and the trades of the cell are listed under them. A cell below the minimum says so and still lists its trades.
 */
export function CellDetailPage() {
  const { filter } = useReportFilter();
  const navigate = useNavigate();
  const title = useUrlState().text('cell') ?? 'Cell';
  const regime = useQuery(dimensionReasonQuery('regime'));
  const query = toQuery(filter);
  const panel = { query, min: null };
  return (
    <>
      <PageHeader
        title={title}
        icon={GridViewIcon}
        hue="reports"
        description="The cell, its figures and its trades. In-sample, simulated trades on replayed history."
        actions={
          <Button
            onClick={() => {
              void navigate(-1);
            }}
          >
            Back to the report
          </Button>
        }
      />
      <Box sx={{ display: 'grid', gap: 2 }}>
        <KpiTiles {...panel} />
        <EquityPanel {...panel} />
        <DistributionPanel {...panel} />
        <Panel title="Exit comparison and MAE / MFE">
          <Typography variant="body2">
            Built in P8-10, over the same filters: <ViewLink view="exits">Exit comparison</ViewLink> and{' '}
            <ViewLink view="maemfe">MAE / MFE</ViewLink>.
          </Typography>
        </Panel>
        <Panel title="Regime split">
          <NonValue
            state={{
              kind: 'unavailable',
              reason: regime.data ?? 'No market context is stored, so no trade has a regime.',
            }}
          />
        </Panel>
        <TradeList query={query} />
      </Box>
    </>
  );
}
