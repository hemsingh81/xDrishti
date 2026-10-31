import { Alert, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { LoadingState } from '@/shared/ui';
import { RetryError } from './RetryError';
import type { Schemas } from '@/shared/api';
import { formatIstTime, formatPrice } from '@/shared/lib/format';
import { blotterQuery, overviewQuery } from '../api/viewsApi';
import { causeWord, stateGroups } from '../lib/stateWords';
import { DateFilter, FilterBar, FilterSelect } from './Filters';
import { CodesCell, FillCell, StateCell, SymbolCell } from './OrderRow';
import { Pager } from './Pager';
import { StackedTable, type Col } from './StackedTable';
import { useUrlState } from '@/shared/analytics';

type Row = Schemas['BlotterRow'];
const shortId = (id: string | null) => (id === null ? 'not known' : id.length > 12 ? `${id.slice(0, 12)}…` : id);
const pageSize = 50;

const columns: readonly Col<Row>[] = [
  { id: 'time', header: 'Time', render: (r) => `${formatIstTime(r.at)} IST` },
  { id: 'account', header: 'Account', render: (r) => r.account },
  { id: 'symbol', header: 'Symbol', render: (r) => <SymbolCell row={r} /> },
  { id: 'side', header: 'Side', render: (r) => r.side },
  { id: 'qty', header: 'Quantity', align: 'right', render: (r) => r.quantity },
  { id: 'type', header: 'Type and price', render: (r) => `${r.orderType} ${formatPrice(r.price)}` },
  { id: 'state', header: 'State', render: (r) => <StateCell row={r} /> },
  { id: 'by', header: 'Caused by', render: (r) => causeWord(r.by) },
  { id: 'ids', header: 'Order ids', render: (r) => `${shortId(r.clientOrderId)} / ${shortId(r.brokerOrderId)}` },
  { id: 'fill', header: 'Fill', render: (r) => <FillCell row={r} /> },
  { id: 'codes', header: 'Codes', render: (r) => <CodesCell row={r} /> },
];

/** The blotter: every order of the day with its state, who caused it, its fill and why it was refused. */
export function OrdersTab() {
  const url = useUrlState();
  const date = url.text('date') ?? '';
  const accountId = url.text('account') ?? '';
  const stateGroup = url.text('group') ?? '';
  const page = Number(url.text('page') ?? '1') || 1;
  const accounts = useQuery(overviewQuery(date || undefined));
  const blotter = useQuery(blotterQuery({ date, accountId, stateGroup, page: page > 1 ? page : undefined }));
  const set = (patch: Record<string, string>) => {
    url.update({ page: null, ...Object.fromEntries(Object.entries(patch).map(([k, v]) => [k, v === '' ? null : v])) });
  };
  return (
    <>
      <FilterBar>
        <DateFilter
          value={date}
          onChange={(v) => {
            set({ date: v });
          }}
        />
        <FilterSelect
          label="Account"
          value={accountId}
          options={(accounts.data?.accounts ?? []).map((a) => ({ value: a.accountId, label: a.name }))}
          onChange={(v) => {
            set({ account: v });
          }}
        />
        <FilterSelect
          label="State group"
          value={stateGroup}
          options={stateGroups.map((g) => ({ value: g, label: g }))}
          onChange={(v) => {
            set({ group: v });
          }}
        />
      </FilterBar>
      {blotter.isPending && <LoadingState label="Loading orders" />}
      {blotter.error && <RetryError error={blotter.error} onRetry={() => void blotter.refetch()} />}
      {blotter.data?.length === 0 && (
        <Alert severity="info" role="status">
          No orders for this view.
        </Alert>
      )}
      {blotter.data && blotter.data.length > 0 && (
        <StackedTable label="Orders" columns={columns} rows={blotter.data} rowKey={(r) => r.intentId} />
      )}
      <Pager
        page={page}
        full={(blotter.data?.length ?? 0) >= pageSize}
        onPage={(p) => {
          url.update({ page: String(p) });
        }}
      />
      <Typography variant="caption" sx={{ color: 'text.secondary', display: 'block', mt: 1 }}>
        Slippage is each fill against the ticket price, positive when worse for you. algoId is shown only as the broker
        returned it.
      </Typography>
    </>
  );
}
