import { Box, FormHelperText } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState, type Ref } from 'react';
import { useDebouncedValue } from '@/shared/lib/useDebouncedValue';
import { Typeahead, type TypeaheadOption } from '@/shared/ui/Typeahead';
import { alertInstrumentsQuery } from '../../api/lookupQueries';
import { problemText } from '../../lib/errors';

interface AlertInstrumentPickerProps {
  /** The chosen instrument's id; '' while none is chosen. */
  value: string;
  onChange: (instrument: { id: string; symbol: string; name: string } | null) => void;
  error?: string;
  inputRef?: Ref<HTMLInputElement>;
}

/** Search the NSE shares and ETFs (the only instruments an alert can be set on) by symbol or name, and choose one. */
export function AlertInstrumentPicker({ value, onChange, error, inputRef }: AlertInstrumentPickerProps) {
  const [text, setText] = useState('');
  const [chosen, setChosen] = useState<TypeaheadOption | null>(null);
  const search = useDebouncedValue(text, 250);
  const lookup = useQuery(alertInstrumentsQuery(search));
  const found: TypeaheadOption[] = (lookup.data ?? []).map((row) => ({
    value: row.id,
    label: row.symbol,
    hint: row.name,
  }));
  const options = chosen && !found.some((option) => option.value === chosen.value) ? [chosen, ...found] : found;
  const problem =
    error ?? (lookup.isError ? problemText(lookup.error, 'The instruments could not be searched.') : null);

  return (
    <Box>
      <Typeahead
        label="Instrument"
        placeholder="Search a share or ETF by symbol or name"
        options={options}
        value={value === '' ? null : value}
        inputRef={inputRef}
        onSearch={setText}
        onChange={(id) => {
          const option = options.find((candidate) => candidate.value === id) ?? null;
          setChosen(option);
          onChange(option ? { id: option.value, symbol: option.label, name: option.hint ?? '' } : null);
        }}
        loading={lookup.isFetching}
        noOptionsText={text.trim() === '' ? 'Type a symbol or a name' : 'No share or ETF matches'}
        minWidth={0}
      />
      {problem && <FormHelperText error>{problem}</FormHelperText>}
    </Box>
  );
}
