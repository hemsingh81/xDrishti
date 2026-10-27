import { Box, Chip, Paper, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ApiError } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { briefingQuery, type Briefing, type BriefingKind } from '../api/briefingApi';
import { fixedLabel, modelLabel } from '../lib/wording';

function FactsUsed({ briefing }: { briefing: Briefing }) {
  const { evidence } = briefing;
  return (
    <Box component="details" sx={{ mt: 1 }}>
      <Typography component="summary" variant="body2" sx={{ cursor: 'pointer' }}>
        Facts used
      </Typography>
      <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
        {typeof evidence.planId === 'number' && (
          <Typography component="li" variant="body2">
            Plan #{evidence.planId}
          </Typography>
        )}
        {typeof evidence.digestId === 'number' && (
          <Typography component="li" variant="body2">
            Digest #{evidence.digestId}
          </Typography>
        )}
        {evidence.runKeys.map((key) => (
          <Typography key={key} component="li" variant="body2" sx={{ overflowWrap: 'anywhere' }}>
            Run {key}
          </Typography>
        ))}
        <Typography component="li" variant="body2" sx={{ overflowWrap: 'anywhere' }}>
          Facts fingerprint {briefing.factsHash.slice(0, 12)}, version {briefing.version}, prompt{' '}
          {briefing.promptVersion}
        </Typography>
      </Box>
    </Box>
  );
}

/** What the checker found in a refused model text, in plain words: the codes' words, the token and where it started. The refused text itself is not shown. */
function WhyRejected({ briefing }: { briefing: Briefing }) {
  const refused = briefing.attempts.filter((a) => !a.passed && a.findings.length > 0);
  if (briefing.source !== 'deterministic' || briefing.reason !== 'guard_rejected' || refused.length === 0) return null;
  return (
    <Box component="details" sx={{ mt: 1 }}>
      <Typography component="summary" variant="body2" sx={{ cursor: 'pointer' }}>
        Why the model text was not used
      </Typography>
      {refused.map((attempt) => (
        <Box key={attempt.n} component="ul" aria-label={`Attempt ${attempt.n} findings`} sx={{ m: 0, pl: 2.5 }}>
          {attempt.findings.map((f, i) => (
            <Typography key={i} component="li" variant="body2" sx={{ overflowWrap: 'anywhere' }}>
              Attempt {attempt.n}: {f.words}
              {f.token ? ` (${f.token})` : ''}
            </Typography>
          ))}
        </Box>
      ))}
    </Box>
  );
}

/**
 * The stored briefing (daily) or weekly review text of one plan date or week (P9-06). Read only: there is no button that asks the model.
 * The text is shown as text, exactly as stored (React escapes it; nothing is interpreted), so a number is never added or dropped by the screen.
 */
export function BriefingCard({ kind, subject }: { kind: BriefingKind; subject: string }) {
  const query = useQuery(briefingQuery(kind, subject));
  const title = kind === 'daily' ? 'Briefing' : 'Weekly review';
  let body;
  if (query.isPending) {
    body = <LoadingState label={`Loading the ${title.toLowerCase()}…`} />;
  } else if (query.error instanceof ApiError && query.error.status === 404) {
    body = (
      <Typography variant="body2" color="text.secondary">
        No {kind === 'daily' ? 'briefing' : 'weekly review'} stored for this {kind === 'daily' ? 'date' : 'week'}.
      </Typography>
    );
  } else if (query.isError) {
    body = <ErrorState error={query.error} />;
  } else {
    const b = query.data;
    body = (
      <>
        <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 1 }}>
          {b.source === 'model' ? modelLabel : fixedLabel(b.reason)}
        </Typography>
        <Typography
          component="div"
          variant="body2"
          role="region"
          aria-label={`${title} text`}
          sx={{ whiteSpace: 'pre-wrap', overflowWrap: 'anywhere' }}
        >
          {b.text}
        </Typography>
        <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1 }}>
          {b.wordCount} words
        </Typography>
        <FactsUsed briefing={b} />
        <WhyRejected briefing={b} />
      </>
    );
  }
  return (
    <Paper variant="outlined" component="section" aria-label={title} sx={{ p: 2, mb: 2 }}>
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center', mb: 1 }}>
        <Typography variant="subtitle1" component="h2">
          {title}
        </Typography>
        <Chip size="small" variant="outlined" label={subject} />
      </Stack>
      {body}
    </Paper>
  );
}
