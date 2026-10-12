import BlockIcon from '@mui/icons-material/BlockOutlined';
import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Chip, Popover, Typography } from '@mui/material';
import { useId, useState, type MouseEvent } from 'react';
import type { QualityOverview } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import { gateLook } from '../lib/summary';

const icons = { error: <BlockIcon />, success: <CheckCircleIcon />, warning: <WarningIcon /> } as const;

/**
 * The fail-closed gate for the nightly plan as a chip (an icon and a word: OK, Blocked, Open with failures). Pressing it
 * opens a popover with why it is not OK and how many instruments fail against the limit.
 */
export function GateChip({ gate }: { gate: QualityOverview['gate'] }) {
  const titleId = useId();
  const popoverId = useId();
  const [anchor, setAnchor] = useState<HTMLElement | null>(null);
  const look = gateLook(gate);
  const open = (event: MouseEvent<HTMLElement>) => {
    setAnchor(event.currentTarget);
  };

  return (
    <>
      <Chip
        size="small"
        variant="outlined"
        color={look.tone}
        icon={icons[look.tone]}
        label={`Gate: ${look.word}`}
        onClick={open}
        aria-haspopup="dialog"
        aria-expanded={anchor !== null}
        aria-controls={anchor ? popoverId : undefined}
      />
      <Popover
        id={popoverId}
        open={anchor !== null}
        anchorEl={anchor}
        onClose={() => {
          setAnchor(null);
        }}
        anchorOrigin={{ vertical: 'bottom', horizontal: 'left' }}
        slotProps={{ paper: { role: 'dialog', 'aria-labelledby': titleId, sx: { maxWidth: 420, p: 2 } } }}
      >
        <Typography id={titleId} variant="h2" component="h2" sx={{ color: `${look.tone}.main` }}>
          Gate: {look.word}
        </Typography>
        {gate.reasons.length > 0 && (
          <Box component="ul" aria-label="Why the gate is not OK" sx={{ mt: 1, mb: 0, pl: 2.5 }}>
            {gate.reasons.map((reason) => (
              <li key={reason}>
                <Typography variant="body2">{reason}</Typography>
              </li>
            ))}
          </Box>
        )}
        <Typography variant="body2" color="text.secondary" sx={{ mt: 1, ...numeric }}>
          {formatNumber(gate.failedInstruments)} of {formatNumber(gate.checkedInstruments)} checked instruments fail
          (the gate blocks the nightly plan above {formatNumber(gate.maxFailedPercent)} %). An instrument that fails is
          skipped by the nightly scan until it is fixed.
        </Typography>
      </Popover>
    </>
  );
}
