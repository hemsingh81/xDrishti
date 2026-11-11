import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { basketSettingsQuery, limitBasketsQuery } from '../api/limitsApi';
import { BasketSettingsForm } from './BasketSettingsForm';
import { LimitHistory } from './LimitHistory';
import { LimitSection } from './LimitSection';

/** The basket settings in force (the gate's breadth source, the cluster cap and cut), their change form and their history. */
export function BasketSettingsSection() {
  const settings = useQuery(basketSettingsQuery);
  const baskets = useQuery(limitBasketsQuery);
  return (
    <LimitSection id="limits-baskets-title" title="Basket settings">
      {settings.isPending || baskets.isPending ? (
        <LoadingState label="Loading the basket settings" />
      ) : settings.isError ? (
        <ErrorState error={settings.error} />
      ) : baskets.isError ? (
        <ErrorState error={baskets.error} />
      ) : (
        <>
          <Typography>
            The gate reads breadth from <strong>{settings.data.breadthSource}</strong>; cluster cap{' '}
            {settings.data.maxClusterRiskPct === null
              ? 'off'
              : `${String(settings.data.maxClusterRiskPct)} percent of capital`}
            ; cluster cut {settings.data.clusterRho}.
          </Typography>
          <LimitHistory
            label="Basket settings history"
            rows={settings.data.history.map((h) => ({
              id: h.id,
              what: h.key,
              from: h.oldValue,
              to: h.newValue,
              direction: h.direction,
              actor: h.actor,
              changedAt: h.changedAt,
            }))}
          />
          <BasketSettingsForm view={settings.data} baskets={baskets.data} />
        </>
      )}
    </LimitSection>
  );
}
