import UploadIcon from '@mui/icons-material/UploadFileOutlined';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useId, useState, type ChangeEvent, type DragEvent } from 'react';
import { capitalise } from '@/shared/lib/format';
import { formatBytes } from '../../lib/tradeFile';

interface FileDropZoneProps {
  file: File | null;
  /** A file was chosen or dropped (it is checked by the caller). */
  onFile: (file: File) => void;
  onClear: () => void;
  disabled?: boolean;
  /** Why the file cannot be used, or what the API said about it. */
  error?: string;
  /** What the file is, for the input's name and the prompt: "tradebook" (default) or "dividend list". */
  what?: string;
}

/**
 * Where the tradebook goes: a bordered area that is a label of the file input, so a click opens the file chooser, a file can
 * be dropped on it and the keyboard reaches the (visually hidden) input itself. The chosen file is named with its size.
 */
export function FileDropZone({
  file,
  onFile,
  onClear,
  disabled = false,
  error,
  what = 'tradebook',
}: FileDropZoneProps) {
  const [over, setOver] = useState(false);
  const hintId = useId();

  function onDrop(event: DragEvent) {
    event.preventDefault();
    setOver(false);
    const dropped = event.dataTransfer.files[0];
    if (dropped && !disabled) onFile(dropped);
  }

  return (
    <Box>
      <Box
        component="label"
        onDragOver={(event: DragEvent) => {
          event.preventDefault();
          if (!disabled) setOver(true);
        }}
        onDragLeave={() => {
          setOver(false);
        }}
        onDrop={onDrop}
        sx={{
          position: 'relative',
          display: 'flex',
          alignItems: 'center',
          gap: 1.5,
          p: 2,
          border: 2,
          borderStyle: 'dashed',
          borderRadius: 1,
          borderColor: error ? 'error.main' : over ? 'primary.main' : 'divider',
          bgcolor: over ? 'action.hover' : 'transparent',
          cursor: disabled ? 'default' : 'pointer',
          '&:focus-within': { outline: 2, outlineStyle: 'solid', outlineColor: 'primary.main', outlineOffset: 2 },
        }}
      >
        <UploadIcon color="action" aria-hidden />
        <Box sx={{ minWidth: 0, flex: 1 }}>
          <Typography sx={{ fontWeight: 600 }}>
            {over ? 'Drop the file to choose it' : `Drop the ${what} here, or click to choose a file`}
          </Typography>
          <Typography variant="body2" color="text.secondary" id={hintId}>
            CSV, up to 10 MB and 20,000 rows. The file is read, never kept: only its name and a fingerprint are.
          </Typography>
        </Box>
        <Box
          component="input"
          type="file"
          accept=".csv,.txt,text/csv,text/plain"
          aria-label={`${capitalise(what)} file (CSV)`}
          aria-describedby={hintId}
          disabled={disabled}
          onChange={(event: ChangeEvent<HTMLInputElement>) => {
            const chosen = event.target.files?.[0];
            if (chosen) onFile(chosen);
            // The same file chosen again is a change too.
            event.target.value = '';
          }}
          sx={{
            position: 'absolute',
            width: '1px',
            height: '1px',
            opacity: 0,
            overflow: 'hidden',
            clip: 'rect(0 0 0 0)',
          }}
        />
      </Box>
      {file && (
        <Stack direction="row" sx={{ alignItems: 'center', gap: 1, mt: 1, flexWrap: 'wrap' }}>
          <Typography variant="body2" sx={{ overflowWrap: 'anywhere', fontWeight: 600 }}>
            {file.name}
          </Typography>
          <Typography variant="body2" color="text.secondary">
            {formatBytes(file.size)}
          </Typography>
          <Button size="small" onClick={onClear} disabled={disabled} aria-label={`Remove ${file.name}`}>
            Remove
          </Button>
        </Stack>
      )}
      {error && (
        <Typography variant="body2" color="error" role="alert" sx={{ mt: 0.75 }}>
          {error}
        </Typography>
      )}
    </Box>
  );
}
