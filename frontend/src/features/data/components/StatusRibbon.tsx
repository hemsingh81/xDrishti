import MoreIcon from '@mui/icons-material/MoreHoriz';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Button, Popover, Stack } from '@mui/material';
import { Fragment, useId, useState, type ReactNode } from 'react';
import type { DataOverview } from '@/shared/api';
import { tickLook } from '../lib/freshness';
import { AccountPill } from './AccountPill';
import { AutoUpdatePill } from './AutoUpdatePill';
import { CoveredPill } from './CoveredPill';
import { InstrumentsPill } from './InstrumentsPill';
import { LastTick } from './LastTick';
import { RibbonBarsPill } from './RibbonBarsPill';

interface Entry {
  id: string;
  node: ReactNode;
  /** Something here needs the owner (shown on the More button when the item is folded away). */
  attention: boolean;
}

/** How many items stay in view on a phone; the others fold into "More". */
const visibleOnPhone = 3;

function entriesOf(overview: DataOverview): Entry[] {
  const { bars, dhan, eod } = overview;
  const lastRun = eod.lastRun?.status;
  return [
    {
      id: 'tick',
      node: <LastTick bars={bars} nextHoliday={overview.nextHoliday} />,
      attention: tickLook(bars).tone === 'warning',
    },
    { id: 'instruments', node: <InstrumentsPill bars={bars} />, attention: bars.instrumentsBehind > 0 },
    { id: 'bars', node: <RibbonBarsPill bars={bars} />, attention: false },
    { id: 'covered', node: <CoveredPill bars={bars} />, attention: false },
    { id: 'account', node: <AccountPill dhan={dhan} />, attention: !dhan.canFetch },
    {
      id: 'auto',
      node: <AutoUpdatePill eod={eod} dhan={dhan} />,
      attention: eod.enabled && (lastRun === 'failed' || lastRun === 'skipped'),
    },
  ];
}

/**
 * The state of the data in one wrapping row of compact items: the last tick (the one highlighted), instruments, bars,
 * the period covered, the Data account and the automatic update. On a phone the first three stay and the rest are behind
 * "More" (which says so when one of them needs attention).
 */
export function StatusRibbon({ overview }: { overview: DataOverview }) {
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const popoverId = useId();
  const entries = entriesOf(overview);
  const folded = entries.slice(visibleOnPhone);
  const attention = folded.some((entry) => entry.attention);

  return (
    <Box
      component="section"
      aria-label="Data status"
      sx={{ display: 'flex', flexWrap: 'wrap', gap: 1.25, alignItems: 'center', mb: 2, flexShrink: 0 }}
    >
      {entries.slice(0, visibleOnPhone).map((entry) => (
        <Fragment key={entry.id}>{entry.node}</Fragment>
      ))}
      {folded.map((entry) => (
        <Box key={entry.id} sx={{ display: { xs: 'none', sm: 'contents' } }}>
          {entry.node}
        </Box>
      ))}
      <Button
        size="small"
        variant="outlined"
        color={attention ? 'warning' : 'inherit'}
        startIcon={attention ? <WarningIcon /> : <MoreIcon />}
        aria-haspopup="dialog"
        aria-expanded={anchor ? 'true' : undefined}
        aria-controls={anchor ? popoverId : undefined}
        onClick={(event) => {
          setAnchor(event.currentTarget);
        }}
        sx={{ display: { xs: 'inline-flex', sm: 'none' } }}
      >
        {attention ? 'More, needs attention' : 'More'}
      </Button>
      <Popover
        open={anchor !== null}
        anchorEl={anchor}
        onClose={() => {
          setAnchor(null);
        }}
        slotProps={{
          paper: {
            id: popoverId,
            role: 'dialog',
            'aria-label': 'More status',
            sx: { p: 1.5, maxWidth: 'calc(100vw - 32px)' },
          },
        }}
      >
        <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
          {folded.map((entry) => (
            <Box key={entry.id}>{entry.node}</Box>
          ))}
        </Stack>
      </Popover>
    </Box>
  );
}
