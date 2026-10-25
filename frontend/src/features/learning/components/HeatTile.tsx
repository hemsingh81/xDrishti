import CheckIcon from '@mui/icons-material/CheckCircleOutlined';
import RemoveIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import { ButtonBase, Typography } from '@mui/material';
import { alpha } from '@mui/material/styles';
import { formatCount, formatR, formatRate, MetricValue } from '@/shared/analytics';
import { numeric } from '@/theme';
import type { CellStats } from '../api/learningQueries';
import { expectancyOf, heat, lowerBoundOf, tileLabel, tintAlpha } from '../lib/cells';

interface HeatTileProps {
  cell: CellStats;
  selected: boolean;
  onOpen: (cellKey: string) => void;
}

/**
 * One stored cell as a button. Enough evidence: the expectancy (sign printed; tinted by that sign), n and the win-rate lower
 * bound. Not enough: hatched, labelled "Not enough", and no expectancy or bound at all (the kit's insufficient state), so a
 * thin cell never reads as a winner. The tint and the hatching are an addition to the words, never the signal.
 */
export function HeatTile({ cell, selected, onOpen }: HeatTileProps) {
  const expectancy = cell.enough ? cell.expectancyR : null;
  // Gain or loss is read from the printed figure: a value that prints "0.00" is neither.
  const sign = expectancy !== null && /[1-9]/.test(formatR(expectancy)) ? Math.sign(expectancy) : 0;
  const Icon = cell.enough ? CheckIcon : RemoveIcon;
  return (
    <ButtonBase
      onClick={() => {
        onOpen(cell.cellKey);
      }}
      aria-label={tileLabel(cell)}
      aria-haspopup="dialog"
      aria-expanded={selected}
      sx={(theme) => ({
        display: 'block',
        width: '100%',
        minWidth: 128,
        minHeight: 44,
        textAlign: 'left',
        p: 1,
        borderRadius: 1,
        border: 1,
        borderColor: selected ? 'primary.main' : 'divider',
        color: 'text.primary',
        fontSize: 'caption.fontSize',
        ...numeric,
        ...(cell.enough
          ? {
              backgroundColor:
                sign === 0
                  ? theme.palette.background.paper
                  : alpha(sign > 0 ? theme.palette.success.main : theme.palette.error.main, tintAlpha(expectancy ?? 0)),
            }
          : {
              backgroundColor: theme.palette.background.default,
              backgroundImage: `repeating-linear-gradient(135deg, transparent 0 6px, ${alpha(theme.palette.text.secondary, heat.hatchAlpha)} 6px 8px)`,
            }),
      })}
    >
      <Typography
        component="span"
        variant="caption"
        sx={{ display: 'flex', alignItems: 'center', gap: 0.5, fontWeight: 700 }}
      >
        <Icon aria-hidden sx={{ fontSize: '1.1em' }} />
        {cell.enough ? 'Enough' : 'Not enough'}
      </Typography>
      <Typography component="span" variant="body2" sx={{ display: 'block', mt: 0.25 }}>
        <MetricValue state={expectancyOf(cell)} format={formatR} />
      </Typography>
      {cell.enough && (
        <Typography component="span" variant="caption" sx={{ display: 'block', color: 'text.primary', ...numeric }}>
          n {formatCount(cell.n)}
          <br />
          Win-rate lower bound <MetricValue state={lowerBoundOf(cell)} format={formatRate} />
        </Typography>
      )}
    </ButtonBase>
  );
}
