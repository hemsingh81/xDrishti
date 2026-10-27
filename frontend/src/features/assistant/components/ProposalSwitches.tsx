import { FormControlLabel, Switch, Typography } from '@mui/material';

interface Props {
  accept: boolean;
  onAccept: (on: boolean) => void;
  auto: boolean;
  onAuto: (on: boolean) => void;
}

/** The two switches of the proposals (P9-08): both off by default; turning either on is an owner act with the password (the dialog asks for it). */
export function ProposalSwitches({ accept, onAccept, auto, onAuto }: Props) {
  return (
    <>
      <div>
        <FormControlLabel
          control={
            <Switch
              checked={accept}
              onChange={(e) => {
                onAccept(e.target.checked);
              }}
            />
          }
          label="Allow accepting suggestions"
        />
        <Typography variant="body2" color="text.secondary">
          Off by default. Accepting writes a proposed hypothesis or changes one of your baskets; it asks your password
          every time and never tracks an instrument, approves, runs or orders anything.
        </Typography>
      </div>
      <div>
        <FormControlLabel
          control={
            <Switch
              checked={auto}
              onChange={(e) => {
                onAuto(e.target.checked);
              }}
            />
          }
          label="Auto-accept hypothesis suggestions"
        />
        <Typography variant="body2" color="text.secondary">
          Off by default. When on, a new hypothesis suggestion whose checks all pass again is written as a proposed
          hypothesis without your accept: at most 3 per day, recorded as automatic. You still approve, start and judge
          it. Basket changes are never automatic.
        </Typography>
      </div>
    </>
  );
}
