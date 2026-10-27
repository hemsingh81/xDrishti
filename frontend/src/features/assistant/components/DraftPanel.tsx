import { Alert, Box, Button, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ApiError } from '@/shared/api';
import { ErrorState, LoadingState } from '@/shared/ui';
import { draftQuery, draftsQuery, useStartDraft } from '../api/draftApi';
import { draftWords } from '../lib/draftWording';
import { DraftResult } from './DraftResult';

const minIdea = 10;
const maxIdea = 2000;

function Current({ id }: { id: number }) {
  const draft = useQuery(draftQuery(id));
  if (draft.isPending) return <LoadingState label="Loading the draft…" />;
  if (draft.isError) return <ErrorState error={draft.error} />;
  return <DraftResult key={`${draft.data.id}-${draft.data.status}`} draft={draft.data} />;
}

/** The drafting panel (spec P9-07): an idea box, the newest drafts and one draft's result. Starting a draft runs the model only; nothing is stored or run until the owner confirms. */
export function DraftPanel() {
  const [idea, setIdea] = useState('');
  const [selected, setSelected] = useState<number | null>(null);
  const start = useStartDraft();
  const recent = useQuery(draftsQuery);
  const ok = idea.trim().length >= minIdea && idea.length <= maxIdea && !start.isPending;

  return (
    <Box
      component="section"
      aria-label={draftWords.title}
      sx={{ mt: 2, p: 2, border: 1, borderColor: 'divider', borderRadius: 1 }}
    >
      <Typography variant="h2" sx={{ fontSize: '1.1rem', mb: 0.5 }}>
        {draftWords.title}
      </Typography>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1.5 }}>
        {draftWords.hint}
      </Typography>
      <Stack spacing={1.5}>
        <TextField
          label="Your idea"
          value={idea}
          onChange={(e) => {
            setIdea(e.target.value);
          }}
          multiline
          minRows={3}
          helperText={`${idea.length} of ${maxIdea} characters (at least ${minIdea})`}
          error={idea.length > maxIdea}
        />
        <Box>
          <Button
            variant="contained"
            disabled={!ok}
            onClick={() => {
              start.mutate(idea.trim(), {
                onSuccess: (draft) => {
                  setSelected(draft.id);
                  setIdea('');
                },
              });
            }}
          >
            Draft strategy
          </Button>
        </Box>
        {start.isError && (
          <Alert severity="error">
            {start.error instanceof ApiError
              ? (start.error.problem?.title ?? 'The request failed.')
              : 'Could not reach the server.'}
          </Alert>
        )}
        {recent.data && recent.data.length > 0 && (
          <Stack direction="row" spacing={1} useFlexGap sx={{ flexWrap: 'wrap' }} aria-label="Recent drafts">
            {recent.data.slice(0, 6).map((d) => (
              <Button
                key={d.id}
                size="small"
                variant={d.id === selected ? 'contained' : 'outlined'}
                onClick={() => {
                  setSelected(d.id);
                }}
              >
                {`#${d.id} ${d.status}`}
              </Button>
            ))}
          </Stack>
        )}
        {selected !== null && <Current id={selected} />}
      </Stack>
    </Box>
  );
}
