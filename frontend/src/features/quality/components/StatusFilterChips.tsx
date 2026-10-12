import { Box, Chip } from '@mui/material';
import type { QualityCounts } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { qualityStatuses, type QualityStatus } from '../lib/filters';
import { ribbonLabels, statusColor } from '../lib/labels';
import { StatusIcon } from './StatusIcon';

interface StatusFilterChipsProps {
  counts: QualityCounts;
  /** The status the table is filtered to; null = all. */
  value: QualityStatus | null;
  onChange: (status: QualityStatus | null) => void;
}

const countOf = (counts: QualityCounts, status: QualityStatus): number =>
  ({ pass: counts.passed, warning: counts.warned, fail: counts.failed, notChecked: counts.notChecked })[status];

/**
 * The instruments by result as chips that filter the table: All, Passed, Warnings, Failed, Not checked, each with its
 * count, an icon and a word. A chip is a toggle (`aria-pressed`): pressing the active one shows all again.
 */
export function StatusFilterChips({ counts, value, onChange }: StatusFilterChipsProps) {
  return (
    <Box role="group" aria-label="Filter by result" sx={{ display: 'flex', flexWrap: 'wrap', gap: 0.75 }}>
      <Chip
        size="small"
        label={<ChipLabel word="All" count={counts.tracked} />}
        variant={value === null ? 'filled' : 'outlined'}
        color={value === null ? 'primary' : 'default'}
        aria-pressed={value === null}
        onClick={() => {
          onChange(null);
        }}
      />
      {qualityStatuses.map((status) => {
        const pressed = value === status;
        return (
          <Chip
            key={status}
            size="small"
            color={statusColor(status)}
            variant={pressed ? 'filled' : 'outlined'}
            icon={<StatusIcon status={status} fontSize="inherit" />}
            label={<ChipLabel word={ribbonLabels[status]} count={countOf(counts, status)} />}
            aria-pressed={pressed}
            onClick={() => {
              onChange(pressed ? null : status);
            }}
          />
        );
      })}
    </Box>
  );
}

function ChipLabel({ word, count }: { word: string; count: number }) {
  return (
    <>
      {word}{' '}
      <Box component="span" sx={{ fontWeight: 700, ...numeric }}>
        {formatNumber(count)}
      </Box>
    </>
  );
}
