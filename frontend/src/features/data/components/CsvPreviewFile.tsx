import CheckCircleIcon from '@mui/icons-material/CheckCircleOutlined';
import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import BlockIcon from '@mui/icons-material/BlockOutlined';
import { Accordion, AccordionDetails, AccordionSummary, Box, Chip, Stack, Typography } from '@mui/material';
import type { ReactElement } from 'react';
import type { CsvFilePreview } from '@/shared/api';
import { monoFontFamily } from '@/theme';
import { verdictLabels } from '../lib/csvReview';
import { CsvPreviewDetails } from './CsvPreviewDetails';

const looks: Readonly<Record<string, { color: 'success' | 'error' | 'warning'; icon: ReactElement }>> = {
  pass: { color: 'success', icon: <CheckCircleIcon /> },
  refused: { color: 'error', icon: <BlockIcon /> },
  error: { color: 'warning', icon: <ErrorIcon /> },
};

/** "Passes", "Refused: <why>" or "Error: <why>": the verdict in words, with an icon. */
function Verdict({ file }: { file: CsvFilePreview }) {
  const look = looks[file.verdict];
  const label = verdictLabels[file.verdict] ?? file.verdict;
  return (
    <Chip
      size="small"
      variant="outlined"
      color={look?.color ?? 'default'}
      icon={look?.icon}
      label={file.verdict === 'pass' || !file.message ? label : `${label}: ${file.message}`}
      sx={{ height: 'auto', '& .MuiChip-label': { whiteSpace: 'normal', py: 0.25 } }}
    />
  );
}

/** One file of the preview as a panel: name and verdict in the header; the counts, examples, first rows and the comparison inside. */
export function CsvPreviewFile({ file, expanded }: { file: CsvFilePreview; expanded: boolean }) {
  return (
    <Accordion
      defaultExpanded={expanded}
      disableGutters
      variant="outlined"
      slotProps={{ transition: { unmountOnExit: true } }}
    >
      <AccordionSummary expandIcon={<ExpandMoreIcon />}>
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center', minWidth: 0 }}>
          <Box component="span" sx={{ fontFamily: monoFontFamily, fontWeight: 600, overflowWrap: 'anywhere' }}>
            {file.name}
          </Box>
          <Verdict file={file} />
        </Stack>
      </AccordionSummary>
      <AccordionDetails>
        {file.verdict === 'pass' ? (
          <CsvPreviewDetails file={file} />
        ) : (
          <Typography>
            {file.message ?? 'The file cannot be loaded.'} Nothing would be written for this file.
          </Typography>
        )}
      </AccordionDetails>
    </Accordion>
  );
}
