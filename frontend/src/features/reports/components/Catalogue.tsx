import { Box, Button, Typography } from '@mui/material';
import { PointerEntry } from './PointerEntry';
import { reportCatalogue } from '../lib/reportCatalogue';
import type { ReportView } from '../lib/reportFilter';

interface CatalogueProps {
  view: ReportView;
  onOpen: (view: ReportView) => void;
}

/** The reports: built ones open; the rest are listed with the slice that builds them and the reason they are not there yet. */
export function Catalogue({ view, onOpen }: CatalogueProps) {
  return (
    <Box component="nav" aria-label="Report catalogue">
      <Typography variant="h2" component="h2" sx={{ mb: 1 }}>
        Reports
      </Typography>
      <Box component="ul" sx={{ listStyle: 'none', m: 0, p: 0, display: 'grid', gap: 0.5 }}>
        {reportCatalogue.map((entry) => (
          <li key={entry.id}>
            {entry.status === 'built' && entry.view ? (
              <Button
                fullWidth
                variant={entry.view === view ? 'contained' : 'text'}
                aria-current={entry.view === view ? 'page' : undefined}
                onClick={() => {
                  if (entry.view) onOpen(entry.view);
                }}
                sx={{ justifyContent: 'flex-start', minHeight: 44 }}
              >
                {entry.label}
              </Button>
            ) : (
              <PointerEntry entry={entry} />
            )}
          </li>
        ))}
      </Box>
    </Box>
  );
}
