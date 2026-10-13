import { FormControlLabel, Switch } from '@mui/material';

interface TradingDaysSwitchProps {
  checked: boolean;
  onChange: (checked: boolean) => void;
}

/** Skip the days the market is closed (NSE holidays), not only the weekend. */
export function TradingDaysSwitch({ checked, onChange }: TradingDaysSwitchProps) {
  return (
    <FormControlLabel
      control={
        <Switch
          checked={checked}
          onChange={(event) => {
            onChange(event.target.checked);
          }}
        />
      }
      label="Trading days only (Monday to Friday, not on market holidays)"
    />
  );
}
