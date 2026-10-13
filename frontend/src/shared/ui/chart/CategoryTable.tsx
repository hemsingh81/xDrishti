import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow } from '@mui/material';
import { numeric } from '@/theme';
import { categoryTotal, shareText, sliceTotal } from './categoryData';
import type { CategoryData } from './categoryTypes';

interface CategoryTableProps {
  id: string;
  label: string;
  data: CategoryData;
  valueFormatter: (value: number) => string;
}

/** The data behind the chart as a table: the alternative to the picture. Names, not colours, tell the rows apart. */
export function CategoryTable({ id, label, data, valueFormatter }: CategoryTableProps) {
  return (
    <Box id={id} sx={{ mt: 1.5 }}>
      <TableContainer sx={{ border: 1, borderColor: 'divider', borderRadius: 1, maxHeight: 360 }}>
        <Table size="small" stickyHeader aria-label={`${label}, data table`}>
          {data.kind === 'donut' ? (
            <DonutRows data={data} format={valueFormatter} />
          ) : (
            <StackRows data={data} format={valueFormatter} />
          )}
        </Table>
      </TableContainer>
    </Box>
  );
}

function DonutRows({
  data,
  format,
}: {
  data: Extract<CategoryData, { kind: 'donut' }>;
  format: (v: number) => string;
}) {
  const total = sliceTotal(data.slices);
  return (
    <>
      <TableHead>
        <TableRow>
          <TableCell>Name</TableCell>
          <TableCell align="right">Value</TableCell>
          <TableCell align="right">Share</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {data.slices.map((s) => (
          <TableRow key={s.id}>
            <TableCell component="th" scope="row">
              {s.name}
            </TableCell>
            <TableCell align="right" sx={numeric}>
              {format(s.value)}
            </TableCell>
            <TableCell align="right" sx={numeric}>
              {shareText(s.value, total)}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </>
  );
}

function StackRows({
  data,
  format,
}: {
  data: Extract<CategoryData, { kind: 'stacked' }>;
  format: (v: number) => string;
}) {
  return (
    <>
      <TableHead>
        <TableRow>
          <TableCell />
          {data.stacks.map((s) => (
            <TableCell key={s.id} align="right">
              {s.name}
            </TableCell>
          ))}
          <TableCell align="right">Total</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {data.categories.map((category, index) => (
          <TableRow key={category.id}>
            <TableCell component="th" scope="row">
              {category.name}
            </TableCell>
            {data.stacks.map((s) => {
              const value = s.values[index] ?? 0;
              return (
                <TableCell key={s.id} align="right" sx={numeric}>
                  {value > 0 ? format(value) : '—'}
                </TableCell>
              );
            })}
            <TableCell align="right" sx={{ fontWeight: 600, ...numeric }}>
              {format(categoryTotal(data.stacks, index))}
            </TableCell>
          </TableRow>
        ))}
      </TableBody>
    </>
  );
}
