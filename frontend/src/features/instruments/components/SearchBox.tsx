import ClearIcon from '@mui/icons-material/Clear';
import SearchIcon from '@mui/icons-material/Search';
import { IconButton, InputAdornment, TextField } from '@mui/material';
import { useEffect, useEffectEvent, useState } from 'react';

const debounceMs = 300;

interface SearchBoxProps {
  /** The committed search text (from the URL). */
  value: string;
  onCommit: (value: string) => void;
}

/** Search field: commits to the URL 300 ms after the last keystroke; follows the URL when it changes (Back, clear all). */
export function SearchBox({ value, onCommit }: SearchBoxProps) {
  const [draft, setDraft] = useState(value);
  const [seen, setSeen] = useState(value);
  if (value !== seen) {
    // The URL changed from elsewhere: show it. (Our own commits already match the draft.)
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
      placeholder="Search symbol, name or ISIN"
      onChange={(event) => {
        setDraft(event.target.value);
      }}
      slotProps={{
        htmlInput: { 'aria-label': 'Search instruments', autoComplete: 'off' },
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
