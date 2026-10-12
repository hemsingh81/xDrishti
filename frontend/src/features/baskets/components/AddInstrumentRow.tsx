import { Button, Stack, TextField } from '@mui/material';
import { useState } from 'react';

/**
 * The prototype's inline add: a symbol field and an Add button (Enter works too). The multi-select dialog does the
 * adding, opened with what was typed as its search.
 */
export function AddInstrumentRow({ onAdd }: { onAdd: (query: string) => void }) {
  const [text, setText] = useState('');
  return (
    <Stack
      component="form"
      direction="row"
      spacing={1}
      noValidate
      onSubmit={(event) => {
        event.preventDefault();
        onAdd(text.trim());
        setText('');
      }}
      sx={{ px: 2, pb: 1.5 }}
    >
      <TextField
        size="small"
        fullWidth
        value={text}
        placeholder="Add instrument (symbol)…"
        onChange={(event) => {
          setText(event.target.value);
        }}
        slotProps={{ htmlInput: { 'aria-label': 'Add instrument', autoComplete: 'off' } }}
      />
      <Button type="submit" variant="contained">
        Add
      </Button>
    </Stack>
  );
}
