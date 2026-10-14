import { FormHelperText } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useDebouncedValue } from '@/shared/lib/useDebouncedValue';
import { Typeahead, type TypeaheadOption } from '@/shared/ui/Typeahead';
import { dividendHoldingsQuery } from '../../api/dividendQueries';
import { problemText } from '../../lib/errors';

interface DividendHoldingFieldProps {
  accountId: string;
  /** The holding chosen; '' = none (the dividend then belongs to the account alone, unallocated). */
  value: string;
  onChange: (holdingId: string) => void;
}

/**
 * The holding a dividend is for, optional: one of the account's holdings (sold ones too), searched by symbol or name. Left
 * empty, the dividend is unallocated: it counts for the account and the portfolio but for no holding.
 */
export function DividendHoldingField({ accountId, value, onChange }: DividendHoldingFieldProps) {
  const [text, setText] = useState('');
  const [chosen, setChosen] = useState<TypeaheadOption | null>(null);
  const search = useDebouncedValue(text, 250);
  const lookup = useQuery({ ...dividendHoldingsQuery(accountId, search), enabled: accountId !== '' });
  const found: TypeaheadOption[] = (lookup.data ?? []).map((row) => ({
    value: row.id,
    label: row.symbol,
    hint: `${row.name}${row.status === 'closed' ? ' · closed' : ''}`,
  }));
  const options = chosen && !found.some((option) => option.value === chosen.value) ? [chosen, ...found] : found;
  return (
    <div data-field="holding">
      <Typeahead
        label="Holding (optional)"
        placeholder="Search by symbol or name, or leave empty"
        options={options}
        value={value === '' ? null : value}
        onSearch={setText}
        onChange={(id) => {
          setChosen(options.find((candidate) => candidate.value === id) ?? null);
          onChange(id ?? '');
        }}
        loading={lookup.isFetching}
        noOptionsText={accountId === '' ? 'Choose the account first' : 'No holding of this account matches'}
        minWidth={0}
      />
      <FormHelperText error={lookup.isError}>
        {lookup.isError
          ? problemText(lookup.error, 'The holdings could not be searched.')
          : 'Without a holding the dividend counts for the account only (unallocated).'}
      </FormHelperText>
    </div>
  );
}
