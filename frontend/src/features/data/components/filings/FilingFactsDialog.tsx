import { Alert, Dialog, DialogContent, DialogTitle, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, NonValue, formatCount, type Column } from '@/shared/analytics';
import type { components } from '@/shared/api/schema';
import { formatNumber } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { filingFactsQuery } from '../../api/filingsQueries';
import { isSynthetic, syntheticNote } from './words';

type Fact = components['schemas']['FilingFactView'];

const columns: readonly Column<Fact>[] = [
  { kind: 'text', id: 'field', header: 'Field', render: (f) => f.field },
  { kind: 'text', id: 'span', header: 'Span', render: (f) => f.span },
  { kind: 'text', id: 'basis', header: 'Basis', render: (f) => f.basis },
  {
    kind: 'text',
    id: 'value',
    header: 'As filed',
    align: 'right',
    render: (f) =>
      f.state === 'value' && f.value !== null ? (
        `${formatNumber(f.value)} ${f.unit}`
      ) : (
        <NonValue state={{ kind: 'unavailable', reason: f.reason ?? 'reason not stated' }} />
      ),
  },
];

/** The parsed facts of one results filing with the parser version, the sanity flags and the counts. No file content is asked for. */
export function FilingFactsDialog({ id, symbol, onClose }: { id: string | null; symbol: string; onClose: () => void }) {
  return (
    <Dialog open={id !== null} onClose={onClose} fullWidth maxWidth="md" aria-labelledby="filing-facts-title">
      <DialogTitle id="filing-facts-title">Facts of the {symbol} results filing</DialogTitle>
      <DialogContent>{id !== null && <Facts id={id} />}</DialogContent>
    </Dialog>
  );
}

function Facts({ id }: { id: string }) {
  const query = useQuery(filingFactsQuery(id));
  if (query.isPending) return <LoadingState label="Loading the facts" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const d = query.data;
  const flags = Object.entries(d.flags);
  return (
    <>
      <Typography variant="body2" sx={{ mb: 1 }}>
        Parser {d.parserVersion}
        {isSynthetic(d.parserVersion) ? ` (${syntheticNote})` : ''}. Outcome: {d.outcome.replaceAll('_', ' ')}
        {d.reason ? ` (${d.reason})` : ''}.
      </Typography>
      {isSynthetic(d.parserVersion) && (
        <Alert severity="warning" sx={{ mb: 1 }}>
          These numbers come from a parser {syntheticNote}.
        </Alert>
      )}
      {flags.length > 0 && (
        <Typography variant="body2" sx={{ mb: 1 }}>
          Kept by the parser:{' '}
          {flags.map(([name, count]) => `${name.replaceAll('_', ' ')} ${formatCount(count)}`).join('; ')}
        </Typography>
      )}
      <DataTable
        label="Parsed facts"
        columns={columns}
        rows={d.facts}
        rowKey={(f) => `${f.field}-${f.span}-${f.basis}`}
        status={
          d.facts.length === 0
            ? { kind: 'empty', reason: d.outcome === 'not_parsed' ? 'not parsed yet' : 'no facts stored' }
            : undefined
        }
      />
    </>
  );
}
