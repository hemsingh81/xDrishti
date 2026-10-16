import { Box, Chip, Typography } from '@mui/material';
import type { DividendPreviewItem } from '@/shared/api';
import { matchKindLabels, matchTitle, targetText, type MatchDraft } from '../../lib/dividendImport';

/**
 * What a row's name was matched to: the holding or instrument (symbol and name, plain text) and the rule that decided; the
 * owner's own pick for the name replaces it. A name nothing fits says where the dividend will be stored.
 */
export function MatchedTo({ item, draft }: { item: DividendPreviewItem; draft: MatchDraft | undefined }) {
  if (draft) {
    return (
      <Box>
        <Typography component="span" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
          {targetText(draft.target)}
        </Typography>
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {draft.remember ? 'your choice, remembered' : 'your choice, this file only'}
        </Typography>
      </Box>
    );
  }
  const { match } = item;
  const title = matchTitle(match);
  if (title === null) {
    return (
      <Typography variant="body2" color="text.secondary">
        {item.status === 'ambiguous'
          ? `Ambiguous: ${String(match.candidates.length)} holdings fit`
          : 'No match: stored unallocated with the file’s name'}
      </Typography>
    );
  }
  return (
    <Box>
      <Typography component="span" sx={{ fontWeight: 600, overflowWrap: 'anywhere' }}>
        {title}
      </Typography>
      {match.name && match.name !== title && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block', overflowWrap: 'anywhere' }}>
          {match.name}
        </Typography>
      )}
      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
        {`by ${matchKindLabels[match.kind] ?? match.kind}${match.holdingId === null ? ' · not held, instrument only' : ''}`}
      </Typography>
      {match.kind === 'contains' && <Chip size="small" color="warning" variant="outlined" label="Partial name match" />}
    </Box>
  );
}
