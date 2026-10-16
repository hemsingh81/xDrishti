import { Button, Stack, TextField } from '@mui/material';
import { useInfiniteRows } from '@/shared/lib/useInfiniteRows';
import { ledgerList } from '../../api/brokerHistoryQueries';
import {
  isLedgerClass,
  ledgerClasses,
  ledgerClassLabels,
  noLedgerFilter,
  periodError,
  type LedgerFilter,
} from '../../lib/ledger';
import { LedgerCoverage } from './LedgerCoverage';
import { LedgerList } from './LedgerList';
import { LedgerTotals } from './LedgerTotals';

const dateField = { inputLabel: { shrink: true } } as const;

/** One account's cash movements: the period and class filters, the totals by class and the list. */
export function CashView({
  accountId,
  filter,
  onFilter,
}: {
  accountId: string;
  filter: LedgerFilter;
  onFilter: (filter: LedgerFilter) => void;
}) {
  const invalid = periodError(filter);
  const list = useInfiniteRows({ ...ledgerList(accountId, filter), enabled: invalid === null });
  const filtered = filter.from !== '' || filter.to !== '' || filter.cls !== 'all';
  return (
    <Stack spacing={2}>
      <Stack direction="row" sx={{ gap: 1.5, flexWrap: 'wrap', alignItems: 'flex-start' }}>
        <TextField
          type="date"
          label="From"
          value={filter.from}
          onChange={(event) => {
            onFilter({ ...filter, from: event.target.value });
          }}
          slotProps={dateField}
          sx={{ minWidth: 160 }}
        />
        <TextField
          type="date"
          label="To"
          value={filter.to}
          error={invalid !== null}
          helperText={invalid}
          onChange={(event) => {
            onFilter({ ...filter, to: event.target.value });
          }}
          slotProps={dateField}
          sx={{ minWidth: 160 }}
        />
        <TextField
          select
          label="Class"
          value={filter.cls}
          onChange={(event) => {
            const value = event.target.value;
            onFilter({ ...filter, cls: isLedgerClass(value) ? value : 'all' });
          }}
          slotProps={{ select: { native: true }, inputLabel: { shrink: true } }}
          sx={{ minWidth: 160 }}
        >
          <option value="all">All classes</option>
          {ledgerClasses.map((cls) => (
            <option key={cls} value={cls}>
              {ledgerClassLabels[cls]}
            </option>
          ))}
        </TextField>
        {filtered && (
          <Button
            size="small"
            sx={{ mt: 1 }}
            onClick={() => {
              onFilter(noLedgerFilter);
            }}
          >
            Clear filters
          </Button>
        )}
      </Stack>
      {invalid === null && (
        <>
          {list.lastPage && <LedgerCoverage coverageFrom={list.lastPage.coverageFrom} />}
          {list.lastPage && <LedgerTotals totals={list.lastPage.totals} />}
          <LedgerList list={list} filtered={filtered} />
        </>
      )}
    </Stack>
  );
}
