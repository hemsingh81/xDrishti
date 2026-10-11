/**
 * Typeahead — a searchable single-choice dropdown over a long list (sectors, baskets, accounts, series), on MUI
 * Autocomplete. Import it by path (`@/shared/ui/Typeahead`), not through the `@/shared/ui` barrel, so Autocomplete stays
 * out of the initial bundle.
 *
 * Props: `options` (`value`, `label`, optional right-aligned `count` and `hint`, `disabled`), `value` (an option's
 * value or null) + `onChange`, `label`, `placeholder` (what "no value" means, e.g. "All sectors"), `clearable`
 * (default true), `loading`, `noOptionsText`, `getOptionDisabled`, `minWidth`. Typing filters the options (case-
 * insensitive substring) and highlights the match. A value that is not among the options (no rows right now) stays
 * selected and visible. No free text: the choice is always one of the options.
 *
 * Semantics: combobox with a listbox of options (arrow keys, Enter to choose, Escape to close or, when closed, clear),
 * named by `label`; emptying the text clears the value too.
 */
import { Autocomplete, Box, CircularProgress, TextField, Typography, createFilterOptions } from '@mui/material';
import { Fragment, type ReactNode } from 'react';
import { formatNumber } from '@/shared/lib/format';

export interface TypeaheadOption {
  value: string;
  label: string;
  /** Right-aligned number, e.g. how many rows have this value. */
  count?: number;
  /** Secondary text under the label. */
  hint?: string;
  disabled?: boolean;
}

export interface TypeaheadProps {
  options: readonly TypeaheadOption[];
  value: string | null;
  onChange: (value: string | null) => void;
  label: string;
  placeholder?: string;
  clearable?: boolean;
  loading?: boolean;
  noOptionsText?: string;
  getOptionDisabled?: (option: TypeaheadOption) => boolean;
  /** Width of the field in px (it never gets narrower than this). */
  minWidth?: number;
}

/** Case-insensitive substring on the label, ignoring leading/trailing spaces of the typed text; accents count. */
const filterOptions = createFilterOptions<TypeaheadOption>({
  ignoreCase: true,
  ignoreAccents: false,
  trim: true,
  matchFrom: 'any',
});

/** Offset of the first case-insensitive occurrence of `needle` in `text`, or -1; compares same-length slices of `text`. */
function matchAt(text: string, needle: string): number {
  const wanted = needle.toLowerCase();
  for (let start = 0; start + needle.length <= text.length; start += 1) {
    if (text.slice(start, start + needle.length).toLowerCase() === wanted) return start;
  }
  return -1;
}

/**
 * The label with the typed text emphasised, using the same rule as the filter (trimmed, case-insensitive substring).
 * It only ever splits the label's own characters, so special characters in the query or the label stay plain text.
 */
function Highlighted({ text, query }: { text: string; query: string }): ReactNode {
  const needle = query.trim();
  const at = needle ? matchAt(text, needle) : -1;
  if (at < 0) return text;
  return (
    <Fragment>
      {text.slice(0, at)}
      <Box component="mark" sx={{ bgcolor: 'transparent', color: 'primary.main', fontWeight: 700 }}>
        {text.slice(at, at + needle.length)}
      </Box>
      {text.slice(at + needle.length)}
    </Fragment>
  );
}

/** Name of an option for assistive technology: "Unclassified 3,630" (label, hint, count). */
const optionName = (option: TypeaheadOption) =>
  [option.label, option.hint, option.count === undefined ? undefined : formatNumber(option.count)]
    .filter((part): part is string => part !== undefined)
    .join(' ');

export function Typeahead({
  options,
  value,
  onChange,
  label,
  placeholder,
  clearable = true,
  loading = false,
  noOptionsText = 'No matches',
  getOptionDisabled,
  minWidth = 220,
}: TypeaheadProps) {
  const known = options.find((o) => o.value === value);
  const kept: TypeaheadOption | null = value === null ? null : (known ?? { value, label: value });
  const all = known || kept === null ? options : [kept, ...options];

  return (
    <Autocomplete
      size="small"
      options={all}
      value={kept}
      loading={loading}
      loadingText="Loading…"
      noOptionsText={noOptionsText}
      disableClearable={!clearable}
      clearOnEscape
      clearText={`Clear ${label}`}
      openText={`Open ${label}`}
      closeText={`Close ${label}`}
      filterOptions={filterOptions}
      isOptionEqualToValue={(a, b) => a.value === b.value}
      getOptionLabel={(option) => option.label}
      getOptionDisabled={(option) => option.disabled === true || (getOptionDisabled?.(option) ?? false)}
      onChange={(_event, next) => {
        onChange(next?.value ?? null);
      }}
      renderInput={(params) => (
        <TextField
          {...params}
          label={label}
          placeholder={placeholder}
          slotProps={{
            ...params.slotProps,
            inputLabel: { ...params.slotProps.inputLabel, shrink: true },
            input: {
              ...params.slotProps.input,
              endAdornment: (
                <Fragment>
                  {loading && <CircularProgress size={16} aria-label="Loading options" />}
                  {params.slotProps.input.endAdornment}
                </Fragment>
              ),
            },
          }}
        />
      )}
      renderOption={({ key, ...props }, option, { inputValue, selected }) => (
        <Box
          component="li"
          key={key}
          {...props}
          aria-label={optionName(option)}
          sx={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 2 }}
        >
          <Box component="span" sx={{ minWidth: 0 }}>
            <Highlighted text={option.label} query={selected && inputValue === option.label ? '' : inputValue} />
            {option.hint && (
              <Typography component="span" variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                {option.hint}
              </Typography>
            )}
          </Box>
          {option.count !== undefined && (
            <Typography component="span" variant="body2" color="text.secondary" className="numeric">
              {formatNumber(option.count)}
            </Typography>
          )}
        </Box>
      )}
      sx={{ minWidth, flex: '0 1 auto' }}
    />
  );
}
