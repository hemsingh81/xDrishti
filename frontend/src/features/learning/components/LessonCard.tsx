import { Box, Button, Card, CardContent, Chip, Link, Stack, Typography } from '@mui/material';
import { Link as RouterLink } from 'react-router';
import { formatAsOf, NonValue } from '@/shared/analytics';
import type { Lesson } from '../api/learningQueries';
import { TermRow } from './TermRow';
import { isCellKey, learningLink, lessonResultWord, monitorKindWord } from '../lib/roleText';

/** One lesson: what a monitor found, its state, the fix tried and the result. Server text is drawn as text. */
export function LessonCard({ lesson, onOpen }: { lesson: Lesson; onOpen: () => void }) {
  return (
    <Card component="li" variant="outlined" sx={{ listStyle: 'none', minWidth: 0 }}>
      <CardContent sx={{ display: 'grid', gap: 1 }}>
        <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
          <Typography component="h3" variant="h3">
            Lesson #{lesson.id}
          </Typography>
          <Chip size="small" variant="outlined" label={monitorKindWord(lesson.monitorKind)} />
          <Chip size="small" variant="outlined" label={`State: ${lesson.state}`} />
        </Stack>
        <Typography sx={{ overflowWrap: 'anywhere' }}>{lesson.problem}</Typography>
        <Box component="dl" sx={{ m: 0, display: 'grid', gap: 0.5 }}>
          <TermRow term="Subject">
            {isCellKey(lesson.subject) ? (
              <Link component={RouterLink} to={learningLink({ tab: 'setups', cell: lesson.subject })}>
                {lesson.subject}
              </Link>
            ) : (
              lesson.subject
            )}
          </TermRow>
          <TermRow term="Fix">
            {lesson.hypothesisRef === null ? (
              <NonValue state={{ kind: 'empty', reason: 'No fix tried yet' }} />
            ) : (
              <Link component={RouterLink} to={learningLink({ tab: 'experiments', hypothesis: lesson.hypothesisRef })}>
                Hypothesis #{lesson.hypothesisRef}
              </Link>
            )}
          </TermRow>
          <TermRow term="Result">{lessonResultWord(lesson.result)}</TermRow>
          <TermRow term="Opened">{formatAsOf(lesson.createdAt)}</TermRow>
          <TermRow term="Updated">{formatAsOf(lesson.updatedAt)}</TermRow>
        </Box>
        <Box>
          <Button size="small" onClick={onOpen} sx={{ minHeight: 44 }}>
            Open lesson #{lesson.id}
          </Button>
        </Box>
      </CardContent>
    </Card>
  );
}
