import { Alert, Box, Button } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type Ref } from 'react';
import type { DividendCandidate } from '@/shared/api';
import { useDebouncedValue } from '@/shared/lib/useDebouncedValue';
import { Typeahead, type TypeaheadOption } from '@/shared/ui/Typeahead';
import { dividendHoldingsQuery } from '../../api/dividendQueries';
import { alertInstrumentsQuery, bareSymbol } from '../../api/lookupQueries';
import { problemText } from '../../lib/errors';

const noneOption: TypeaheadOption = {
  value: 'none',
  label: 'Leave unallocated',
  hint: 'Counts for the account only, with the file’s name',
};

interface MatchTargetFieldProps {
  accountId: string;
  /** The holdings an ambiguous name fits: offered first. */
  candidates: readonly DividendCandidate[];
  value: TypeaheadOption | null;
  onChange: (option: TypeaheadOption | null) => void;
  inputRef?: Ref<HTMLInputElement>;
}

/**
 * Where the dividends of a name go: a type-ahead over the account's holdings (sold ones too) and the instrument list (NSE
 * shares and ETFs not held), or "Leave unallocated". The options are searched on the server as the owner types.
 */
export function MatchTargetField({ accountId, candidates, value, onChange, inputRef }: MatchTargetFieldProps) {
  const [text, setText] = useState('');
  const search = useDebouncedValue(text, 250);
  const holdings = useQuery(dividendHoldingsQuery(accountId, search));
  const instruments = useQuery(alertInstrumentsQuery(search));
  const heldSymbols = new Set((holdings.data ?? []).map((row) => row.symbol));
  const fromCandidates: TypeaheadOption[] = candidates.map((candidate) => ({
    value: `h:${candidate.holdingId}`,
    label: candidate.symbol,
    hint: `${candidate.name} · fits this name`,
  }));
  const fromHoldings: TypeaheadOption[] = (holdings.data ?? []).map((row) => ({
    value: `h:${row.id}`,
    label: row.symbol,
    hint: `${row.name} · held`,
  }));
  const fromMaster: TypeaheadOption[] = (instruments.data ?? [])
    .filter((row) => !heldSymbols.has(bareSymbol(row.symbol)))
    .map((row) => ({ value: `i:${row.id}`, label: bareSymbol(row.symbol), hint: `${row.name} · not held` }));
  const seen = new Set<string>();
  const options = [...fromCandidates, ...fromHoldings, ...fromMaster, noneOption].filter((option) => {
    if (seen.has(option.value)) return false;
    seen.add(option.value);
    return true;
  });
  const failed = holdings.isError ? holdings : instruments.isError ? instruments : null;
  return (
    <Box>
      <Typeahead
        label="Holding or instrument"
        placeholder="Search by symbol or name"
        options={options}
        value={value?.value ?? null}
        onSearch={setText}
        onChange={(next) => {
          onChange(options.find((option) => option.value === next) ?? null);
        }}
        loading={holdings.isFetching || instruments.isFetching}
        noOptionsText="No holding or instrument matches"
        minWidth={0}
        inputRef={inputRef}
      />
      {failed && (
        <Alert
          severity="error"
          sx={{ mt: 1 }}
          action={
            <Button
              color="inherit"
              size="small"
              onClick={() => {
                void failed.refetch();
              }}
            >
              Retry
            </Button>
          }
        >
          {problemText(failed.error, 'The holdings could not be searched.')}
        </Alert>
      )}
    </Box>
  );
}
