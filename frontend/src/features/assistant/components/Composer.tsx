import SendIcon from '@mui/icons-material/SendOutlined';
import StopIcon from '@mui/icons-material/StopCircleOutlined';
import { Box, Button, Stack, TextField, Typography } from '@mui/material';
import { useEffect, type Ref } from 'react';
import { words } from '../lib/wording';

interface Props {
  value: string;
  onChange: (value: string) => void;
  onSend: () => void;
  onStop: () => void;
  running: boolean;
  /** Why asking is not possible now, as visible text; null when it is. */
  disabledReason: string | null;
  inputRef?: Ref<HTMLTextAreaElement>;
}

/** The question box: Enter sends, Shift+Enter is a newline, Stop (or Esc) cancels a running turn. */
export function Composer({ value, onChange, onSend, onStop, running, disabledReason, inputRef }: Props) {
  const tooLong = value.length > words.maxChars;
  const blocked = disabledReason !== null || running;
  const canSend = !blocked && value.trim().length > 0 && !tooLong;
  useEffect(() => {
    if (!running) return undefined;
    const onKey = (event: KeyboardEvent) => {
      if (event.key === 'Escape') onStop();
    };
    globalThis.addEventListener('keydown', onKey);
    return () => {
      globalThis.removeEventListener('keydown', onKey);
    };
  }, [running, onStop]);

  return (
    <Box
      sx={{
        position: 'sticky',
        bottom: 0,
        bgcolor: 'background.default',
        pt: 1,
        pb: 1,
        borderTop: 1,
        borderColor: 'divider',
      }}
    >
      {disabledReason && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.5 }}>
          {disabledReason}
        </Typography>
      )}
      <Stack direction="row" spacing={1} sx={{ alignItems: 'flex-end' }}>
        <TextField
          label={words.composerLabel}
          value={value}
          onChange={(event) => {
            onChange(event.target.value);
          }}
          onKeyDown={(event) => {
            if (event.key === 'Enter' && !event.shiftKey && !event.nativeEvent.isComposing) {
              event.preventDefault();
              if (canSend) onSend();
            }
          }}
          multiline
          minRows={1}
          maxRows={6}
          fullWidth
          disabled={blocked}
          inputRef={inputRef}
          error={tooLong}
          helperText={
            value.length > words.maxChars - 200 ? `${value.length} of ${words.maxChars} characters` : undefined
          }
        />
        {running ? (
          <Button variant="outlined" color="warning" startIcon={<StopIcon />} onClick={onStop}>
            Stop
          </Button>
        ) : (
          <Button variant="contained" startIcon={<SendIcon />} disabled={!canSend} onClick={onSend}>
            Send
          </Button>
        )}
      </Stack>
    </Box>
  );
}
