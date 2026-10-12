import { Stack, Typography } from '@mui/material';
import type { CsvPreviewResult } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { previewTally } from '../lib/csvReview';
import { CsvPreviewFile } from './CsvPreviewFile';

/** The preview of the checked files: a live line with the tally, then a panel per file (those that fail are open). */
export function CsvPreviewResults({ result }: { result: CsvPreviewResult }) {
  const { files } = result;
  return (
    <Stack component="section" aria-label="Preview results" spacing={1}>
      <Typography variant="h3">Preview</Typography>
      <Typography variant="body2" color="text.secondary">
        {formatNumber(files.length)} {files.length === 1 ? 'file' : 'files'} read, nothing was written:{' '}
        {previewTally(files)}.
      </Typography>
      {files.map((file) => (
        <CsvPreviewFile key={file.name} file={file} expanded={files.length <= 3 || file.verdict !== 'pass'} />
      ))}
    </Stack>
  );
}
