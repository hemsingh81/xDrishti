import { Chip } from '@mui/material';
import { kindLabel } from '../../lib/reportImport';

/** What the file is, in plain words; a file that cannot be imported here is outlined grey, one that can is blue. */
export function KindChip({ kind, supported }: { kind: string; supported: boolean }) {
  return <Chip size="small" variant="outlined" color={supported ? 'primary' : 'default'} label={kindLabel(kind)} />;
}
