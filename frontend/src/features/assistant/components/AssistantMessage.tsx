import ErrorIcon from '@mui/icons-material/ErrorOutlineOutlined';
import { Box, Typography } from '@mui/material';
import { checkSpans, type Span } from '../lib/markSpans';
import { GroundingBanner } from './GroundingBanner';
import { PlainText } from './PlainText';
import { ToolCallChips, type ChipCall } from './ToolCallChips';

export interface AssistantMessageProps {
  text: string;
  calls: readonly ChipCall[];
  /** The grounding status and its spans; null while the answer is still arriving. */
  grounding: { status: string; spans: readonly (Span & { text: string })[] } | null;
  /** A line under the answer: stopped, incomplete (connection closed) or the error sentence. */
  notes: readonly string[];
  /** Text of a running turn is shown dimmed until `done` (it may still be discarded). */
  streaming?: boolean;
  /** "Reading", "Answering" ... while the turn runs. */
  working?: string | null;
}

/** The assistant's answer above its tool calls: text, marks, the check result. Everything is text. */
export function AssistantMessage({
  text,
  calls,
  grounding,
  notes,
  streaming = false,
  working = null,
}: AssistantMessageProps) {
  const checked = checkSpans(text, grounding?.spans ?? []);
  return (
    <Box
      component="article"
      aria-label="Assistant answer"
      sx={{
        alignSelf: 'flex-start',
        maxWidth: '100%',
        minWidth: 0,
        width: '100%',
        border: 1,
        borderColor: 'divider',
        bgcolor: 'background.paper',
        borderRadius: 2,
        px: 1.5,
        py: 1,
      }}
    >
      {working && (
        <Typography variant="body2" color="text.secondary" sx={{ mb: 0.5 }}>
          {working}
        </Typography>
      )}
      <Box sx={{ opacity: streaming ? 0.8 : 1 }}>
        <PlainText text={text} spans={checked.valid} />
      </Box>
      {notes.map((note) => (
        <Box key={note} sx={{ display: 'flex', gap: 0.75, alignItems: 'center', mt: 0.5, color: 'warning.main' }}>
          <ErrorIcon aria-hidden fontSize="small" />
          <Typography variant="body2" sx={{ color: 'inherit' }}>
            {note}
          </Typography>
        </Box>
      ))}
      {grounding && (
        <GroundingBanner status={grounding.status} count={checked.valid.length} notLocated={checked.dropped} />
      )}
      <ToolCallChips calls={calls} />
    </Box>
  );
}
