import { Box, Chip, MenuItem, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatIstDate } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { optionCoverageQuery, optionUnderlyings } from '../api/qualityQueries';

/** Read-only: how complete and usable the recorded option archive is, per session and check. Never a price, IV or Greek. */
// shortcut: no virtual list; a long archive is capped at this many rows with a count of the rest.
const maxRows = 200;

export function OptionsArchiveCard() {
  const [underlying, setUnderlying] = useState<string>(optionUnderlyings[0]);
  const query = useQuery(optionCoverageQuery(underlying));
  const data = query.data;

  return (
    <Box component="section" aria-labelledby="options-archive-heading" sx={{ p: 2 }}>
      <Typography id="options-archive-heading" component="h2" variant="subtitle1" sx={{ fontWeight: 600 }}>
        Options archive
      </Typography>
      <TextField
        select
        size="small"
        label="Underlying"
        value={underlying}
        onChange={(e) => {
          setUnderlying(e.target.value);
        }}
        sx={{ my: 1, minWidth: 180 }}
      >
        {optionUnderlyings.map((u) => (
          <MenuItem key={u} value={u}>
            {u}
          </MenuItem>
        ))}
      </TextField>
      {query.isPending && <LoadingState label="Loading the options archive checks" />}
      {query.isError && <ErrorState error={query.error} />}
      {data && (
        <>
          <Typography variant="body2" sx={{ mb: 1 }}>
            {`Usable chain sessions: ${String(data.usableChainSessions)}`}
          </Typography>
          {data.truncated && (
            <Typography variant="body2" color="text.secondary" sx={{ mb: 1 }}>
              More sessions exist than are listed; the oldest are listed first.
            </Typography>
          )}
          {data.sessions.length === 0 ? (
            <Typography variant="body2" color="text.secondary">
              Nothing is recorded for {data.underlying} in the last 30 days yet, so there is nothing to check.
            </Typography>
          ) : (
            <Box component="ul" aria-label="Option archive sessions" sx={{ m: 0, p: 0, listStyle: 'none' }}>
              {data.sessions.slice(0, maxRows).map((s) => (
                <Box
                  component="li"
                  key={`${s.date}-${s.source}-${s.fetchKey ?? ''}`}
                  sx={{ display: 'flex', gap: 1, alignItems: 'center', flexWrap: 'wrap', py: 0.5 }}
                >
                  <Typography variant="body2" sx={{ fontWeight: 600 }}>
                    {`${formatIstDate(s.date)}, ${s.source}${s.fetchKey ? ` ${s.fetchKey}` : ''}`}
                  </Typography>
                  <Chip size="small" variant="outlined" label={s.usable ? 'Usable' : 'Not usable'} />
                  {s.checks.map((c) => (
                    <Chip
                      key={c.code}
                      size="small"
                      variant="outlined"
                      label={`${c.code}: ${c.status}${c.reason ? ` (${c.reason})` : ''}`}
                    />
                  ))}
                </Box>
              ))}
            </Box>
          )}
          {data.sessions.length > maxRows && (
            <Typography variant="body2" color="text.secondary">
              {`${String(data.sessions.length - maxRows)} more sessions not shown`}
            </Typography>
          )}
        </>
      )}
    </Box>
  );
}
