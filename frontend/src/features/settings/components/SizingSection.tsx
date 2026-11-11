import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { ErrorState, LoadingState } from '@/shared/ui';
import { sizingSettingsQuery } from '../api/limitsApi';
import { LimitHistory } from './LimitHistory';
import { LimitSection } from './LimitSection';
import { SizingSettingsForm } from './SizingSettingsForm';

const onOff = (on: boolean) => (on ? 'on' : 'off');

/** The sizing settings in force, their change form and their history. */
export function SizingSection() {
  const query = useQuery(sizingSettingsQuery);
  return (
    <LimitSection id="limits-sizing-title" title="Sizing">
      {query.isPending ? (
        <LoadingState label="Loading the sizing settings" />
      ) : query.isError ? (
        <ErrorState error={query.error} />
      ) : (
        <>
          <Typography>
            Volatility scale <strong>{onOff(query.data.volTargetEnabled)}</strong>, floor {query.data.floor}; capacity
            cap <strong>{onOff(query.data.capacityEnabled)}</strong>, share {query.data.capacityPct}; budget minimum fit{' '}
            {query.data.minFit}.
          </Typography>
          <LimitHistory
            label="Sizing settings history"
            rows={query.data.history.map((h) => ({
              id: h.id,
              what: h.key,
              from: h.oldValue,
              to: h.newValue,
              direction: h.direction,
              actor: h.actor,
              changedAt: h.changedAt,
            }))}
          />
          <SizingSettingsForm view={query.data} />
        </>
      )}
    </LimitSection>
  );
}
