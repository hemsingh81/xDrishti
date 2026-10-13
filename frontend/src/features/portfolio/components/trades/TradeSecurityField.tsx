import { Box, Button, FormHelperText, Stack, TextField } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useDebouncedValue } from '@/shared/lib/useDebouncedValue';
import { Typeahead, type TypeaheadOption } from '@/shared/ui/Typeahead';
import { tradeHoldingsQuery } from '../../api/tradeQueries';
import { problemText } from '../../lib/errors';
import type { TradeForm } from '../../lib/tradeForm';

export interface PickedHolding {
  id: string;
  symbol: string;
  name: string;
}

interface TradeSecurityFieldProps {
  accountId: string;
  form: TradeForm;
  error?: string;
  onChange: (patch: Partial<TradeForm>, picked?: PickedHolding | null) => void;
}

/**
 * The security of the trade: one of the account's holdings (searched by symbol or name), or, for a security that is not held,
 * its symbol and/or ISIN typed in (the API accepts an NSE share or ETF of the instrument list).
 */
export function TradeSecurityField({ accountId, form, error, onChange }: TradeSecurityFieldProps) {
  const [text, setText] = useState('');
  const [chosen, setChosen] = useState<TypeaheadOption | null>(null);
  const search = useDebouncedValue(text, 250);
  const lookup = useQuery({ ...tradeHoldingsQuery(accountId, search), enabled: accountId !== '' && !form.typed });
  const found: TypeaheadOption[] = (lookup.data ?? []).map((row) => ({
    value: row.id,
    label: row.symbol,
    hint: row.name,
  }));
  const options = chosen && !found.some((option) => option.value === chosen.value) ? [chosen, ...found] : found;
  const problem = error ?? (lookup.isError ? problemText(lookup.error, 'The holdings could not be searched.') : null);

  if (form.typed) {
    return (
      <Box data-field="security">
        <Stack direction={{ xs: 'column', sm: 'row' }} sx={{ gap: 1.5 }}>
          <TextField
            label="Symbol"
            value={form.symbol}
            error={error !== undefined}
            onChange={(event) => {
              onChange({ symbol: event.target.value });
            }}
            slotProps={{ inputLabel: { shrink: true }, htmlInput: { autoCapitalize: 'characters' } }}
            sx={{ flex: 1 }}
          />
          <TextField
            label="ISIN (optional)"
            value={form.isin}
            error={error !== undefined}
            onChange={(event) => {
              onChange({ isin: event.target.value });
            }}
            slotProps={{ inputLabel: { shrink: true } }}
            sx={{ flex: 1 }}
          />
        </Stack>
        {problem && <FormHelperText error>{problem}</FormHelperText>}
        <Button
          size="small"
          sx={{ mt: 0.5 }}
          onClick={() => {
            onChange({ typed: false, symbol: '', isin: '' }, null);
          }}
        >
          Choose from the account’s holdings instead
        </Button>
      </Box>
    );
  }
  return (
    <Box data-field="security">
      <Typeahead
        label="Security"
        placeholder="Search the account’s holdings by symbol or name"
        options={options}
        value={form.holdingId === '' ? null : form.holdingId}
        onSearch={setText}
        onChange={(id) => {
          const option = options.find((candidate) => candidate.value === id) ?? null;
          setChosen(option);
          onChange(
            { holdingId: id ?? '' },
            option ? { id: option.value, symbol: option.label, name: option.hint ?? '' } : null,
          );
        }}
        loading={lookup.isFetching}
        noOptionsText={accountId === '' ? 'Choose the account first' : 'No holding of this account matches'}
        minWidth={0}
      />
      {problem && <FormHelperText error>{problem}</FormHelperText>}
      <Button
        size="small"
        sx={{ mt: 0.5 }}
        onClick={() => {
          onChange({ typed: true, holdingId: '' }, null);
        }}
      >
        Not in the list? Enter a symbol or ISIN
      </Button>
    </Box>
  );
}
