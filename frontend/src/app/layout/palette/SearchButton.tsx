import SearchIcon from '@mui/icons-material/Search';
import { Box, Button } from '@mui/material';
import { lazy, Suspense, useEffect, useRef, useState } from 'react';

// The dialog and its data code load on first use; the button itself is tiny.
const CommandPalette = lazy(() => import('./CommandPalette'));

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.userAgent);
const hint = isMac ? '⌘K' : 'Ctrl K';

/** The "Search or jump to…" trigger (icon only on small screens) and the ⌘K / Ctrl+K shortcut that opens the palette. */
export function SearchButton() {
  const [open, setOpen] = useState(false);
  // Counts openings; it keys the palette so every opening starts with an empty search.
  const [openings, setOpenings] = useState(0);
  const loaded = openings > 0;
  const trigger = useRef<HTMLButtonElement>(null);
  const wasOpen = useRef(false);

  const show = () => {
    setOpenings((n) => n + 1);
    setOpen(true);
  };

  useEffect(() => {
    const onKey = (event: KeyboardEvent) => {
      if ((event.metaKey || event.ctrlKey) && event.key.toLowerCase() === 'k') {
        event.preventDefault();
        setOpenings((n) => n + 1);
        setOpen(true);
      }
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
    };
  }, []);

  // Whatever opened the palette (button or shortcut), closing returns focus to the trigger.
  useEffect(() => {
    if (open) {
      wasOpen.current = true;
    } else if (wasOpen.current) {
      wasOpen.current = false;
      trigger.current?.focus();
    }
  }, [open]);

  return (
    <>
      <Button
        ref={trigger}
        color="inherit"
        size="small"
        variant="outlined"
        aria-haspopup="dialog"
        aria-label="Search or jump to"
        title={`Search or jump to… (${hint})`}
        onClick={show}
        startIcon={<SearchIcon />}
        sx={{
          minWidth: { xs: 40, lg: 240 },
          // The prototype's `.search-btn`: 34 px high, radius 8, on the alternate surface.
          height: 34,
          borderRadius: '8px',
          fontSize: '13px',
          fontWeight: 400,
          px: { xs: 0, lg: '10px' },
          justifyContent: { xs: 'center', lg: 'flex-start' },
          color: 'text.tertiary',
          borderColor: 'divider',
          bgcolor: 'background.alt',
          boxShadow: 'none',
          '&:hover': { bgcolor: 'background.paper', color: 'text.secondary', borderColor: 'divider' },
          // Icon only below lg: the label and shortcut hint need the room.
          '& .MuiButton-startIcon': { mr: { xs: 0, lg: 1 }, ml: { xs: 0, lg: -0.5 } },
        }}
      >
        <Box component="span" sx={{ display: { xs: 'none', lg: 'flex' }, flex: 1, alignItems: 'center', gap: 1 }}>
          <Box component="span" sx={{ flex: 1, textAlign: 'left' }}>
            Search or jump to…
          </Box>
          <Box component="kbd" aria-hidden sx={{ fontFamily: 'inherit', fontSize: 12 }}>
            {hint}
          </Box>
        </Box>
      </Button>
      {loaded && (
        <Suspense fallback={null}>
          <CommandPalette
            key={openings}
            open={open}
            onClose={() => {
              setOpen(false);
            }}
          />
        </Suspense>
      )}
    </>
  );
}
