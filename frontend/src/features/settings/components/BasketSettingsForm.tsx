import { Checkbox, FormControlLabel, MenuItem, TextField } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { components } from '@/shared/api/schema';
import { basketSettingsQuery, changeBasketSettings } from '../api/limitsApi';
import { inRange, loosenedSince, parseNumber } from '../lib/limits';
import { LimitForm } from './LimitForm';

type View = components['schemas']['BasketSettingsView'];

/** The owner's change of the basket settings (BK-08): where the market gate reads breadth, the cap on open risk per correlation cluster and the cluster cut. */
export function BasketSettingsForm({
  view,
  baskets,
}: {
  view: View;
  baskets: readonly { id: string; name: string }[];
}) {
  const queryClient = useQueryClient();
  const [source, setSource] = useState(view.breadthSource);
  const [cap, setCap] = useState(view.maxClusterRiskPct === null ? '' : String(view.maxClusterRiskPct));
  const [rho, setRho] = useState(String(view.clusterRho));
  const [clear, setClear] = useState(false);
  const capValid = clear || cap.trim() === '' || inRange(cap, 0.000001, 100);
  const capNumber = clear || cap.trim() === '' ? null : parseNumber(cap);
  const capChanged = clear
    ? view.maxClusterRiskPct !== null
    : capNumber !== null && capNumber !== view.maxClusterRiskPct;
  const changed = source !== view.breadthSource || capChanged || parseNumber(rho) !== view.clusterRho;
  const options = ['tracked', ...baskets.map((b) => `basket:${b.id}`)];
  const nameOf = (value: string) =>
    value === 'tracked'
      ? 'The tracked set'
      : `Basket ${baskets.find((b) => `basket:${b.id}` === value)?.name ?? value}`;

  return (
    <LimitForm
      label="Change the basket settings"
      submitLabel="Change the basket settings"
      consequence="The cap only ever makes a plan stricter; raising or clearing it, raising the cluster cut or changing the gate's breadth source loosens a limit and is shown on the next plan until you dismiss it. It places no order."
      fieldsReady={capValid && inRange(rho, 0.1, 1) && changed}
      run={async (password) => {
        const after = await changeBasketSettings({
          breadthSource: source === view.breadthSource ? null : source,
          maxClusterRiskPct: clear || !capChanged ? null : capNumber,
          clearCap: clear && view.maxClusterRiskPct !== null ? true : null,
          clusterRho: parseNumber(rho) === view.clusterRho ? null : parseNumber(rho),
          password,
        });
        queryClient.setQueryData(basketSettingsQuery.queryKey, after);
        return loosenedSince(view.version, after.history);
      }}
    >
      <TextField
        select
        label="Where the gate reads breadth"
        value={options.includes(source) ? source : 'tracked'}
        onChange={(event) => {
          setSource(event.target.value);
        }}
        helperText="Any change of the source loosens the gate."
      >
        {options.map((o) => (
          <MenuItem key={o} value={o}>
            {nameOf(o)}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        label="Cap on open risk per cluster (percent of capital, above 0 up to 100)"
        value={clear ? '' : cap}
        disabled={clear}
        onChange={(event) => {
          setCap(event.target.value);
        }}
        error={!capValid}
        helperText="Empty: no cap."
        slotProps={{ htmlInput: { inputMode: 'decimal' } }}
      />
      <FormControlLabel
        control={
          <Checkbox
            checked={clear}
            onChange={(event) => {
              setClear(event.target.checked);
            }}
          />
        }
        label="Turn the cap off"
      />
      <TextField
        label="Cluster cut, correlation (0.1 to 1)"
        value={rho}
        onChange={(event) => {
          setRho(event.target.value);
        }}
        error={!inRange(rho, 0.1, 1)}
        slotProps={{ htmlInput: { inputMode: 'decimal' } }}
      />
    </LimitForm>
  );
}
