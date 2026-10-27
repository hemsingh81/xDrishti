import { FormControlLabel, Switch, Typography } from '@mui/material';

interface Props {
  draftConfirm: boolean;
  onDraftConfirm: (on: boolean) => void;
  autoQuick: boolean;
  onAutoQuick: (on: boolean) => void;
}

/** The two switches of strategy drafting (P9-07): both off by default; turning either on is an owner act with the password (the dialog asks for it). */
export function DraftSwitches({ draftConfirm, onDraftConfirm, autoQuick, onAutoQuick }: Props) {
  return (
    <>
      <div>
        <FormControlLabel
          control={
            <Switch
              checked={draftConfirm}
              onChange={(e) => {
                onDraftConfirm(e.target.checked);
              }}
            />
          }
          label="Allow confirming strategy drafts"
        />
        <Typography variant="body2" color="text.secondary">
          Off by default. Confirming stores a draft as a library version and can start a quick test; it asks your
          password every time.
        </Typography>
      </div>
      <div>
        <FormControlLabel
          control={
            <Switch
              checked={autoQuick}
              onChange={(e) => {
                onAutoQuick(e.target.checked);
              }}
            />
          }
          label="Auto quick test of valid drafts"
        />
        <Typography variant="body2" color="text.secondary">
          Off by default. When on, a clean draft of an idea you typed here, whose text names exactly one of your baskets
          by its exact name, is stored and gets a quick test without your confirm: at most 1 per hour and 3 per day,
          recorded as automatic. It never runs a full test and never places an order.
        </Typography>
      </div>
    </>
  );
}
