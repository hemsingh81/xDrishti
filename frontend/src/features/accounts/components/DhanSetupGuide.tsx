import ExpandLessIcon from '@mui/icons-material/ExpandLess';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import InfoIcon from '@mui/icons-material/InfoOutlined';
import { Box, Button, Collapse, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useId, useState } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { dhanRedirectQuery } from '../api/accountsQueries';
import { dhanApp } from '../lib/dhanApp';
import { RedirectUrl } from './RedirectUrl';

/** How to create the Dhan app (from Dhan's documentation): numbered steps, the redirect URL and notes. */
function GuideContent({ headingId }: { headingId: string }) {
  const redirect = useQuery(dhanRedirectQuery);
  return (
    <Stack spacing={2}>
      <Box component="ol" aria-labelledby={headingId} sx={{ m: 0, pl: 3, display: 'grid', gap: 0.75 }}>
        {dhanApp.setupSteps.map((step) => (
          <Typography component="li" key={step}>
            {step}
          </Typography>
        ))}
      </Box>
      {redirect.isPending && <LoadingState label="Loading the redirect URL" />}
      {redirect.error && <ErrorState error={redirect.error} />}
      {redirect.data && <RedirectUrl url={redirect.data.redirectUrl} />}
      <Stack component="ul" aria-label={`${dhanApp.name} notes`} spacing={0.5} sx={{ m: 0, p: 0, listStyle: 'none' }}>
        {dhanApp.notes.map((note) => (
          <Stack component="li" key={note} direction="row" spacing={1} sx={{ alignItems: 'flex-start' }}>
            <InfoIcon fontSize="small" color="info" aria-hidden sx={{ mt: 0.25 }} />
            <Typography variant="body2" color="text.secondary">
              {note}
            </Typography>
          </Stack>
        ))}
      </Stack>
    </Stack>
  );
}

const title = `Set up the ${dhanApp.name} app`;

/** The Dhan setup guide; `collapsible` hides it behind a disclosure button (the Connection tab). */
export function DhanSetupGuide({ collapsible = false }: { collapsible?: boolean }) {
  const headingId = useId();
  const panelId = useId();
  const [open, setOpen] = useState(!collapsible);

  if (!collapsible) {
    return (
      <Box component="section" aria-labelledby={headingId}>
        <Typography id={headingId} variant="h3" sx={{ mb: 1 }}>
          {title}
        </Typography>
        <GuideContent headingId={headingId} />
      </Box>
    );
  }
  return (
    <Box component="section" aria-labelledby={headingId}>
      <Button
        aria-expanded={open}
        aria-controls={open ? panelId : undefined}
        endIcon={open ? <ExpandLessIcon /> : <ExpandMoreIcon />}
        onClick={() => {
          setOpen((o) => !o);
        }}
        sx={{ px: 0 }}
      >
        <span id={headingId}>{title}</span>
      </Button>
      <Collapse in={open} id={panelId} unmountOnExit>
        <Box sx={{ pt: 1 }}>
          <GuideContent headingId={headingId} />
        </Box>
      </Collapse>
    </Box>
  );
}
