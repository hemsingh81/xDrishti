import { Button, Stack, Typography } from '@mui/material';

/** Previous / next for a server-paged list; "next" is offered while the page was full. */
export function Pager({ page, full, onPage }: { page: number; full: boolean; onPage: (page: number) => void }) {
  if (page === 1 && !full) return null;
  return (
    <Stack direction="row" sx={{ gap: 1, alignItems: 'center', mt: 2 }}>
      <Button
        disabled={page <= 1}
        onClick={() => {
          onPage(page - 1);
        }}
      >
        Previous page
      </Button>
      <Typography variant="body2">Page {page}</Typography>
      <Button
        disabled={!full}
        onClick={() => {
          onPage(page + 1);
        }}
      >
        Next page
      </Button>
    </Stack>
  );
}
