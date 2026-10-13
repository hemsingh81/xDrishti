import {
  Box,
  Button,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableContainer,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useState } from 'react';
import { numeric } from '@/theme';
import type { ChartSeries } from './types';

/** Dates per page: a table of a few hundred rows is paged, the newest first. */
export const chartTablePageSize = 20;

interface ChartTableProps {
  label: string;
  dates: readonly string[];
  series: readonly ChartSeries[];
  valueFormatter: (value: number) => string;
  dateFormatter: (date: string) => string;
  id: string;
}

/** The data behind the chart as a table (newest date first, paged): the alternative to the picture. */
export function ChartTable({ label, dates, series, valueFormatter, dateFormatter, id }: ChartTableProps) {
  const [page, setPage] = useState(0);
  const pages = Math.max(1, Math.ceil(dates.length / chartTablePageSize));
  const current = Math.min(page, pages - 1);
  const newestFirst = [...dates].reverse();
  const rows = newestFirst.slice(current * chartTablePageSize, (current + 1) * chartTablePageSize);
  const columns = series.map((s) => ({ id: s.id, name: s.name, values: new Map(s.data) }));
  const from = dates.length === 0 ? 0 : current * chartTablePageSize + 1;
  const to = Math.min(dates.length, (current + 1) * chartTablePageSize);

  return (
    <Box id={id} sx={{ mt: 1.5 }}>
      <TableContainer sx={{ border: 1, borderColor: 'divider', borderRadius: 1, maxHeight: 360 }}>
        <Table size="small" stickyHeader aria-label={`${label}, data table`}>
          <TableHead>
            <TableRow>
              <TableCell>Date</TableCell>
              {columns.map((c) => (
                <TableCell key={c.id} align="right">
                  {c.name}
                </TableCell>
              ))}
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((date) => (
              <TableRow key={date}>
                <TableCell component="th" scope="row" sx={{ whiteSpace: 'nowrap' }}>
                  {dateFormatter(date)}
                </TableCell>
                {columns.map((c) => {
                  const value = c.values.get(date);
                  return (
                    <TableCell key={c.id} align="right" sx={numeric}>
                      {value === null || value === undefined ? '—' : valueFormatter(value)}
                    </TableCell>
                  );
                })}
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <Stack direction="row" sx={{ mt: 0.5, alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
        <Typography variant="caption" color="text.secondary" role="status">
          {`Dates ${String(from)}–${String(to)} of ${String(dates.length)}, newest first`}
        </Typography>
        <Stack direction="row" sx={{ gap: 0.5 }}>
          <Button
            size="small"
            disabled={current === 0}
            onClick={() => {
              setPage(current - 1);
            }}
          >
            Newer
          </Button>
          <Button
            size="small"
            disabled={current >= pages - 1}
            onClick={() => {
              setPage(current + 1);
            }}
          >
            Older
          </Button>
        </Stack>
      </Stack>
    </Box>
  );
}
