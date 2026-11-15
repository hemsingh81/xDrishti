import PauseIcon from '@mui/icons-material/Pause';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ReplayIcon from '@mui/icons-material/Replay';
import { Button, IconButton, Paper, Stack, Typography } from '@mui/material';
import { formatIstTime } from '@/shared/lib/format';
import { speeds, type Speed } from '../../hooks/useSimPlayback';

export interface ClockControls {
  minute: number;
  playing: boolean;
  buffering: boolean;
  atEnd: boolean;
  speed: Speed;
  pause: () => void;
  resume: () => void;
  restart: () => void;
  setSpeed: (speed: Speed) => void;
}

const speedLabel = (speed: Speed) => (speed === 'max' ? 'Max' : `${speed}X`);

/** The floating virtual clock of the played day: the market minute, pause or resume, and the speed. */
export function SimClock({ clock, day }: { clock: ClockControls; day: string }) {
  return (
    <Paper
      component="section"
      aria-label="Simulation clock"
      elevation={6}
      sx={{
        position: 'sticky',
        bottom: 16,
        zIndex: 'speedDial',
        justifySelf: 'end',
        p: 1.5,
        borderRadius: 2,
        display: 'grid',
        gap: 1,
        maxWidth: '100%',
      }}
    >
      <Stack direction="row" sx={{ alignItems: 'center', gap: 1 }}>
        <Typography variant="h3" component="p" aria-live="off" sx={{ fontVariantNumeric: 'tabular-nums' }}>
          <time dateTime={new Date(clock.minute).toISOString()}>{formatIstTime(clock.minute)}</time>
        </Typography>
        <Typography variant="caption" color="text.secondary">
          {`${day} · ${clock.atEnd ? 'end of day' : clock.buffering ? 'loading the tape…' : clock.playing ? 'playing' : 'paused'}`}
        </Typography>
        {clock.atEnd ? (
          <IconButton aria-label="Play the day again" onClick={clock.restart} sx={{ minWidth: 44, minHeight: 44 }}>
            <ReplayIcon />
          </IconButton>
        ) : clock.playing ? (
          <IconButton aria-label="Pause" onClick={clock.pause} sx={{ minWidth: 44, minHeight: 44 }}>
            <PauseIcon />
          </IconButton>
        ) : (
          <IconButton aria-label="Resume" onClick={clock.resume} sx={{ minWidth: 44, minHeight: 44 }}>
            <PlayArrowIcon />
          </IconButton>
        )}
      </Stack>
      <Stack direction="row" role="group" aria-label="Speed" sx={{ gap: 0.5, flexWrap: 'wrap' }}>
        {speeds.map((speed) => (
          <Button
            key={String(speed)}
            size="small"
            variant={clock.speed === speed ? 'contained' : 'outlined'}
            aria-pressed={clock.speed === speed}
            onClick={() => {
              clock.setSpeed(speed);
            }}
            sx={{ minHeight: 44, minWidth: 44 }}
          >
            {speedLabel(speed)}
          </Button>
        ))}
      </Stack>
    </Paper>
  );
}
