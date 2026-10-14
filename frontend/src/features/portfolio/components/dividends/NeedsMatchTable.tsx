import { Button, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { formatInrPrecise, formatNumber } from '@/shared/lib/format';
import { numeric } from '@/theme';
import {
  importPageSize,
  spellingsShown,
  spellingsText,
  targetText,
  type Drafts,
  type NameGroup,
} from '../../lib/dividendImport';
import { PageNav } from './PageNav';
import { usePaged } from './usePaged';

interface NeedsMatchTableProps {
  groups: readonly NameGroup[];
  drafts: Drafts;
  onChoose: (group: NameGroup) => void;
}

/**
 * The names nothing (or more than one holding) fits, one line per name with its rows and rupees. A name without a pick is stored
 * unallocated with the file's name; choosing a holding, an instrument or "leave unallocated" is the owner's decision per name.
 */
export function NeedsMatchTable({ groups, drafts, onChoose }: NeedsMatchTableProps) {
  const { page, setPage, shown } = usePaged(groups);
  if (groups.length === 0) return <Typography color="text.secondary">Every name of the file was matched.</Typography>;
  return (
    <>
      <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
        These names match no holding, or more than one. A name you leave alone is stored unallocated with the file’s
        name; choose a holding to allocate it.
      </Typography>
      <TableContainer role="region" aria-label="Names that need a match, scrolls sideways" tabIndex={0}>
        <Table size="small" aria-label="Names that need a match" sx={{ minWidth: 640 }}>
          <TableHead>
            <TableRow>
              <TableCell sx={{ fontWeight: 600 }}>Name in the file</TableCell>
              <TableCell sx={{ fontWeight: 600, textAlign: 'right' }}>Rows</TableCell>
              <TableCell sx={{ fontWeight: 600, textAlign: 'right' }}>Amount</TableCell>
              <TableCell sx={{ fontWeight: 600 }}>Your choice</TableCell>
              <TableCell sx={{ fontWeight: 600, textAlign: 'right' }}>Action</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {shown.map((group) => {
              const draft = drafts.get(group.key);
              const hidden = group.spellings.length - spellingsShown;
              return (
                <TableRow key={group.key}>
                  <TableCell sx={{ overflowWrap: 'anywhere', verticalAlign: 'top' }}>
                    {group.spellings.slice(0, spellingsShown).map((spelling) => (
                      <Typography key={spelling} component="span" sx={{ display: 'block' }}>
                        {spelling}
                      </Typography>
                    ))}
                    {hidden > 0 && (
                      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                        {`+${String(hidden)} more`}
                      </Typography>
                    )}
                    {group.candidates.length > 1 && (
                      <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                        {`Ambiguous: fits ${group.candidates.map((c) => c.symbol).join(', ')}`}
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell sx={{ ...numeric, textAlign: 'right', verticalAlign: 'top' }}>
                    {formatNumber(group.rows)}
                  </TableCell>
                  <TableCell sx={{ ...numeric, textAlign: 'right', whiteSpace: 'nowrap', verticalAlign: 'top' }}>
                    {formatInrPrecise(group.amount)}
                  </TableCell>
                  <TableCell sx={{ verticalAlign: 'top' }}>
                    {draft ? (
                      <>
                        <Typography component="span" sx={{ fontWeight: 600 }}>
                          {targetText(draft.target)}
                        </Typography>
                        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
                          {draft.remember ? 'remembered for later files' : 'this file only'}
                        </Typography>
                      </>
                    ) : (
                      <Typography variant="body2" color="text.secondary">
                        Not chosen: stored unallocated
                      </Typography>
                    )}
                  </TableCell>
                  <TableCell sx={{ textAlign: 'right', verticalAlign: 'top' }}>
                    <Button
                      size="small"
                      variant={draft ? 'text' : 'outlined'}
                      aria-label={`${draft ? 'Change the choice for' : 'Choose a holding for'} ${spellingsText(group.spellings)}`}
                      onClick={() => {
                        onChoose(group);
                      }}
                    >
                      {draft ? 'Change' : 'Choose'}
                    </Button>
                  </TableCell>
                </TableRow>
              );
            })}
          </TableBody>
        </Table>
      </TableContainer>
      <PageNav noun="names" total={groups.length} page={page} pageSize={importPageSize} onPage={setPage} />
    </>
  );
}
