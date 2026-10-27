import { Box } from '@mui/material';
import { visuallyHidden } from '@mui/utils';
import { markSpans, type Span } from '../lib/markSpans';
import { words } from '../lib/wording';

/**
 * A piece of the answer with the figures the server did not find in a tool result marked: a dotted underline AND the hidden
 * words "(not from a tool)", so the mark never depends on colour. `offset` is where the piece starts in the raw answer.
 */
export function MarkedText({ text, offset, spans }: { text: string; offset: number; spans: readonly Span[] }) {
  const shifted = spans.map((s) => ({ start: s.start - offset, end: s.end - offset }));
  return (
    <>
      {markSpans(text, shifted).map((part, i) =>
        part.marked ? (
          <Box
            component="mark"
            key={i}
            sx={{
              bgcolor: 'transparent',
              color: 'inherit',
              textDecoration: 'underline dotted',
              textDecorationThickness: '2px',
              textUnderlineOffset: '3px',
            }}
          >
            {part.text}
            <Box component="span" sx={visuallyHidden}>
              {' '}
              {words.notFromTool}
            </Box>
          </Box>
        ) : (
          part.text
        ),
      )}
    </>
  );
}
