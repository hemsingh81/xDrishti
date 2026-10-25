import { Box } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { NonValue } from '@/shared/analytics';
import { pivotQuery } from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import type { ReportPanelProps } from '../lib/reportFilter';
import type { CellInfo } from './CellPanel';
import { QueryPanel } from './Panel';
import { PivotGrid } from './PivotGrid';

interface BasketGridsProps extends ReportPanelProps {
  onOpen: (info: CellInfo) => void;
  selectedKey: string | null;
}

function Grid({ other, ...props }: BasketGridsProps & { other: 'side' | 'style' }) {
  const result = useQuery(pivotQuery(props.query, `basket,${other}`));
  const names = useBasketNames();
  const title = `Basket by ${other}`;
  return (
    <QueryPanel title={title} query={result}>
      {(pivot) =>
        pivot.cells.length === 0 ? (
          <NonValue state={{ kind: 'empty', reason: 'No trades in this selection.' }} />
        ) : (
          <PivotGrid
            pivot={pivot}
            min={props.min}
            names={names}
            label={`Expectancy by basket and ${other}`}
            onOpen={props.onOpen}
            selectedKey={props.selectedKey}
          />
        )
      }
    </QueryPanel>
  );
}

/** Basket by side and basket by style, rows first (two requests). */
export function BasketGrids(props: BasketGridsProps) {
  return (
    <Box sx={{ display: 'grid', gap: 2, gridTemplateColumns: 'minmax(0, 1fr)' }}>
      <Grid {...props} other="side" />
      <Grid {...props} other="style" />
    </Box>
  );
}
