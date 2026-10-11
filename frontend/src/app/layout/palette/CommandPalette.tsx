import SearchIcon from '@mui/icons-material/Search';
import { Box, InputAdornment, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useId, useMemo, useRef, useState } from 'react';
import { useNavigate } from 'react-router';
import { accountsQuery } from '@/features/accounts';
import { IconChip } from '@/shared/ui';
import { AppDialog, AppDialogBody, AppDialogHeader } from '@/shared/ui/dialog';
import { useColorMode } from '@/theme';
import { buildPaletteItems, filterPaletteItems, paletteGroups, type PaletteItem } from './paletteItems';

interface CommandPaletteProps {
  open: boolean;
  onClose: () => void;
}

/** "Search or jump to…": one text field over pages, accounts and actions (combobox + listbox, arrow keys, Enter). */
export default function CommandPalette({ open, onClose }: CommandPaletteProps) {
  const navigate = useNavigate();
  const { toggle } = useColorMode();
  const { data: accounts } = useQuery(accountsQuery);
  const [query, setQuery] = useState('');
  const [active, setActive] = useState(0);
  const inputRef = useRef<HTMLInputElement>(null);
  const listId = useId();
  const optionId = (item: PaletteItem) => `${listId}-${item.id}`;

  const shown = useMemo(
    () =>
      filterPaletteItems(
        buildPaletteItems({ accounts: accounts ?? [], go: (to) => void navigate(to), toggleMode: toggle }),
        query,
      ),
    [accounts, navigate, toggle, query],
  );
  const current = shown[Math.min(active, shown.length - 1)];

  const choose = (item: PaletteItem | undefined) => {
    if (item) {
      onClose();
      item.run();
    }
  };
  const move = (delta: number) => {
    setActive(Math.max(0, Math.min(shown.length - 1, active + delta)));
  };

  return (
    <AppDialog open={open} onClose={onClose} initialFocusRef={inputRef}>
      <AppDialogHeader>Search or jump to</AppDialogHeader>
      <AppDialogBody>
        <TextField
          fullWidth
          size="small"
          placeholder="Search pages, accounts and actions…"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value);
            setActive(0);
          }}
          onKeyDown={(event) => {
            if (event.key === 'ArrowDown' || event.key === 'ArrowUp') {
              event.preventDefault();
              move(event.key === 'ArrowDown' ? 1 : -1);
            } else if (event.key === 'Enter') {
              event.preventDefault();
              choose(current);
            }
          }}
          slotProps={{
            input: {
              startAdornment: (
                <InputAdornment position="start">
                  <SearchIcon fontSize="small" />
                </InputAdornment>
              ),
            },
            htmlInput: {
              role: 'combobox',
              'aria-label': 'Search or jump to',
              'aria-expanded': true,
              'aria-controls': listId,
              'aria-autocomplete': 'list',
              'aria-activedescendant': current ? optionId(current) : undefined,
              ref: inputRef,
              autoComplete: 'off',
              spellCheck: false,
            },
          }}
        />
        <Box id={listId} role="listbox" aria-label="Results" sx={{ mt: 1.5 }}>
          {paletteGroups.map((group) => {
            const items = shown.filter((item) => item.group === group);
            return items.length === 0 ? null : (
              <Box key={group} role="group" aria-label={group}>
                <Typography
                  variant="overline"
                  color="text.secondary"
                  aria-hidden
                  sx={{ display: 'block', px: 1, pt: 1 }}
                >
                  {group}
                </Typography>
                {items.map((item) => (
                  <PaletteOption
                    key={item.id}
                    id={optionId(item)}
                    item={item}
                    selected={item === current}
                    onHover={() => {
                      setActive(shown.indexOf(item));
                    }}
                    onChoose={() => {
                      choose(item);
                    }}
                  />
                ))}
              </Box>
            );
          })}
        </Box>
        {shown.length === 0 && (
          <Typography role="status" color="text.secondary" sx={{ py: 3, textAlign: 'center' }}>
            No matches
          </Typography>
        )}
      </AppDialogBody>
    </AppDialog>
  );
}

interface PaletteOptionProps {
  id: string;
  item: PaletteItem;
  selected: boolean;
  onHover: () => void;
  onChoose: () => void;
}

function PaletteOption({ id, item, selected, onHover, onChoose }: PaletteOptionProps) {
  return (
    <Stack
      id={id}
      role="option"
      aria-selected={selected}
      tabIndex={-1}
      direction="row"
      spacing={1.5}
      onClick={onChoose}
      onMouseMove={selected ? undefined : onHover}
      ref={(node: HTMLElement | null) => {
        if (selected && node && 'scrollIntoView' in node) node.scrollIntoView({ block: 'nearest' });
      }}
      sx={{
        alignItems: 'center',
        px: 1,
        py: 0.75,
        borderRadius: 1,
        cursor: 'pointer',
        bgcolor: selected ? 'action.selected' : undefined,
      }}
    >
      <IconChip icon={item.icon} hue={item.hue} />
      <Typography sx={{ flex: 1, minWidth: 0, fontWeight: 600 }}>{item.label}</Typography>
      <Typography variant="caption" color="text.secondary" sx={{ textAlign: 'right' }}>
        {item.hint}
      </Typography>
    </Stack>
  );
}
