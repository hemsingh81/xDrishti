import CloseIcon from '@mui/icons-material/Close';
import { Alert, Box, Divider, Drawer, IconButton, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useId, type ReactNode } from 'react';
import { formatCount } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { cellQuery, cellStateQuery } from '../api/learningQueries';
import { CellWindowsTable } from './CellWindowsTable';
import { ChangelogTimeline } from './ChangelogTimeline';

interface CellDrawerProps {
  cellKey: string;
  onClose: () => void;
}

/** One cell in full: its windows, where it is in its life, and its own changelog. Read only. */
export function CellDrawer({ cellKey, onClose }: CellDrawerProps) {
  const titleId = useId();
  const [strategy, timeframe, side, style, exit, regime] = cellKey.split('|');
  return (
    <Drawer
      anchor="right"
      open
      onClose={onClose}
      slotProps={{ paper: { role: 'dialog', 'aria-labelledby': titleId, sx: { width: 640, maxWidth: '100vw' } } }}
    >
      <Stack direction="row" sx={{ alignItems: 'flex-start', gap: 1, px: 2.5, py: 1.5 }}>
        <Box sx={{ flex: 1, minWidth: 0 }}>
          <Typography id={titleId} component="h2" variant="h2" sx={{ overflowWrap: 'anywhere' }}>
            {strategy ?? cellKey}
            {timeframe ? ` · ${timeframe}` : ''}
          </Typography>
          <Typography variant="body2" color="text.secondary" sx={{ overflowWrap: 'anywhere' }}>
            {[side, style, exit, regime && regime !== '*' ? regime : null].filter(Boolean).join(' · ')}
          </Typography>
        </Box>
        <IconButton aria-label="Close cell" edge="end" onClick={onClose} sx={{ minWidth: 44, minHeight: 44 }}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Divider />
      <Box sx={{ p: 2.5, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 3 }}>
        <Section title="Statistics by window">
          <Statistics cellKey={cellKey} />
        </Section>
        <Section title="Where this cell is in its life">
          <Lifecycle cellKey={cellKey} />
        </Section>
        <Section title="Changelog of this cell">
          <ChangelogTimeline scope={{ subjectKind: 'cell', subject: cellKey }} label="Changelog of this cell" />
        </Section>
      </Box>
    </Drawer>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  const id = useId();
  return (
    <Box component="section" aria-labelledby={id}>
      <Typography id={id} component="h3" variant="h3" sx={{ mb: 1 }}>
        {title}
      </Typography>
      {children}
    </Box>
  );
}

function Statistics({ cellKey }: { cellKey: string }) {
  const query = useQuery(cellQuery(cellKey));
  if (query.isPending) return <LoadingState label="Loading the cell…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  return <CellWindowsTable cells={query.data} />;
}

function Lifecycle({ cellKey }: { cellKey: string }) {
  const query = useQuery(cellStateQuery(cellKey));
  if (query.isPending) return <LoadingState label="Loading the cell state…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const { state, legalNext, rows, problems, paperDaysBeforeActive, note } = query.data;
  return (
    <Box sx={{ display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 1 }}>
      <Typography>
        State: <strong>{state}</strong> (replayed from {formatCount(rows)} changelog {rows === 1 ? 'row' : 'rows'}).
      </Typography>
      <Typography variant="body2">
        {legalNext.length === 0
          ? 'The lifecycle allows no move from this state.'
          : `The lifecycle allows a move to: ${legalNext.join(' or ')}.`}{' '}
        Needs {formatCount(paperDaysBeforeActive)} paper days before active. This is what the rules allow, not a
        control: moves are made with the CLI (<code>xd learn lifecycle</code>), never on this screen.
      </Typography>
      {problems.length > 0 && (
        <Alert severity="warning">
          The changelog rows of this cell do not add up:
          <ul style={{ margin: 0, paddingLeft: 18 }}>
            {problems.map((problem, index) => (
              <li key={index}>{problem}</li>
            ))}
          </ul>
        </Alert>
      )}
      <Typography variant="caption" color="text.secondary">
        {note}
      </Typography>
    </Box>
  );
}
