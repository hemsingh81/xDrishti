import { Alert, Button, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { lazy, Suspense, useState } from 'react';
import { ErrorState, LoadingState } from '@/shared/ui';
import { complianceQuery } from '../api/complianceQueries';
import { noteSentence, runbookPath } from '../lib/labels';
import { ComplianceCards } from './ComplianceCards';
import { ComplianceHistory } from './ComplianceHistory';

// The dialog holds the only write of the feature, so it loads only when the owner opens it.
const ComplianceDialog = lazy(() => import('./ComplianceDialog').then((m) => ({ default: m.ComplianceDialog })));

/**
 * The Compliance tab of the Live screen (spec P11-05): the owner's dated notes about the static IP, the SEBI re-check and Dhan's IP rule for reads.
 * It reads only the API of the records and calls no broker; a record is written in the dialog, with the password typed again.
 */
export function ComplianceTab() {
  const q = useQuery(complianceQuery);
  const [open, setOpen] = useState(false);
  if (q.isPending) return <LoadingState label="Loading the compliance records…" />;
  if (q.isError) return <ErrorState error={q.error} />;
  const { items, latest, writesEnabled } = q.data;
  return (
    <Stack sx={{ gap: 2 }}>
      <Typography>
        {noteSentence} The questions and the checks are in the runbook: <code>{runbookPath}</code>.
      </Typography>
      {!writesEnabled && (
        <Alert severity="info">
          Writing records is switched off in this deployment (<code>Live__Writes__Enabled</code> is false). You can read
          the records; the switch is yours and is changed in the deployment.
        </Alert>
      )}
      <div>
        <Button
          variant="contained"
          disabled={!writesEnabled}
          onClick={() => {
            setOpen(true);
          }}
        >
          New record
        </Button>
      </div>
      <ComplianceCards latest={latest} />
      <ComplianceHistory items={items} />
      {open && (
        <Suspense fallback={null}>
          <ComplianceDialog
            onClose={() => {
              setOpen(false);
            }}
          />
        </Suspense>
      )}
    </Stack>
  );
}
