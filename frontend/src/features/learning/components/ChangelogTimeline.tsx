import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { EvidenceBlock, formatAsOf, formatCount, NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { changelogLimit, changelogQuery, type ChangelogEntry, type ChangelogScope } from '../api/learningQueries';

interface ChangelogTimelineProps {
  scope?: ChangelogScope;
  /** Names the list; unique on a page that shows two. */
  label: string;
  /** Rows shown; the rest are counted, never hidden silently. */
  show?: number;
}

/** The learning changelog, newest first. Every field is the server's text, drawn as text. Rows are never changed here. */
export function ChangelogTimeline({ scope = {}, label, show = 25 }: ChangelogTimelineProps) {
  const query = useQuery(changelogQuery(scope));
  if (query.isPending) return <LoadingState label="Loading the changelog…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const rows = query.data;
  if (rows.length === 0) {
    return <NonValue state={{ kind: 'empty', reason: 'No cell move or model designation has been recorded.' }} />;
  }
  return (
    <Box>
      <Box
        component="ol"
        aria-label={label}
        sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gridTemplateColumns: 'minmax(0, 1fr)', gap: 1.5 }}
      >
        {rows.slice(0, show).map((row) => (
          <Entry key={row.id} row={row} />
        ))}
      </Box>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1 }}>
        {rows.length > show ? `Showing the newest ${formatCount(show)} of ${formatCount(rows.length)} rows read. ` : ''}
        {rows.length >= changelogLimit ? 'The newest 200 rows were read; there may be more. ' : ''}
        Rows are never changed; a revert is a row that points at the one it reverts.
      </Typography>
      <Box sx={{ mt: 1 }}>
        <EvidenceBlock n={rows.length} range={null} basis="unknown" compact />
      </Box>
    </Box>
  );
}

function Entry({ row }: { row: ChangelogEntry }) {
  return (
    <Box component="li" sx={{ borderLeft: 3, borderColor: 'divider', pl: 1.5, minWidth: 0 }}>
      <Typography variant="body2" sx={{ fontWeight: 700, overflowWrap: 'anywhere' }}>
        {row.fromState} → {row.toState}{' '}
        <Typography component="span" variant="caption" color="text.secondary">
          ({row.kind}, {row.subjectKind})
        </Typography>
      </Typography>
      <Typography variant="caption" color="text.secondary" component="p" sx={{ m: 0, overflowWrap: 'anywhere' }}>
        {formatAsOf(row.at)} · by {row.actor} · {row.subject}
      </Typography>
      <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
        {row.reason}
      </Typography>
      {row.revertsId !== null && (
        <Typography variant="caption" component="p" sx={{ m: 0 }}>
          Reverts row {formatCount(row.revertsId)}
        </Typography>
      )}
      {row.paperDays !== null && (
        <Typography variant="caption" component="p" sx={{ m: 0 }}>
          Paper days stated: {formatCount(row.paperDays)}
        </Typography>
      )}
      {row.evidenceIds.length > 0 && (
        <Typography variant="caption" color="text.secondary" component="p" sx={{ m: 0, overflowWrap: 'anywhere' }}>
          Evidence: {row.evidenceIds.join(', ')}
        </Typography>
      )}
    </Box>
  );
}
