import { MenuItem, TextField } from '@mui/material';
import { lensOptions, readLens, type Lens } from '../lib/lensToQuery';

interface LensSelectorProps {
  lens: Lens;
  onChange: (lens: Lens) => void;
}

/** The lens: what a group-based report is cut by. "By individual trade" waits for P8-11 and "By account" is refused by the server. */
export function LensSelector({ lens, onChange }: LensSelectorProps) {
  return (
    <TextField
      select
      size="small"
      label="Lens"
      value={lens}
      onChange={(event) => {
        onChange(readLens(event.target.value));
      }}
      sx={{ minWidth: 200 }}
    >
      {lensOptions.map((option) => (
        <MenuItem
          key={option.id}
          value={option.id}
          disabled={option.id === 'trade' || option.id === 'account'}
          sx={{ minHeight: 44 }}
        >
          {option.label}
        </MenuItem>
      ))}
    </TextField>
  );
}
