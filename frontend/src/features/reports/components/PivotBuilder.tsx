import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ExportMenu } from '@/features/exports';
import { useState } from 'react';
import { useUrlState } from '@/shared/analytics';
import { pivotQuery } from '../api/reportQueries';
import { useBasketNames } from '../hooks/useBasketNames';
import { NON_ADDITIVE_NOTE } from '../lib/evidence';
import { readPivot } from '../lib/compareState';
import type { ReportPanelProps } from '../lib/reportFilter';
import { CellPanel, type CellInfo } from './CellPanel';
import { readLens } from '../lib/lensToQuery';
import { LensTable } from './LensTable';
import { Panel } from './Panel';
import { PivotAxes } from './PivotAxes';
import { PivotError } from './PivotError';
import { PivotGrid } from './PivotGrid';
import { LoadingState } from '@/shared/ui';

/** Any two dimensions by one metric, plus the lens table. Axes, metric and lens live in the address. */
export function PivotBuilder({ query, min }: ReportPanelProps) {
  const url = useUrlState();
  const axes = readPivot(url.text);
  const lens = readLens(url.text('lens'));
  const [open, setOpen] = useState<CellInfo | null>(null);
  const names = useBasketNames();
  const result = useQuery(pivotQuery(query, `${axes.rows},${axes.columns}`, axes.metric));
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 2 }}>
      <Panel title="Pivot builder">
        <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 1.5 }}>
          <PivotAxes {...axes} set={url.update} />
          {result.isPending ? (
            <LoadingState label="Loading the pivot…" />
          ) : result.isError ? (
            <PivotError error={result.error} set={url.update} />
          ) : (
            <>
              <ExportMenu
                kind="report-pivot"
                query={{ ...query, lens: `${axes.rows},${axes.columns}`, metric: axes.metric }}
              />
              <PivotGrid
                pivot={result.data}
                min={min}
                names={names}
                label="Pivot"
                onOpen={setOpen}
                selectedKey={open?.key ?? null}
              />
              {result.data.nonAdditive && (
                <Typography variant="body2" color="text.secondary">
                  {NON_ADDITIVE_NOTE}
                </Typography>
              )}
            </>
          )}
        </Box>
      </Panel>
      <LensTable lens={lens} query={query} min={min} onOpen={setOpen} />
      <CellPanel
        query={query}
        info={open}
        onClose={() => {
          setOpen(null);
        }}
      />
    </Box>
  );
}
