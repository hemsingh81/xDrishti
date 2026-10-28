import CheckIcon from '@mui/icons-material/CheckCircleOutlineOutlined';
import HelpIcon from '@mui/icons-material/HelpOutlineOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmptyOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Chip, Stack, Typography } from '@mui/material';
import type { LiveVsPaperView } from '../api/reportQueries';
import { intervalText, r, verdictWords } from '../lib/reportLabels';

const verdictIcons = {
  within: CheckIcon,
  not_within: WarningIcon,
  inconclusive: HelpIcon,
  insufficient: HourglassIcon,
};

function Tile({ label, children }: { label: string; children: string }) {
  return (
    <Stack sx={{ minWidth: 150, gap: 0.25 }}>
      <Typography variant="caption" color="text.secondary">
        {label}
      </Typography>
      <Typography variant="h3" component="p">
        {children}
      </Typography>
    </Stack>
  );
}

/** The tiles: pairs, the mean shortfall with its interval as text, the verdict as icon and word, and the entry and exit parts. */
export function ReportSummary({ view }: { view: LiveVsPaperView }) {
  const s = view.summary;
  const Icon = (verdictIcons as Record<string, typeof HelpIcon | undefined>)[s.verdict] ?? HelpIcon;
  return (
    <Stack component="section" aria-label="Summary" sx={{ gap: 1 }}>
      <Stack direction="row" sx={{ gap: 3, flexWrap: 'wrap', alignItems: 'flex-end' }}>
        <Tile label="Pairs">{String(s.nPairs)}</Tile>
        <Tile label="Mean shortfall (R, positive is live worse)">{r(s.mean)}</Tile>
        <Tile label="95 % interval">{intervalText(s.lo95, s.hi95)}</Tile>
        <Tile label="Entry part (R)">{r(s.entryDiffMean)}</Tile>
        <Tile label="Exit part (R)">{r(s.exitDiffMean)}</Tile>
        <Tile label="Charges part (R)">{r(s.chargesDiffMean)}</Tile>
        <Chip
          variant="outlined"
          color={s.verdict === 'within' ? 'success' : s.verdict === 'not_within' ? 'warning' : 'default'}
          icon={<Icon />}
          label={`Verdict: ${verdictWords[s.verdict] ?? s.verdict}`}
        />
      </Stack>
      <Typography variant="body2" color="text.secondary">
        {`Margin ${s.threshold.toFixed(2)} R on the one-sided 95 % upper bound; a verdict needs ${String(s.minPairs)} pairs. Every figure here has its n; under the minimum it says so.`}
      </Typography>
    </Stack>
  );
}
