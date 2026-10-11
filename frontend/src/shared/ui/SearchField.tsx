import ClearIcon from '@mui/icons-material/Clear';
import SearchIcon from '@mui/icons-material/Search';
import { IconButton, InputAdornment, TextField } from '@mui/material';
import { useEffect, useEffectEvent, useState } from 'react';

const debounceMs = 300;

interface SearchFieldProps {
  /** The committed search text (usually from the URL). */
  value: string;
  onCommit: (value: string) => void;
  /** Accessible name of the field, e.g. "Search holdings". */
  label: string;
  placeholder?: string;
}

/**
 * Search field for list screens: commits 300 ms after the last keystroke, follows `value` when it changes from outside
 * (Back, "clear all filters") and has a clear button. Import it by path (`@/shared/ui/SearchField`).
 */
export function SearchField({ value, onCommit, label, placeholder }: SearchFieldProps) {
  const [draft, setDraft] = useState(value);
  const [seen, setSeen] = useState(value);
  if (value !== seen) {
    // The value changed from elsewhere: show it. (Our own commits already match the draft.)
    setSeen(value);
    setDraft(value);
  }

  const commit = useEffectEvent((text: string) => {
    onCommit(text);
  });
  useEffect(() => {
    if (draft === value) return undefined;
    const timer = setTimeout(() => {
      commit(draft);
    }, debounceMs);
    return () => {
      clearTimeout(timer);
    };
  }, [draft, value]);

  return (
    <TextField
      type="search"
      size="small"
      value={draft}
      placeholder={placeholder}
      onChange={(event) => {
        setDraft(event.target.value);
      }}
      slotProps={{
        htmlInput: { 'aria-label': label, autoComplete: 'off' },
        input: {
          startAdornment: (
            <InputAdornment position="start">
              <SearchIcon fontSize="small" aria-hidden />
            </InputAdornment>
          ),
          endAdornment: draft ? (
            <InputAdornment position="end">
              <IconButton
                size="small"
                edge="end"
                aria-label="Clear search"
                onClick={() => {
                  setDraft('');
                  onCommit('');
                }}
              >
                <ClearIcon fontSize="small" />
              </IconButton>
            </InputAdornment>
          ) : null,
        },
      }}
      sx={{ minWidth: { xs: '100%', sm: 320 }, flex: { sm: '1 1 320px' } }}
    />
  );
}
