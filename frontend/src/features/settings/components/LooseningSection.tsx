import { Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { looseningSettingsQuery } from '@/shared/api/looseningRead';
import { ErrorState, LoadingState } from '@/shared/ui';
import { LimitHistory } from './LimitHistory';
import { LimitSection } from './LimitSection';
import { LooseningSettingForm } from './LooseningSettingForm';

/** The automatic dismissal of the plan screen's loosening banner: what is in force, its change form and its history. */
export function LooseningSection() {
  const query = useQuery(looseningSettingsQuery);
  return (
    <LimitSection id="limits-banner-title" title="Loosening banner">
      {query.isPending ? (
        <LoadingState label="Loading the banner setting" />
      ) : query.isError ? (
        <ErrorState error={query.error} />
      ) : (
        <>
          <Typography>
            Automatic dismissal is <strong>{query.data.autoDismiss ? 'on' : 'off'}</strong>. {query.data.notice}
          </Typography>
          <LimitHistory
            label="Loosening banner history"
            rows={query.data.history.map((h) => ({
              id: h.id,
              what: 'Automatic dismissal',
              from: '',
              to: h.autoDismiss ? 'on' : 'off',
              direction: '',
              actor: h.actor,
              changedAt: h.changedAt,
            }))}
          />
          <LooseningSettingForm view={query.data} />
        </>
      )}
    </LimitSection>
  );
}
