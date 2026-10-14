import { Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import type { DividendPreviewItem } from '@/shared/api';
import { importPageSize, type Drafts } from '../../lib/dividendImport';
import { ItemRow, type ItemVariant } from './ItemRow';
import { PageNav } from './PageNav';
import { usePaged } from './usePaged';

interface ItemsTableProps {
  label: string;
  items: readonly DividendPreviewItem[];
  variant: ItemVariant;
  drafts: Drafts;
  lines: ReadonlySet<number>;
  onInclude: (line: number, on: boolean) => void;
  onChange: (item: DividendPreviewItem) => void;
  emptyText: string;
}

/**
 * The rows of one status, 50 at a time (a file may hold 20,000 rows; only the page on screen is in the page). The table
 * scrolls sideways inside its own region on a phone, so a long name cannot widen the screen.
 */
export function ItemsTable({ label, items, variant, drafts, lines, onInclude, onChange, emptyText }: ItemsTableProps) {
  const { page, setPage, shown } = usePaged(items);
  if (items.length === 0) return <Typography color="text.secondary">{emptyText}</Typography>;
  return (
    <>
      <TableContainer role="region" aria-label={`${label}, scrolls sideways`} tabIndex={0}>
        <Table size="small" aria-label={label} sx={{ minWidth: 760 }}>
          <TableHead>
            <TableRow>
              {variant === 'duplicates' && <TableCell sx={{ fontWeight: 600 }}>Include</TableCell>}
              <TableCell sx={{ fontWeight: 600 }}>Line</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Pay date</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Name in the file</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Matched to</TableCell>
              <TableCell sx={{ fontWeight: 600, textAlign: 'right' }}>Per share</TableCell>
              <TableCell sx={{ fontWeight: 600, textAlign: 'right' }}>Shares</TableCell>
              <TableCell sx={{ fontWeight: 600, textAlign: 'right' }}>Amount</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Notes</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {shown.map((item) => (
              <ItemRow
                key={item.line}
                item={item}
                variant={variant}
                draft={drafts.get(item.nameKey)}
                included={lines.has(item.line)}
                onInclude={onInclude}
                onChange={onChange}
              />
            ))}
          </TableBody>
        </Table>
      </TableContainer>
      <PageNav noun="rows" total={items.length} page={page} pageSize={importPageSize} onPage={setPage} />
    </>
  );
}
