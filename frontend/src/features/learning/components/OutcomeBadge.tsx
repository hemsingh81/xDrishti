import CheckIcon from '@mui/icons-material/CheckOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import RemoveIcon from '@mui/icons-material/RemoveCircleOutlineOutlined';
import SavingsIcon from '@mui/icons-material/SavingsOutlined';
import { Box, Chip, Typography } from '@mui/material';
import type { SvgIconComponent } from '@mui/icons-material';
import { outcomeWords, verdictWords, wordsOf, type Words } from '../lib/outcomeText';

const icons: Record<string, SvgIconComponent> = {
  Better: CheckIcon,
  NotBetter: RemoveIcon,
  CheaperAssumption: SavingsIcon,
};

/**
 * An outcome or verdict as an icon, a word and its one-line meaning. Every one is neutral: no green fill for "Better", no
 * red for "Not better" or "No measurable change" (P8-03 decision 2).
 */
export function OutcomeBadge({ name, kind = 'outcome' }: { name: string; kind?: 'outcome' | 'verdict' }) {
  const map: Record<string, Words> = kind === 'outcome' ? outcomeWords : verdictWords;
  const { word, meaning } = wordsOf(map, name);
  const Icon = icons[name] ?? HelpIcon;
  return (
    <Box sx={{ display: 'grid', gap: 0.5, justifyItems: 'start' }}>
      <Chip size="small" variant="outlined" icon={<Icon aria-hidden />} label={word} />
      <Typography variant="caption" color="text.secondary">
        {meaning}
      </Typography>
    </Box>
  );
}
