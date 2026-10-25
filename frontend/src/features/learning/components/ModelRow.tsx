import { Box, Button, Card, CardContent, Chip, Stack, Typography } from '@mui/material';
import { formatAsOf, formatCount, formatRatio } from '@/shared/analytics';
import { formatDate } from '@/shared/lib/format';
import type { Model } from '../api/learningQueries';
import { OutcomeBadge } from './OutcomeBadge';
import { TermRow } from './TermRow';

/** One registered model and what the validation said about it. Advisory: nothing here changes a role. */
export function ModelRow({ model: m, onOpen }: { model: Model; onOpen: () => void }) {
  return (
    <Card component="li" variant="outlined" sx={{ listStyle: 'none', minWidth: 0 }}>
      <CardContent sx={{ display: 'grid', gap: 1 }}>
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
          <Typography component="h3" variant="h3">
            Model #{m.id}
          </Typography>
          <Chip size="small" variant="outlined" label={`Role: ${m.role}`} />
        </Stack>
        <OutcomeBadge name={m.verdict} kind="verdict" />
        <Box component="dl" sx={{ m: 0, display: 'grid', gap: 0.5 }}>
          <TermRow term="Trainer">{m.trainer}</TermRow>
          <TermRow term="Feature version">{m.featureVersion}</TermRow>
          <TermRow term="Created">{formatAsOf(m.createdAt)}</TermRow>
          <TermRow term="Training rows">
            {formatCount(m.trainRows)}, {formatDate(m.from)} to {formatDate(m.to)}
          </TermRow>
          <TermRow term="Hold-out">
            {m.holdOutStart === null ? 'no hold-out kept' : `from ${formatDate(m.holdOutStart)}`}
          </TermRow>
          <TermRow term="Folds">
            {formatCount(m.usedFolds)} used; {formatCount(m.foldsBeatingBoth)} beat both baselines;{' '}
            {formatCount(m.foldsNeeded)} needed
          </TermRow>
          <TermRow term="Pooled calibration error">{formatRatio(m.pooledEcePp)} percentage points</TermRow>
          <TermRow term="Log loss">{formatRatio(m.pooledLogLoss)}</TermRow>
          <TermRow term="Brier">{formatRatio(m.pooledBrier)}</TermRow>
          <TermRow term="May be champion">{m.mayBeChampion ? 'yes' : 'no'}</TermRow>
        </Box>
        {m.reasons.length > 0 && (
          <Box component="ul" aria-label={`Reasons for model ${m.id}`} sx={{ m: 0, pl: 2.5 }}>
            {m.reasons.map((reason, index) => (
              <li key={index}>{reason}</li>
            ))}
          </Box>
        )}
        <Box>
          <Button size="small" onClick={onOpen} sx={{ minHeight: 44 }}>
            Open model #{m.id}
          </Button>
        </Box>
      </CardContent>
    </Card>
  );
}
