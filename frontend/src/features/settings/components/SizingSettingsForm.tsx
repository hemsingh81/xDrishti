import { Checkbox, FormControlLabel, TextField } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { components } from '@/shared/api/schema';
import { changeSizing, sizingSettingsQuery } from '../api/limitsApi';
import { inRange, loosenedSince, parseNumber } from '../lib/limits';
import { LimitForm } from './LimitForm';

type View = components['schemas']['SizingSettingsView'];

/** The owner's change of the sizing settings (BK-10): scaling a suggestion down by volatility and capacity, never up. A value left as it is stays. */
export function SizingSettingsForm({ view }: { view: View }) {
  const queryClient = useQueryClient();
  const [vol, setVol] = useState(view.volTargetEnabled);
  const [floor, setFloor] = useState(String(view.floor));
  const [cap, setCap] = useState(view.capacityEnabled);
  const [pct, setPct] = useState(String(view.capacityPct));
  const [minFit, setMinFit] = useState(String(view.minFit));
  const valid = inRange(floor, 0.25, 1) && inRange(pct, 0.000001, 0.005) && inRange(minFit, 0.25, 1);
  const changed =
    vol !== view.volTargetEnabled ||
    cap !== view.capacityEnabled ||
    parseNumber(floor) !== view.floor ||
    parseNumber(pct) !== view.capacityPct ||
    parseNumber(minFit) !== view.minFit;

  return (
    <LimitForm
      label="Change the sizing settings"
      submitLabel="Change the sizing settings"
      consequence="Sizing only ever scales a suggested quantity down. Raising the floor, lowering the minimum fit, switching a scale off or raising the capacity share loosens a limit and is shown on the next plan until you dismiss it. It places no order."
      fieldsReady={valid && changed}
      run={async (password) => {
        const next = await changeSizing({
          volTargetEnabled: vol,
          floor: parseNumber(floor),
          capacityEnabled: cap,
          capacityPct: parseNumber(pct),
          minFit: parseNumber(minFit),
          password,
        });
        queryClient.setQueryData(sizingSettingsQuery.queryKey, next);
        return loosenedSince(view.version, next.history);
      }}
    >
      <FormControlLabel
        control={
          <Checkbox
            checked={vol}
            onChange={(event) => {
              setVol(event.target.checked);
            }}
          />
        }
        label="Scale down in unusually volatile shares"
      />
      <TextField
        label="Volatility floor (0.25 to 1)"
        value={floor}
        onChange={(event) => {
          setFloor(event.target.value);
        }}
        error={!inRange(floor, 0.25, 1)}
        helperText="The lowest scale a volatile share can be cut to."
        slotProps={{ htmlInput: { inputMode: 'decimal' } }}
      />
      <FormControlLabel
        control={
          <Checkbox
            checked={cap}
            onChange={(event) => {
              setCap(event.target.checked);
            }}
          />
        }
        label="Cap a suggestion at a share of the traded value"
      />
      <TextField
        label="Capacity share (up to 0.005)"
        value={pct}
        onChange={(event) => {
          setPct(event.target.value);
        }}
        error={!inRange(pct, 0.000001, 0.005)}
        helperText="Share of the 20-day traded value; it can only be tightened below the app's own limit."
        slotProps={{ htmlInput: { inputMode: 'decimal' } }}
      />
      <TextField
        label="Budget minimum fit (0.25 to 1)"
        value={minFit}
        onChange={(event) => {
          setMinFit(event.target.value);
        }}
        error={!inRange(minFit, 0.25, 1)}
        helperText="The lowest share of its size a basket budget may cut a candidate to."
        slotProps={{ htmlInput: { inputMode: 'decimal' } }}
      />
    </LimitForm>
  );
}
