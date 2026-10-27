import { Box, Typography } from '@mui/material';
import { Fragment } from 'react';
import { parseBlocks, parseInline, type Line } from '../lib/plainText';
import type { Span } from '../lib/markSpans';
import { MarkedText } from './MarkedText';

function Inline({ line, spans }: { line: Line; spans: readonly Span[] }) {
  return (
    <>
      {parseInline(line).map((piece, i) => {
        const inner = <MarkedText text={piece.text} offset={piece.offset} spans={spans} />;
        return piece.bold ? <strong key={i}>{inner}</strong> : <Fragment key={i}>{inner}</Fragment>;
      })}
    </>
  );
}

/**
 * Model text as text. Only paragraphs, `- ` and `1.` lists and `**bold**` get structure; HTML, links, images and tables are
 * never interpreted (React escapes every string; no raw-HTML injection API is used anywhere in this feature). Long unbroken tokens wrap.
 */
export function PlainText({ text, spans = [] }: { text: string; spans?: readonly Span[] }) {
  return (
    <Box sx={{ overflowWrap: 'anywhere', '& > :last-child': { mb: 0 } }}>
      {parseBlocks(text).map((block, i) =>
        block.kind === 'p' ? (
          <Typography key={i} variant="body1" sx={{ mb: 1, whiteSpace: 'pre-wrap' }}>
            {block.lines.map((line, j) => (
              <Fragment key={j}>
                {j > 0 && '\n'}
                <Inline line={line} spans={spans} />
              </Fragment>
            ))}
          </Typography>
        ) : (
          <Box key={i} component={block.kind} sx={{ mt: 0, mb: 1, pl: 3 }}>
            {block.items.map((line, j) => (
              <li key={j}>
                <Inline line={line} spans={spans} />
              </li>
            ))}
          </Box>
        ),
      )}
    </Box>
  );
}
