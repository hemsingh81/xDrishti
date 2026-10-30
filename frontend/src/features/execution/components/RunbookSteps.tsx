import { Card, CardContent, CardHeader, List, ListItem, ListItemText } from '@mui/material';

const steps = [
  'Check the router WAN address and your ISP line, and compare it with the address saved at Dhan (Profile, Get Trading & Data APIs).',
  'If you added or changed the IP after your last login, Connect Dhan again once.',
  "In Dhan's web app use the sandbox tool (IP Setup, Get Static IP) and confirm Orders Allowed is true.",
  'Record the attestation here with the address and what Dhan showed.',
  'If the address really changed, Dhan locks changes for 7 days (the card shows the date); use the secondary slot if there is one, otherwise the account stays stopped.',
];

/** The runbook's IP steps (invalid-IP rejection, DH-905), beside the form that records the outcome. */
export function RunbookSteps() {
  return (
    <Card component="section" aria-label="IP runbook">
      <CardHeader title="If an order was rejected for an invalid IP" />
      <CardContent>
        <List dense component="ol" sx={{ listStyle: 'decimal', pl: 3 }}>
          {steps.map((s) => (
            <ListItem key={s} sx={{ display: 'list-item', pl: 0 }}>
              <ListItemText primary={s} />
            </ListItem>
          ))}
        </List>
      </CardContent>
    </Card>
  );
}
