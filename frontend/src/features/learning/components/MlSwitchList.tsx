import { Box, Button, Chip, Stack, Typography } from '@mui/material';
import type { MlSettings } from '../api/mlQueries';
import { switchKeys, switchMeaning, switchName, switchTitle, type SwitchKey } from '../lib/mlText';

const stateOf = (settings: MlSettings, key: SwitchKey): boolean =>
  ({
    influence: settings.influenceEnabled,
    autoPromote: settings.autoPromote,
    autoRollback: settings.autoRollback,
    autoHoldOutScore: settings.autoHoldOutScore,
  })[key];

interface MlSwitchListProps {
  settings: MlSettings;
  /** Given: each switch gets a button that asks for the owner act. Absent: the list only reads. */
  onTurn?: (key: SwitchKey, on: boolean) => void;
}

/** The ML switches, ON or OFF in words, with what each means. OFF is the default; no switch places an order. */
export function MlSwitchList({ settings, onTurn }: MlSwitchListProps) {
  return (
    <Box component="ul" aria-label="ML switches" sx={{ m: 0, p: 0, listStyle: 'none', display: 'grid', gap: 1.5 }}>
      {switchKeys.map((key) => {
        const on = stateOf(settings, key);
        return (
          <Box key={key} component="li" sx={{ borderLeft: 3, borderColor: 'divider', pl: 1.5, minWidth: 0 }}>
            <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
              <Typography component="h3" variant="body2" sx={{ fontWeight: 700 }}>
                {switchTitle(key)}
              </Typography>
              <Typography component="span" variant="caption" color="text.secondary">
                {switchName(key)}
              </Typography>
              <Chip size="small" variant="outlined" label={on ? 'ON' : 'OFF'} />
            </Stack>
            <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
              {switchMeaning(key, on)}
            </Typography>
            {onTurn && (
              <Button
                size="small"
                onClick={() => {
                  onTurn(key, !on);
                }}
                sx={{ minHeight: 44 }}
              >
                {on ? `Turn off ${switchTitle(key).toLowerCase()}` : `Turn on ${switchTitle(key).toLowerCase()}`}
              </Button>
            )}
          </Box>
        );
      })}
    </Box>
  );
}
