import { Box, FormHelperText } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Typeahead, type TypeaheadOption } from '@/shared/ui/Typeahead';
import { instrumentLookupQuery } from '../api/dataQueries';
import { problemText } from '../lib/errors';

interface InstrumentPickerProps {
  /** The chosen instrument's id; '' while none is chosen. */
  value: string;
  /** The chosen instrument's id and symbol; '' and null when the choice is cleared. */
  onChange: (id: string, symbol: string | null) => void;
  error?: string;
}

/** Search the instruments with data (symbol or name) as you type, and choose one. */
export function InstrumentPicker({ value, onChange, error }: InstrumentPickerProps) {
  const [text, setText] = useState('');
  const [chosen, setChosen] = useState<TypeaheadOption | null>(null);
  const lookup = useQuery(instrumentLookupQuery(text));
  const found: TypeaheadOption[] = (lookup.data?.items ?? []).map((row) => ({
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
        placeholder="Search by symbol or name"
        options={options}
        value={value === '' ? null : value}
        onSearch={setText}
        onChange={(id) => {
          const option = options.find((candidate) => candidate.value === id) ?? null;
          setChosen(option);
          onChange(id ?? '', option?.label ?? null);
        }}
        loading={lookup.isFetching}
        noOptionsText={text.trim() === '' ? 'Type a symbol or a name' : 'No instrument matches'}
        minWidth={0}
      />
      {problem && <FormHelperText error>{problem}</FormHelperText>}
    </Box>
  );
}
