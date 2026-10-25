import { Box, Link, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { dimensionReasonQuery } from '../api/reportQueries';
import type { CatalogueEntry } from '../lib/reportCatalogue';

/**
 * A report the stored data cannot serve (yet): its name, the missing input and the slice or phase that supplies it, never a chart
 * or a figure. Where the server states the reason (regime, account) the server's own words are used.
 */
export function PointerEntry({ entry }: { entry: CatalogueEntry }) {
  const { dimension } = entry;
  const server = useQuery({ ...dimensionReasonQuery(dimension ?? 'regime'), enabled: dimension !== undefined });
  const reason = (dimension === undefined ? null : server.data) ?? entry.reason;
  return (
    <Box sx={{ px: 1, py: 0.5, color: 'text.secondary' }}>
      <Typography variant="body2">{entry.label}</Typography>
      <Typography variant="caption" component="p">
        {entry.status === 'unavailable' ? 'Not available' : 'Planned'}, {entry.slice}: {reason}
      </Typography>
      {entry.link && (
        <Link component={RouterLink} to={entry.link.to} variant="caption">
          {entry.link.label}
        </Link>
      )}
    </Box>
  );
}
