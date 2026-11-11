import { MenuItem, TextField, Typography } from '@mui/material';
import { useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import type { components } from '@/shared/api/schema';
import { basketBudgetsQuery, changeBudget } from '../api/limitsApi';
import { inRange, loosenedSince, parseNumber } from '../lib/limits';
import { LimitForm } from './LimitForm';

type View = components['schemas']['BasketRiskBudgetsView'];

/** The owner's change of one basket's risk budget (BK-10): 1 to 100 percent of the total open risk limit, or empty to remove it. */
export function BudgetsForm({ view, baskets }: { view: View; baskets: readonly { id: string; name: string }[] }) {
  const queryClient = useQueryClient();
  const [basket, setBasket] = useState('');
  const [pct, setPct] = useState('');
  const current = view.budgets.find((b) => b.basketId === basket)?.budgetPct ?? null;
  const next = parseNumber(pct);
  const valid = basket !== '' && (pct.trim() === '' || inRange(pct, 1, 100));
  const changed = next !== current;

  return (
    <LimitForm
      label="Change a basket budget"
      submitLabel="Change the budget"
      consequence="A budget caps what share of the day's open risk limit a basket may use. Raising or removing one loosens a limit and is shown on the next plan until you dismiss it. It places no order."
      fieldsReady={valid && changed}
      run={async (password) => {
        const before = view.history.reduce((max, h) => Math.max(max, h.id), 0);
        const after = await changeBudget(basket, next, password);
        queryClient.setQueryData(basketBudgetsQuery.queryKey, after);
        return loosenedSince(before, after.history);
      }}
    >
      <TextField
        select
        label="Basket"
        value={basket}
        onChange={(event) => {
          setBasket(event.target.value);
          setPct(String(view.budgets.find((b) => b.basketId === event.target.value)?.budgetPct ?? ''));
        }}
        helperText={
          basket === '' ? 'Choose a basket.' : `Now: ${current === null ? 'no budget' : `${String(current)} percent`}`
        }
      >
        {baskets.map((b) => (
          <MenuItem key={b.id} value={b.id}>
            {b.name}
          </MenuItem>
        ))}
      </TextField>
      <TextField
        label="Budget (1 to 100 percent, empty removes it)"
        value={pct}
        onChange={(event) => {
          setPct(event.target.value);
        }}
        error={pct.trim() !== '' && !inRange(pct, 1, 100)}
        slotProps={{ htmlInput: { inputMode: 'decimal' } }}
      />
      {baskets.length === 0 && <Typography variant="body2">There is no basket to give a budget to.</Typography>}
    </LimitForm>
  );
}
