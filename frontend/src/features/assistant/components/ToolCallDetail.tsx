import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { DataTable, EvidenceBlock, formatAsOf, formatCount, formatSigned, type Column } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { toolCallQuery } from '../api/assistantApi';
import { asOfOf, cellText, evidenceProps, metricCellOf, tableOf, totalRowsOf } from '../lib/toolRows';
import { words } from '../lib/wording';

type Row = Record<string, unknown>;
const fmt = (v: number) => (Number.isInteger(v) ? formatCount(v) : formatSigned(v));

function argumentsText(value: unknown): string {
  return JSON.stringify(value, null, 2);
}

/** One stored tool call, loaded when its chip is opened: arguments, the rows in the kit's table, the evidence. All text. */
export function ToolCallDetail({ id }: { id: number }) {
  const query = useQuery(toolCallQuery(id));
  if (query.isPending) return <LoadingState label="Loading tool call…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  const call = query.data;
  const table = tableOf(call.result);
  const columns: Column<Row>[] = table.columns.map((c) =>
    c.metric
      ? { kind: 'metric', id: c.id, header: c.id, metric: (r) => metricCellOf(r[c.id]), format: fmt }
      : { kind: 'text', id: c.id, header: c.id, render: (r) => cellText(r[c.id]) },
  );
  const evidence = evidenceProps(call.evidence);
  const total = totalRowsOf(call.result);
  const asOf = asOfOf(call.result);
  const isSql = call.tool === 'run_sql';
  const sql =
    isSql && typeof call.arguments === 'object' && call.arguments !== null
      ? ((call.arguments as Record<string, unknown>).query ?? (call.arguments as Record<string, unknown>).sql)
      : undefined;
  return (
    <Box sx={{ p: 1.5, border: 1, borderColor: 'divider', borderRadius: 1, mt: 0.5, minWidth: 0 }}>
      {isSql && (
        <Typography variant="caption" component="p" sx={{ fontWeight: 600 }}>
          {words.adHoc}
        </Typography>
      )}
      {call.tool === 'make_chart' && (
        <Typography variant="caption" component="p" sx={{ fontWeight: 600 }}>
          {words.chartAsTable}
        </Typography>
      )}
      <Typography variant="caption" component="p">
        {asOf ? `As of ${formatAsOf(asOf)}` : 'As of not stated'}
      </Typography>
      <Box
        component="pre"
        aria-label="Arguments"
        sx={{ m: 0, my: 1, p: 1, overflow: 'auto', bgcolor: 'action.hover', fontSize: '0.8rem' }}
      >
        {typeof sql === 'string' ? sql : argumentsText(call.arguments)}
      </Box>
      {call.status !== 'ok' ? (
        <Typography role="alert" variant="body2" color="error">
          {call.errorCode ?? 'error'}
          {errorMessage(call.result)}
        </Typography>
      ) : (
        <DataTable<Row>
          label={`Tool call result: ${call.tool}`}
          columns={columns}
          rows={table.rows}
          rowKey={(r) => String(table.rows.indexOf(r))}
          {...(evidence ? { evidence } : {})}
        />
      )}
      {call.truncated && (
        <Typography variant="caption" component="p">
          Showing {formatCount(call.rows)} of {total === null ? 'more' : formatCount(total)} rows
        </Typography>
      )}
      {call.status !== 'ok' && evidence && <EvidenceBlock {...evidence} />}
    </Box>
  );
}

function errorMessage(result: unknown): string {
  if (typeof result !== 'object' || result === null) return '';
  const error = (result as Record<string, unknown>).error;
  const message = typeof error === 'object' && error !== null ? (error as Record<string, unknown>).message : null;
  return typeof message === 'string' ? `: ${message}` : '';
}
