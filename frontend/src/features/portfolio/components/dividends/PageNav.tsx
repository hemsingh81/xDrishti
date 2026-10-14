import { Button, Stack, Typography } from '@mui/material';
import { useRef } from 'react';
import { formatNumber } from '@/shared/lib/format';

interface PageNavProps {
  /** What the rows are called in the status line: "rows", "names". */
  noun: string;
  total: number;
  page: number;
  pageSize: number;
  onPage: (page: number) => void;
}

/** Previous / Next over a long list that is shown a page at a time, with the range on screen as a status line. */
export function PageNav({ noun, total, page, pageSize, onPage }: PageNavProps) {
  const pages = Math.max(1, Math.ceil(total / pageSize));
  const first = total === 0 ? 0 : page * pageSize + 1;
  const last = Math.min(total, (page + 1) * pageSize);
  const statusRef = useRef<HTMLSpanElement>(null);
  const go = (target: number) => {
    // The button that was pressed is disabled on the end page: focus stays on the page, at the line that says where it is.
    if (target === 0 || target === pages - 1) statusRef.current?.focus();
    onPage(target);
  };
  return (
    <Stack direction="row" sx={{ alignItems: 'center', gap: 1, mt: 1, flexWrap: 'wrap' }}>
      <Button
        size="small"
        disabled={page === 0}
        onClick={() => {
          go(page - 1);
        }}
      >
        Previous page
      </Button>
      <Button
        size="small"
        disabled={page >= pages - 1}
        onClick={() => {
          go(page + 1);
        }}
      >
        Next page
      </Button>
      <Typography
        ref={statusRef}
        tabIndex={-1}
        variant="caption"
        color="text.secondary"
        role="status"
        className="numeric"
      >
        {`Showing ${noun} ${formatNumber(first)}–${formatNumber(last)} of ${formatNumber(total)}`}
      </Typography>
    </Stack>
  );
}
