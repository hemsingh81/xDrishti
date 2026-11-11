import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { basketBudgetsQuery, limitBasketsQuery } from '../api/limitsApi';
import { BudgetsForm } from './BudgetsForm';
import { LimitHistory } from './LimitHistory';
import { LimitSection } from './LimitSection';

/** The basket risk budgets in force (one row per basket that has one), their change form and their history. */
export function BudgetsSection() {
  const budgets = useQuery(basketBudgetsQuery);
  const baskets = useQuery(limitBasketsQuery);
  const names = new Map((baskets.data ?? []).map((b) => [b.id, b.name]));
  return (
    <LimitSection id="limits-budgets-title" title="Basket risk budgets">
      {budgets.isPending || baskets.isPending ? (
        <LoadingState label="Loading the basket budgets" />
      ) : budgets.isError ? (
        <ErrorState error={budgets.error} />
      ) : baskets.isError ? (
        <ErrorState error={baskets.error} />
      ) : (
        <>
          {budgets.data.budgets.length === 0 ? (
            <Typography>No basket has a risk budget.</Typography>
          ) : (
            <ul aria-label="Basket budgets in force">
              {budgets.data.budgets.map((b) => (
                <li key={b.basketId}>
                  {names.get(b.basketId) ?? b.basketId}: {b.budgetPct} percent of the open risk limit
                </li>
              ))}
            </ul>
          )}
          <LimitHistory
            label="Basket budget history"
            rows={budgets.data.history.map((h) => ({
              id: h.id,
              what: names.get(h.basketId) ?? h.basketId,
              from: '',
              to: h.budgetPct === null ? 'removed' : `${String(h.budgetPct)} percent`,
              direction: h.direction,
              actor: h.actor,
              changedAt: h.changedAt,
            }))}
          />
          <BudgetsForm view={budgets.data} baskets={baskets.data} />
        </>
      )}
    </LimitSection>
  );
}
