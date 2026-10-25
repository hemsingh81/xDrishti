import { Alert, Box } from '@mui/material';
import { useState } from 'react';
import { useUrlState } from '@/shared/analytics';
import { readCompared } from '../lib/compareState';
import { joined, type ReportPanelProps } from '../lib/reportFilter';
import { BasketEquityPanel } from './BasketEquityPanel';
import { BasketGrids } from './BasketGrids';
import { BasketPicker } from './BasketPicker';
import { CellPanel, type CellInfo } from './CellPanel';
import { ComparisonTable } from './ComparisonTable';
import { StrategyByBasketPanel } from './StrategyByBasketPanel';

/** Two to six baskets side by side: table, overlaid cumulative R, strategy per basket and the side and style grids. */
export function BasketComparison({ query, min }: ReportPanelProps) {
  const url = useUrlState();
  const compared = readCompared(url.text('cmp'));
  const setCompared = (ids: string[]) => {
    url.update({ cmp: joined(ids) });
  };
  const [open, setOpen] = useState<CellInfo | null>(null);
  const ready = compared.length >= 2;
  // The compared baskets replace the screen's basket filter for these panels.
  const cmpQuery = { ...query, basket: joined(compared) ?? '' };
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <Box>
        <BasketPicker value={compared} onChange={setCompared} />
      </Box>
      {!ready ? (
        <Alert severity="info">Choose at least two baskets to compare</Alert>
      ) : (
        <>
          <ComparisonTable query={cmpQuery} onOpen={setOpen} />
          <BasketEquityPanel query={cmpQuery} min={min} />
          <StrategyByBasketPanel query={cmpQuery} />
          <BasketGrids query={cmpQuery} min={min} onOpen={setOpen} selectedKey={open?.key ?? null} />
        </>
      )}
      <CellPanel
        info={open}
        onClose={() => {
          setOpen(null);
        }}
      />
    </Box>
  );
}
