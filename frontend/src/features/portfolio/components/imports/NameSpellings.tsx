import { Typography } from '@mui/material';
import { spellingsShown } from '../../lib/dividendImport';
import type { NameGroup } from '../../lib/reportImport';

/** The spellings of a name in the file (up to three, then how many more), the symbol written beside it and the securities an ambiguous name fits. */
export function NameSpellings({ group }: { group: NameGroup }) {
  const hidden = group.spellings.length - spellingsShown;
  return (
    <>
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
      {group.symbolHint && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {`Symbol in the file: ${group.symbolHint}`}
        </Typography>
      )}
      {group.candidates.length > 1 && (
        <Typography variant="caption" color="text.secondary" sx={{ display: 'block' }}>
          {`Ambiguous: fits ${group.candidates.map((c) => c.symbol ?? c.name).join(', ')}`}
        </Typography>
      )}
    </>
  );
}
