import { Alert, Box, Button, Chip, List, ListItem, Stack, TextField, Typography } from '@mui/material';
import { useState } from 'react';
import { ApiError } from '@/shared/api';
import { useDiscardDraft, useValidateText, type Draft } from '../api/draftApi';
import { draftWords, failReasons } from '../lib/draftWording';
import { ConfirmDraftDialog } from './ConfirmDraftDialog';

function Lines({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) return null;
  return (
    <Box>
      <Typography variant="subtitle2">{title}</Typography>
      <List dense disablePadding>
        {items.map((item) => (
          <ListItem key={item} disableGutters sx={{ py: 0 }}>
            {item}
          </ListItem>
        ))}
      </List>
    </Box>
  );
}

function Summary({ draft }: { draft: Draft }) {
  const run = draft.runSummary;
  if (!draft.confirmed) return null;
  return (
    <Alert severity="success">
      {draftWords.confirmed} {draft.confirmed.strategyId} version {draft.confirmed.version}
      {draft.confirmed.edited ? ' (you edited the text)' : ''}.
      {draft.confirmed.labRunId === null ? ' No quick test was started.' : ` ${draftWords.quickTest}`}
      {run && (
        <Box component="span" sx={{ display: 'block' }}>
          Quick test {run.state}
          {run.trades !== null && run.instrumentsRun !== null
            ? `: ${run.instrumentsRun} instruments run, ${run.skipped ?? 0} skipped, ${run.trades} trades, ${run.openAtEnd ?? 0} open at the end.`
            : '.'}{' '}
          {run.labels.join(' ')}
          {run.problems.map((p) => ` ${p.message}`).join('')}
        </Box>
      )}
    </Alert>
  );
}

/** One draft: what the model understood and chose, the text (editable), the checks, and the owner's Confirm / Discard. Everything the model wrote is shown as plain text. */
export function DraftResult({ draft }: { draft: Draft }) {
  const [edited, setEdited] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const validate = useValidateText();
  const discard = useDiscardDraft();
  const text = edited ?? draft.yaml ?? '';
  const done = draft.status === 'confirmed' || draft.status === 'discarded';
  const hasText = draft.yaml !== null;
  const problems = validate.data ? validate.data.problems : draft.problems;
  const confirmable =
    !done && hasText && (draft.status === 'valid' || (edited !== null && validate.data?.valid === true));

  return (
    <Stack spacing={1.5} aria-label="Draft">
      <Stack direction="row" spacing={1} sx={{ alignItems: 'center' }}>
        <Chip label={draft.status} size="small" />
        <Typography variant="body2" color="text.secondary">
          {draftWords[draft.status as keyof typeof draftWords]}
        </Typography>
      </Stack>
      {draft.status === 'failed' && (
        <Alert severity="warning">{failReasons[draft.failReason ?? ''] ?? draftWords.failed}</Alert>
      )}
      <Summary draft={draft} />
      <Lines title="What the model understood" items={draft.understood} />
      <Lines title="Missing from the language today" items={draft.unsupported} />
      <Lines title="Left out of the text" items={draft.notExpressed.map((n) => `${n.what}: ${n.why}`)} />
      <Lines
        title="Values the model chose itself"
        items={draft.assumptions.map((a) => `${a.field} = ${a.value} (${a.why})`)}
      />
      {draft.requestedTest.from || draft.requestedTest.to || draft.requestedTest.baskets.length > 0 ? (
        <Typography variant="body2">
          The idea asked for a test {draft.requestedTest.from ?? ''} to {draft.requestedTest.to ?? ''}
          {draft.requestedTest.baskets.length > 0 ? ` on ${draft.requestedTest.baskets.join(', ')}` : ''}. You pick the
          window and the universe when you confirm.
        </Typography>
      ) : null}
      {draft.findings.length > 0 && (
        <Alert severity="warning">
          The checks found: {draft.findings.map((f) => `${f.code}${f.detail ? ` (${f.detail})` : ''}`).join(', ')}. Read
          the text with care.
        </Alert>
      )}
      {hasText && (
        <TextField
          label="Strategy text"
          value={text}
          onChange={(e) => {
            setEdited(e.target.value);
            validate.reset();
          }}
          multiline
          minRows={8}
          disabled={done}
          slotProps={{ htmlInput: { spellCheck: false, style: { fontFamily: 'monospace' } } }}
        />
      )}
      {problems.length > 0 && (
        <Alert severity={problems.some((p) => p.severity === 'error') ? 'error' : 'info'}>
          {problems.map((p) => `${p.path ? `${p.path}: ` : ''}${p.message}`).join(' | ')}
        </Alert>
      )}
      {validate.isError && (
        <Alert severity="error">
          {validate.error instanceof ApiError ? validate.error.message : 'Could not validate.'}
        </Alert>
      )}
      {validate.data?.valid === true && <Alert severity="success">The edited text is valid.</Alert>}
      {!done && (
        <Stack direction="row" spacing={1}>
          {hasText && edited !== null && (
            <Button
              onClick={() => {
                validate.mutate(text);
              }}
              disabled={validate.isPending}
            >
              Validate
            </Button>
          )}
          <Button
            variant="contained"
            disabled={!confirmable}
            onClick={() => {
              setConfirmOpen(true);
            }}
          >
            Confirm
          </Button>
          <Button
            onClick={() => {
              discard.mutate(draft.id);
            }}
            disabled={discard.isPending || draft.status === 'drafting'}
          >
            Discard
          </Button>
        </Stack>
      )}
      {confirmOpen && (
        <ConfirmDraftDialog
          draft={draft}
          text={edited}
          onClose={() => {
            setConfirmOpen(false);
          }}
        />
      )}
    </Stack>
  );
}
