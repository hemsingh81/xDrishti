import { Box, ToggleButton, ToggleButtonGroup, Typography } from '@mui/material';
import { useState } from 'react';
import type { ReportPanelProps } from '../lib/reportFilter';
import { EquityByGroup } from './EquityByGroup';
import { StrategyTimeframePanel } from './StrategyTimeframePanel';

type SideChoice = 'both' | 'BUY' | 'SELL';

/** R4: the strategy by timeframe grid with a side selector, and running R of each strategy. Descriptive, not a ranking. */
export function StrategiesReport({ query, min }: ReportPanelProps) {
  const [side, setSide] = useState<SideChoice>('both');
  const gridQuery = side === 'both' ? query : { ...query, side };
  return (
    <Box sx={{ display: 'grid', gap: 2 }}>
      <ToggleButtonGroup
        exclusive
        size="small"
        value={side}
        aria-label="Side"
        onChange={(_event, next: SideChoice | null) => {
          if (next) setSide(next);
        }}
      >
        <ToggleButton value="both">Both sides</ToggleButton>
        <ToggleButton value="BUY">Long (BUY)</ToggleButton>
        <ToggleButton value="SELL">Short (SELL)</ToggleButton>
      </ToggleButtonGroup>
      <StrategyTimeframePanel query={gridQuery} min={min} />
      <EquityByGroup query={query} min={min} title="Equity by strategy" lens="strategy" />
      <Typography variant="body2" color="text.secondary">
        Descriptive, not a ranking: many strategies and timeframes over thin samples invite a selection effect.
      </Typography>
    </Box>
  );
}
