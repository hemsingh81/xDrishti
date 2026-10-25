import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useSearchParams } from 'react-router';
import {
  BarList,
  DataTable,
  EvidenceBlock,
  formatCount,
  formatR,
  formatRate,
  NonValue,
  type Column,
} from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { lossTagsQuery, type CellLossTags } from '../api/learningQueries';
import { Panel } from './Panel';

type TagRow = CellLossTags['tags'][number];

const label = (kind: string) => kind.replaceAll('_', ' ');

function CellSection({ cell }: { cell: CellLossTags }) {
  const columns: Column<TagRow>[] = [
    { kind: 'text', id: 'kind', header: 'Tag', render: (r) => label(r.kind) },
    {
      kind: 'metric',
      id: 'count',
      header: 'Losses',
      metric: (r) => ({ kind: 'value', value: r.count }),
      format: formatCount,
    },
    {
      kind: 'metric',
      id: 'share',
      header: `Share of ${formatCount(cell.losses)} losses`,
      metric: (r) =>
        r.share === null ? { kind: 'unavailable', reason: 'no losses in the cell' } : { kind: 'value', value: r.share },
      format: formatRate,
    },
    {
      kind: 'metric',
      id: 'lost',
      header: 'R lost',
      metric: (r) => ({ kind: 'value', value: r.lostR }),
      format: formatR,
    },
  ];
  return (
    <Panel title={cell.cellKey}>
      <Typography variant="body2" sx={{ mb: 1 }}>
        {formatCount(cell.trades)} trades, {formatCount(cell.losses)} losses, {formatCount(cell.untagged)} untagged
        {cell.losses < 1 ? ' (insufficient: no loss to tag)' : ''}
      </Typography>
      <BarList
        label={`${cell.cellKey} losses per tag`}
        format={formatCount}
        diverging={false}
        rows={cell.tags.map((t) => ({
          id: t.kind,
          label: label(t.kind),
          state: { kind: 'value', value: t.count },
          n: cell.losses,
        }))}
      />
      <Box sx={{ mt: 1.5 }}>
        <DataTable label={`${cell.cellKey} tags`} columns={columns} rows={cell.tags} rowKey={(r) => r.kind} />
      </Box>
      <Box sx={{ mt: 1.5 }}>
        <Typography variant="body2" sx={{ fontWeight: 600 }}>
          Growing tags
        </Typography>
        {cell.growingState === 'not_evaluable' ? (
          <NonValue state={{ kind: 'not_evaluable', reason: cell.growingReason ?? 'not enough losses' }} />
        ) : cell.growing.length === 0 ? (
          <Typography variant="body2">None.</Typography>
        ) : (
          <ul aria-label={`${cell.cellKey} growing tags`}>
            {cell.growing.map((g) => (
              <li key={g.kind}>
                {label(g.kind)}: {formatCount(g.earlier)} of {formatCount(g.window)} losses before,{' '}
                {formatCount(g.recent)} of {formatCount(g.window)} now
              </li>
            ))}
          </ul>
        )}
      </Box>
    </Panel>
  );
}

/** Why trades lost, on the report population: counts and R lost per tag, growing tags, worst losses. Tags describe; they do not explain. */
export function LossTagsTab() {
  const [params] = useSearchParams();
  const runs = params.getAll('run');
  const query = useQuery(lossTagsQuery(runs));
  if (query.isPending) return <LoadingState label="Loading loss tags…" />;
  if (query.isError)
    return (
      <Panel title="Loss tags">
        <ErrorState error={query.error} />
      </Panel>
    );
  const v = query.data;
  const evidence = (
    <EvidenceBlock
      label="Loss tags evidence"
      n={v.state === 'empty' ? null : v.evidence.n}
      range={null}
      basis={v.evidence.basis === 'net' ? 'net' : v.evidence.basis === 'gross' ? 'gross' : 'unknown'}
      population={v.evidence.runs.map(
        (r) => `${r.strategyId} ${r.runKey.slice(0, 12)} ${r.style ?? ''} ${r.exit ?? ''}`,
      )}
      limits={[...v.limits, ...v.evidence.unavailableInputs]}
    />
  );
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      {v.state === 'empty' ? (
        <Panel title="Loss tags">
          <NonValue
            state={{
              kind: 'empty',
              reason: 'Choose runs: add run=KEY:STYLE:EXIT to the address. No stored run records its style and exit.',
            }}
          />
          {v.evidence.skipped.length > 0 && (
            <ul aria-label="Runs left out">
              {v.evidence.skipped.map((s) => (
                <li key={s}>{s}</li>
              ))}
            </ul>
          )}
        </Panel>
      ) : (
        <>
          <Typography variant="body2" color="text.secondary">
            {formatCount(v.tradesRead)} trades read, {formatCount(v.losses)} losses. A loss can carry several tags, so
            counts and R lost add to more than the total.
          </Typography>
          {v.cells.map((cell) => (
            <CellSection key={cell.cellKey} cell={cell} />
          ))}
          <Panel title="Worst losses">
            <ul aria-label="Worst losses">
              {v.worst.map((w) => (
                <li key={w.key}>
                  {w.key} [{w.cellKey}] {formatR(w.r)}
                  {w.tags.length === 0 ? ': untagged' : ''}
                  <ul>
                    {w.tags.map((t) => (
                      <li key={t.kind}>
                        {label(t.kind)}: {t.detail}
                      </li>
                    ))}
                  </ul>
                </li>
              ))}
            </ul>
          </Panel>
          <Typography variant="body2">
            The growing-tag detector made {formatCount(v.comparisons)} comparisons; about{' '}
            {formatCount(Math.round(v.expectedChanceFlags * 10) / 10)} flags are expected by chance alone.
          </Typography>
        </>
      )}
      <Panel title="Not built">
        <ul aria-label="Tags not built">
          {v.notBuilt.map((n) => (
            <li key={n}>{n}</li>
          ))}
        </ul>
      </Panel>
      {evidence}
    </Box>
  );
}
