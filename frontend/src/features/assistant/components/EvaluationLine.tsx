import { Typography } from '@mui/material';
import { formatAsOf } from '@/shared/analytics';
import type { AssistantStatus } from '../api/assistantApi';
import { words } from '../lib/wording';

/** Plain, neutral line about the evaluation: no badge, no success colour. */
export function EvaluationLine({ evaluation }: { evaluation: AssistantStatus['evaluation'] }) {
  const text =
    evaluation.state === 'passed'
      ? `Evaluated ${formatAsOf(evaluation.at)}`
      : evaluation.state === 'failed'
        ? `Warning: the last evaluation failed${evaluation.at ? ` (${formatAsOf(evaluation.at)})` : ''}`
        : words.notEvaluated;
  return (
    <Typography
      variant="body2"
      color={evaluation.state === 'failed' ? 'warning.main' : 'text.secondary'}
      sx={{ mb: 1 }}
    >
      {text}
    </Typography>
  );
}
