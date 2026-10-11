import { Card, CardContent, Stack, ToggleButton, ToggleButtonGroup } from '@mui/material';
import type { AccountSummary, PortfolioBucket } from '@/shared/api';
import { SearchField } from '@/shared/ui/SearchField';
import { Typeahead, type TypeaheadOption } from '@/shared/ui/Typeahead';
import { holdingStatuses, type HoldingStatus, type PortfolioFilters } from '../lib/filters';

interface ScopeBarProps {
  filters: PortfolioFilters;
  /** The accounts with the Portfolio role. */
  accounts: readonly AccountSummary[];
  /** Undefined while loading or when they could not be loaded: the filter then offers nothing, the rest still works. */
  buckets: readonly PortfolioBucket[] | undefined;
  bucketsLoading: boolean;
  onChange: (patch: Partial<PortfolioFilters>, options?: { replace?: boolean }) => void;
}

const statusLabels: Record<HoldingStatus, string> = { open: 'Open', closed: 'Closed', all: 'All' };

const accountOptions = (accounts: readonly AccountSummary[]): TypeaheadOption[] =>
  accounts.map((a) => ({ value: a.id, label: a.name, hint: `${a.broker} · ${a.maskedClientId}` }));

const bucketOptions = (buckets: readonly PortfolioBucket[] | undefined): TypeaheadOption[] =>
  (buckets ?? []).map((b) => ({ value: b.id, label: b.name }));

/** Which holdings: the account, the bucket, open or closed, and a search. Every change goes to the URL through `onChange`. */
export function ScopeBar({ filters, accounts, buckets, bucketsLoading, onChange }: ScopeBarProps) {
  return (
    <Card sx={{ mb: 2, flexShrink: 0 }}>
      <CardContent>
        <Stack direction="row" sx={{ flexWrap: 'wrap', gap: 1.5, alignItems: 'center' }}>
          <Typeahead
            label="Account"
            placeholder="All accounts"
            options={accountOptions(accounts)}
            value={filters.account}
            noOptionsText="No account matches"
            onChange={(account) => {
              onChange({ account });
            }}
          />
          <Typeahead
            label="Bucket"
            placeholder="All buckets"
            options={bucketOptions(buckets)}
            value={filters.bucket}
            loading={bucketsLoading}
            noOptionsText="No bucket matches"
            onChange={(bucket) => {
              onChange({ bucket });
            }}
          />
          <SearchField
            value={filters.q}
            label="Search holdings"
            placeholder="Search symbol, name or ISIN"
            onCommit={(q) => {
              onChange({ q }, { replace: true });
            }}
          />
          <ToggleButtonGroup
            exclusive
            size="small"
            aria-label="Status"
            value={filters.status}
            onChange={(_event, status: HoldingStatus | null) => {
              if (status) onChange({ status });
            }}
          >
            {holdingStatuses.map((status) => (
              <ToggleButton key={status} value={status}>
                {statusLabels[status]}
              </ToggleButton>
            ))}
          </ToggleButtonGroup>
        </Stack>
      </CardContent>
    </Card>
  );
}
