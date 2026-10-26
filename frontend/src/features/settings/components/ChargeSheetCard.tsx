import { Button, Card, CardContent, Chip, Stack, TextField, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { LoadingState } from '@/shared/ui';
import { chargeSheetQuery } from '../api/chargeSheetApi';
import { problemText } from '../lib/problems';
import { stateWords } from '../lib/chargeSheetWords';
import { ChargeSheetActions } from './ChargeSheetActions';
import { ChargeSheetHistory } from './ChargeSheetHistory';
import { ChargeSheetPreview } from './ChargeSheetPreview';

/**
 * Settings: the owner's confirmation of the stored charge sheet (P8-23, ADR 0127). Reports and the paper comparison print figures
 * after costs (net) only while it stands. Loaded on demand as the last tab of Settings. The status, the sheet and the history come
 * from the server; the screen decides nothing about whether a confirmation stands.
 */
export default function ChargeSheetCard() {
  const [typed, setTyped] = useState('');
  const [shown, setShown] = useState('');
  const query = useQuery(chargeSheetQuery(shown));
  const status = query.data;

  return (
    <Card component="section" aria-labelledby="charge-sheet-title">
      <CardContent>
        <Stack spacing={2.5}>
          <Stack spacing={0.5}>
            <Typography id="charge-sheet-title" variant="h2" component="h2">
              Charge sheet confirmation
            </Typography>
            {status && <Typography color="text.secondary">{status.meaning}</Typography>}
          </Stack>
          {query.isPending && <LoadingState label="Loading the confirmation" />}
          {query.isError && (
            <Stack spacing={1} sx={{ alignItems: 'flex-start' }}>
              <Typography role="alert" color="error">
                {problemText(query.error, 'The confirmation could not be loaded.')}
              </Typography>
              <Button
                size="small"
                onClick={() => {
                  void query.refetch();
                }}
              >
                Retry
              </Button>
            </Stack>
          )}
          {status && (
            <>
              <Stack direction="row" spacing={1.5} useFlexGap sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
                <Chip
                  label={stateWords[status.state] ?? status.state}
                  color={status.state === 'confirmed' ? 'success' : 'default'}
                />
                <Typography>{status.text}</Typography>
              </Stack>
              <Typography color="text.secondary">
                Confirming is switched {status.capabilityOn ? 'on' : 'off (the default)'}.
              </Typography>
              <Stack
                component="form"
                direction={{ xs: 'column', sm: 'row' }}
                spacing={1}
                onSubmit={(event) => {
                  event.preventDefault();
                  setShown(typed.trim());
                }}
              >
                <TextField
                  label="Charge sheet id"
                  size="small"
                  value={typed}
                  onChange={(event) => {
                    setTyped(event.target.value);
                  }}
                  placeholder={status.inForce?.sheetId ?? 'for example my-broker'}
                  helperText="Leave empty to show the confirmed sheet."
                />
                <Button type="submit" variant="outlined" sx={{ alignSelf: 'flex-start' }}>
                  Show sheet
                </Button>
              </Stack>
              {status.sheetNote && <Typography role="status">{status.sheetNote}</Typography>}
              {status.sheet && <ChargeSheetPreview sheet={status.sheet} />}
              <ChargeSheetActions status={status} shown={shown} />
              <Typography variant="h3" component="h3">
                Acts
              </Typography>
              <ChargeSheetHistory history={status.history} />
            </>
          )}
        </Stack>
      </CardContent>
    </Card>
  );
}
