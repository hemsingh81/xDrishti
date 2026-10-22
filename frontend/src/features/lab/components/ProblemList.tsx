import { Alert, List, ListItem, ListItemText } from '@mui/material';
import type { StrategyValidation } from '../api/labQueries';
import { placeOf } from '../lib/markers';

/** The answer of validate: "Valid" with the hash, or every located problem (severity in words, not colour alone). */
export function ProblemList({ result }: { result: StrategyValidation }) {
  return (
    <>
      {result.valid && (
        <Alert severity="success" sx={{ mb: result.problems.length ? 1 : 0 }}>
          Valid{result.hash ? ` - hash ${result.hash.slice(0, 12)}` : ''}
        </Alert>
      )}
      {result.problems.length > 0 && (
        <List dense aria-label="Problems" disablePadding>
          {result.problems.map((p, i) => (
            <ListItem key={`${p.code}-${p.path}-${i}`} disableGutters>
              <ListItemText
                primary={`${p.severity}: ${p.message}`}
                secondary={[p.code, p.path, placeOf(p)].filter(Boolean).join(' · ')}
              />
            </ListItem>
          ))}
        </List>
      )}
    </>
  );
}
