import { Box, Chip, Typography } from '@mui/material';
import type { ReactNode } from 'react';
import type { CorporateAction } from '@/shared/api';
import { formatDate } from '@/shared/lib/format';
import type { GridColumn } from '@/shared/ui/grid';
import { monoFontFamily } from '@/theme';
import { actionSourceLabel, factorText, kindLabel } from '../lib/actions';
import { ActionStatusChip } from './ActionStatusChip';

/**
 * The columns of the corporate actions list: instrument (symbol over name), ex-date, kind, ratio, price factor, source,
 * state and note; then the buttons of the row. The factor and source are hidden on narrow screens, the note on
 * medium ones.
 */
export function actionColumns(actions: (action: CorporateAction) => ReactNode): readonly GridColumn<CorporateAction>[] {
  return [
    {
      id: 'instrument',
      header: 'Instrument',
      minWidth: 170,
      render: (action) => (
        <Box sx={{ minWidth: 0 }}>
          <Typography component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600, display: 'block' }}>
            {action.symbol}
          </Typography>
          <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>
            {action.name}
          </Typography>
        </Box>
      ),
    },
    {
      id: 'exDate',
      header: 'Ex-date',
      minWidth: 120,
      render: (action) => <time dateTime={action.exDate}>{formatDate(action.exDate)}</time>,
    },
    {
      id: 'kind',
      header: 'Kind',
      minWidth: 90,
      render: (action) => <Chip size="small" variant="outlined" label={kindLabel(action.kind)} />,
    },
    { id: 'ratio', header: 'Ratio', minWidth: 70, numeric: true, accessor: (action) => action.ratio },
    {
      id: 'factor',
      header: 'Price factor',
      minWidth: 100,
      numeric: true,
      hideBelow: 'sm',
      accessor: (action) => factorText(action.factor),
    },
    {
      id: 'source',
      header: 'Source',
      minWidth: 100,
      hideBelow: 'md',
      render: (action) => <Chip size="small" variant="outlined" label={actionSourceLabel(action.source)} />,
    },
    { id: 'status', header: 'Status', minWidth: 130, render: (action) => <ActionStatusChip status={action.status} /> },
    {
      id: 'note',
      header: 'Note',
      grow: true,
      minWidth: 160,
      hideBelow: 'xl',
      accessor: (action) => action.note ?? '—',
    },
    { id: 'actions', header: 'Actions', kind: 'handle', align: 'right', minWidth: 290, render: actions },
  ];
}
