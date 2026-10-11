import PeopleIcon from '@mui/icons-material/PeopleOutlined';
import { Button, Card, CardContent, Stack, Typography } from '@mui/material';

/** First-run state: no accounts yet. */
export function EmptyAccounts({ onAdd }: { onAdd: () => void }) {
  return (
    <Card>
      <CardContent>
        <Stack spacing={1.5} sx={{ alignItems: 'center', textAlign: 'center', py: 4 }}>
          <PeopleIcon color="disabled" fontSize="large" aria-hidden />
          <Typography variant="h2" component="h2">
            No accounts yet
          </Typography>
          <Typography color="text.secondary" sx={{ maxWidth: 520 }}>
            Add your first broker account to get started. You choose what each account is used for — market data,
            trading or portfolio — and set its risk.
          </Typography>
          <Button variant="contained" onClick={onAdd}>
            Add account
          </Button>
        </Stack>
      </CardContent>
    </Card>
  );
}
