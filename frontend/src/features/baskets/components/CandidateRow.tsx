import { Checkbox, Chip, FormControlLabel, Stack, Typography } from '@mui/material';
import type { InstrumentSummary } from '@/shared/api';
import { monoFontFamily } from '@/theme';
import { typeLabel } from '../lib/labels';

interface CandidateRowProps {
  instrument: InstrumentSummary;
  checked: boolean;
  /** Already a member of this basket. */
  member: boolean;
  basketName: string;
  onToggle: (id: string) => void;
}

/**
 * One instrument to pick. Already-in-basket and retired ones are shown but cannot be picked, and say why in words (the
 * reason is part of the checkbox's name, so a screen reader hears it too).
 */
export function CandidateRow({ instrument, checked, member, basketName, onToggle }: CandidateRowProps) {
  const retired = instrument.retired && !instrument.tracked.active;
  return (
    <Stack component="li" sx={{ listStyle: 'none', borderBottom: 1, borderColor: 'divider' }}>
      <FormControlLabel
        disabled={member || retired}
        sx={{ m: 0, py: 0.25, pr: 1, alignItems: 'center' }}
        control={
          <Checkbox
            size="small"
            checked={member || checked}
            onChange={() => {
              onToggle(instrument.id);
            }}
          />
        }
        label={
          <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
            <Typography component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600 }}>
              {instrument.symbol}
            </Typography>
            <Typography component="span" color="text.secondary">
              {instrument.displayName}
            </Typography>
            <Chip size="small" variant="outlined" label={typeLabel(instrument.type)} />
            {member && <Chip size="small" label={`Already in ${basketName}`} />}
            {retired && <Chip size="small" label="Retired — cannot be added" />}
            {!member && !retired && (
              <Chip
                size="small"
                variant="outlined"
                color={instrument.tracked.active ? 'success' : 'default'}
                label={instrument.tracked.active ? 'Tracked' : 'Will be tracked'}
              />
            )}
          </Stack>
        }
      />
    </Stack>
  );
}
