import FactCheckIcon from '@mui/icons-material/FactCheckOutlined';
import BuildIcon from '@mui/icons-material/BuildOutlined';
import HistoryIcon from '@mui/icons-material/HistoryOutlined';
import { Button, Stack, Typography } from '@mui/material';
import type { QualityRun } from '@/shared/api';
import { IconChip } from '@/shared/ui';
import { LastRunLine } from './LastRunLine';
import { RunChecksButton } from './RunChecksButton';

interface QualityHeaderProps {
  lastRun: QualityRun | null;
  busy: boolean;
  /** The state of the page is not known yet, so starting a run would be a guess. */
  canRun: boolean;
  historyOpen: boolean;
  onRun: () => void;
  onFix: () => void;
  onOpenHistory: () => void;
}

/** The slim header row: title, the last run in words, the Run history button and Run checks. */
export function QualityHeader({ lastRun, busy, canRun, historyOpen, onRun, onFix, onOpenHistory }: QualityHeaderProps) {
  return (
    <Stack direction="row" sx={{ mb: 1.5, columnGap: 2, rowGap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
      <Stack direction="row" spacing={1.25} sx={{ alignItems: 'center' }}>
        <IconChip icon={FactCheckIcon} hue="quality" size="sm" />
        <Typography variant="h1">Data quality</Typography>
      </Stack>
      {lastRun && <LastRunLine run={lastRun} />}
      <Stack direction="row" sx={{ ml: 'auto', gap: 1, alignItems: 'center' }}>
        <Button
          variant="outlined"
          startIcon={<HistoryIcon />}
          aria-haspopup="dialog"
          aria-expanded={historyOpen}
          onClick={onOpenHistory}
        >
          Run history
        </Button>
        <Button variant="outlined" startIcon={<BuildIcon />} disabled={!canRun} aria-haspopup="dialog" onClick={onFix}>
          Fix issues
        </Button>
        <RunChecksButton busy={busy} disabled={!canRun} onRun={onRun} />
      </Stack>
    </Stack>
  );
}
