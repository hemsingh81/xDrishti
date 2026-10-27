import BlockIcon from '@mui/icons-material/BlockOutlined';
import HourglassIcon from '@mui/icons-material/HourglassEmptyOutlined';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useEffect, useState } from 'react';
import type { Gate } from '../lib/gate';
import { words } from '../lib/wording';

function Elapsed() {
  const [since] = useState(() => Date.now());
  const [seconds, setSeconds] = useState(0);
  useEffect(() => {
    const id = setInterval(() => {
      setSeconds(Math.floor((Date.now() - since) / 1000));
    }, 1000);
    return () => {
      clearInterval(id);
    };
  }, [since]);
  return <Typography variant="body2">{seconds} seconds since first seen</Typography>;
}

/** The honest state of the assistant: icon + words + reason, and what to do. Draws no number except the elapsed seconds. */
export function StatusBanner({ gate, onTurnOn }: { gate: Gate; onTurnOn: () => void }) {
  if (gate.kind === 'ready') return null;
  // Busy and nightly only disable the composer (its reason is shown there).
  if (gate.kind === 'busy' || gate.kind === 'nightly') return null;
  const Icon = gate.kind === 'loading' ? HourglassIcon : BlockIcon;
  return (
    <Box
      role="status"
      sx={{ border: 1, borderColor: 'warning.main', borderRadius: 2, p: 2, mb: 2, bgcolor: 'background.paper' }}
    >
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <Icon aria-hidden color="warning" />
        <Typography variant="h3" component="h2">
          {gate.title}
        </Typography>
      </Stack>
      {gate.help && <Typography sx={{ mt: 1 }}>{gate.help}</Typography>}
      {gate.command && (
        <Stack direction="row" spacing={1} sx={{ mt: 1, alignItems: 'center' }}>
          <Box
            component="pre"
            tabIndex={0}
            aria-label="Command"
            sx={{ m: 0, p: 1, bgcolor: 'action.hover', borderRadius: 1, overflow: 'auto', flex: 1 }}
          >
            <code>{gate.command}</code>
          </Box>
          <Button
            size="small"
            onClick={() => {
              void (globalThis.navigator.clipboard as Clipboard | undefined)?.writeText(gate.command ?? '');
            }}
          >
            Copy
          </Button>
        </Stack>
      )}
      {gate.kind === 'loading' && <Elapsed />}
      {gate.kind === 'off' && (
        <Button variant="contained" sx={{ mt: 1.5 }} onClick={onTurnOn}>
          {words.turnOn}
        </Button>
      )}
    </Box>
  );
}
