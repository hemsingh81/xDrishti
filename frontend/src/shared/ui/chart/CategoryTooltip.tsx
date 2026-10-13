import { Box, Paper, Stack, Typography } from '@mui/material';
import { numeric } from '@/theme';
import { categoryTotal, shareText, sliceTotal } from './categoryData';
import type { CategoryData, CategoryHover } from './categoryTypes';

interface CategoryTooltipProps {
  data: CategoryData;
  hover: CategoryHover;
  colors: readonly string[];
  valueFormatter: (value: number) => string;
}

function Row({ color, name, value }: { color: string | undefined; name: string; value: string }) {
  return (
    <Stack direction="row" sx={{ justifyContent: 'space-between', alignItems: 'center', gap: 2 }}>
      <Stack direction="row" sx={{ alignItems: 'center', gap: 0.75 }}>
        <Box component="span" sx={{ width: 8, height: 8, borderRadius: '2px', bgcolor: color, flex: 'none' }} />
        <Typography variant="caption">{name}</Typography>
      </Stack>
      <Typography variant="caption" sx={{ fontWeight: 600, ...numeric }}>
        {value}
      </Typography>
    </Stack>
  );
}

/**
 * The tooltip of the chart, next to the pointer: a slice's value and share, or a bar's total and its split. Plain React over
 * the canvas, so it takes the theme's colours and the caller's formatters. It is decoration (the table has the same
 * figures), so it is hidden from assistive technology and never takes the pointer.
 */
export function CategoryTooltip({ data, hover, colors, valueFormatter }: CategoryTooltipProps) {
  let title: string;
  let rows: { key: string; color: string | undefined; name: string; value: string }[];
  if (data.kind === 'donut') {
    const slice = data.slices[hover.index];
    if (!slice) return null;
    title = slice.name;
    rows = [
      {
        key: slice.id,
        color: colors[slice.color],
        name: shareText(slice.value, sliceTotal(data.slices)),
        value: valueFormatter(slice.value),
      },
    ];
  } else {
    const category = data.categories[hover.index];
    if (!category) return null;
    title = `${category.name} · ${valueFormatter(categoryTotal(data.stacks, hover.index))}`;
    rows = data.stacks
      .filter((s) => (s.values[hover.index] ?? 0) > 0)
      .map((s) => ({
        key: s.id,
        color: colors[s.color],
        name: s.name,
        value: valueFormatter(s.values[hover.index] ?? 0),
      }));
  }
  const left = hover.x > hover.width / 2;
  return (
    <Paper
      aria-hidden
      elevation={3}
      sx={{
        position: 'absolute',
        top: hover.y,
        left: hover.x,
        transform: left ? 'translate(calc(-100% - 12px), -50%)' : 'translate(12px, -50%)',
        px: 1.25,
        py: 0.75,
        minWidth: 140,
        pointerEvents: 'none',
        zIndex: 1,
        border: 1,
        borderColor: 'divider',
      }}
    >
      <Typography variant="caption" sx={{ fontWeight: 600, display: 'block', mb: 0.25 }}>
        {title}
      </Typography>
      {rows.map((row) => (
        <Row key={row.key} color={row.color} name={row.name} value={row.value} />
      ))}
    </Paper>
  );
}
