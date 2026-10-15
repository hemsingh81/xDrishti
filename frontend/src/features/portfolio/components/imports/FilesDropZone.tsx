import UploadIcon from '@mui/icons-material/UploadFileOutlined';
import { Box, Button, Stack, Typography } from '@mui/material';
import { useId, useState, type ChangeEvent, type DragEvent } from 'react';
import { maxReportFiles } from '../../lib/reportFiles';
import { formatBytes } from '../../lib/tradeFile';

interface FilesDropZoneProps {
  files: readonly File[];
  /** Files were chosen or dropped (they are checked by the caller). */
  onFiles: (files: File[]) => void;
  onRemove: (index: number) => void;
  disabled?: boolean;
  /** Why some files were not added. */
  problems: readonly string[];
}

/**
 * Where the downloaded files go: a bordered area that is a label of the file input, so a click opens the file chooser, files
 * can be dropped on it and the keyboard reaches the (visually hidden) input itself. The chosen files are listed with their
 * size and a Remove button each.
 */
export function FilesDropZone({ files, onFiles, onRemove, disabled = false, problems }: FilesDropZoneProps) {
  const [over, setOver] = useState(false);
  const hintId = useId();

  function onDrop(event: DragEvent) {
    event.preventDefault();
    setOver(false);
    if (!disabled) onFiles([...event.dataTransfer.files]);
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
          borderColor: problems.length > 0 ? 'error.main' : over ? 'primary.main' : 'divider',
          bgcolor: over ? 'action.hover' : 'transparent',
          cursor: disabled ? 'default' : 'pointer',
          '&:focus-within': { outline: 2, outlineStyle: 'solid', outlineColor: 'primary.main', outlineOffset: 2 },
        }}
      >
        <UploadIcon color="action" aria-hidden />
        <Box sx={{ minWidth: 0, flex: 1 }}>
          <Typography sx={{ fontWeight: 600 }}>
            {over ? 'Drop the files to choose them' : 'Drop the files here, or click to choose them'}
          </Typography>
          <Typography variant="body2" color="text.secondary" id={hintId}>
            {`CSV, XLS, XLSX or text; the format is recognised from the content, not the name. Up to ${String(maxReportFiles)} files, 10 MB each and 40 MB together. The files are read, never kept: only their names and a fingerprint are.`}
          </Typography>
        </Box>
        <Box
          component="input"
          type="file"
          multiple
          aria-label="Report files (CSV, XLS or XLSX)"
          aria-describedby={hintId}
          disabled={disabled}
          onChange={(event: ChangeEvent<HTMLInputElement>) => {
            const chosen = event.target.files ? [...event.target.files] : [];
            if (chosen.length > 0) onFiles(chosen);
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
      {files.length > 0 && (
        <Stack component="ul" aria-label="Chosen files" spacing={0.5} sx={{ m: 0, mt: 1, p: 0, listStyle: 'none' }}>
          {files.map((file, index) => (
            <Stack
              component="li"
              key={`${file.name}-${String(file.size)}`}
              direction="row"
              sx={{ alignItems: 'center', gap: 1, flexWrap: 'wrap' }}
            >
              <Typography variant="body2" sx={{ overflowWrap: 'anywhere', fontWeight: 600 }}>
                {file.name}
              </Typography>
              <Typography variant="body2" color="text.secondary">
                {formatBytes(file.size)}
              </Typography>
              <Button
                size="small"
                disabled={disabled}
                aria-label={`Remove ${file.name}`}
                onClick={() => {
                  onRemove(index);
                }}
              >
                Remove
              </Button>
            </Stack>
          ))}
        </Stack>
      )}
      {problems.length > 0 && (
        <Stack component="ul" role="alert" spacing={0.25} sx={{ m: 0, mt: 0.75, p: 0, listStyle: 'none' }}>
          {problems.map((problem) => (
            <Typography component="li" key={problem} variant="body2" color="error">
              {problem}
            </Typography>
          ))}
        </Stack>
      )}
    </Box>
  );
}
