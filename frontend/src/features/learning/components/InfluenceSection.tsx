import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatAsOfDay, formatCount, MetricValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { Facts } from '@/shared/ui/Facts';
import { influenceQuery, rollbackQuery, type MlInfluence } from '../api/mlQueries';
import { figure, formatFine, formatFineR } from '../lib/figure';
import { holdOutStateLabel, termLabel } from '../lib/mlText';
import { MlSwitchList } from './MlSwitchList';
import { TermStateChip } from './TermStateChip';

/** Whether the model may influence a decision, term by term. Read only: today the evidence terms have no source, so it does not. */
export function InfluenceSection({ id }: { id: number }) {
  const query = useQuery(influenceQuery(id));
  if (query.isPending) return <LoadingState label="Loading the influence check…" />;
  if (query.isError) return <ErrorState error={query.error} />;
  return <Body view={query.data} />;
}

function Body({ view }: { view: MlInfluence }) {
  const inTerms = new Set(view.terms.flatMap((term) => term.reasons.map((r) => r.code)));
  const further = view.reasons.filter((reason) => !inTerms.has(reason.code));
  const holdOut = view.holdOut;
  return (
    <Box sx={{ display: 'grid', gap: 2, minWidth: 0 }}>
      <Typography variant="body2" sx={{ fontWeight: 600 }}>
        {view.mayInfluence
          ? 'Every term holds and this model is the champion: it may influence a decision.'
          : 'This model may not influence a decision. Anything but "Holds" is not a pass.'}
      </Typography>
      <Box
        component="ul"
        aria-label="Influence predicate terms"
        sx={{ m: 0, p: 0, listStyle: 'none', display: 'grid', gap: 1.5 }}
      >
        {view.terms.map((term) => (
          <Box key={term.name} component="li" sx={{ borderLeft: 3, borderColor: 'divider', pl: 1.5, minWidth: 0 }}>
            <Typography variant="body2" sx={{ fontWeight: 700, mb: 0.5 }}>
              {termLabel(term.name)}
            </Typography>
            <TermStateChip state={term.state} />
            {term.reasons.length > 0 && (
              <Box component="ul" aria-label={`Reasons for ${termLabel(term.name)}`} sx={{ m: 0, mt: 0.5, pl: 2.5 }}>
                {term.reasons.map((reason) => (
                  <li key={reason.code}>
                    {reason.text}{' '}
                    <Typography component="span" variant="caption" color="text.secondary">
                      ({reason.code})
                    </Typography>
                  </li>
                ))}
              </Box>
            )}
          </Box>
        ))}
      </Box>
      <Facts
        items={[
          { label: 'Champion now', value: view.role === 'champion' ? 'yes' : `no (role: ${view.role})` },
          ...further.map((reason) => ({ label: 'Further reason', value: `${reason.text} (${reason.code})` })),
          { label: 'Hold-out state', value: `${holdOutStateLabel(holdOut.state)}; family ${holdOut.family}` },
          {
            label: 'Hold-out block',
            value: holdOut.blockStart ? `from ${formatAsOfDay(holdOut.blockStart)}` : 'no look taken yet',
          },
          {
            label: 'Hold-out rows',
            value: (
              <MetricValue state={figure(holdOut.rows, 'no source for the hold-out score yet')} format={formatCount} />
            ),
          },
          {
            label: 'Odds of beating the best no-ML baseline',
            value: (
              <MetricValue
                state={figure(holdOut.probabilityBeatsNoMl, 'no source for the hold-out score yet')}
                format={formatFine}
              />
            ),
          },
          {
            label: 'Effect of the take rule',
            value: (
              <MetricValue
                state={figure(holdOut.policyEffectR, 'no source for the hold-out score yet')}
                format={formatFineR}
              />
            ),
          },
          {
            label: 'Interval',
            value: (
              <MetricValue
                state={{
                  kind: 'unavailable',
                  reason: 'the hold-out probability has no source, so no interval can be drawn',
                }}
              />
            ),
          },
          { label: 'Thresholds', value: view.settings },
        ]}
      />
      {view.notes.map((note) => (
        <Typography key={note} variant="body2" color="text.secondary">
          {note}
        </Typography>
      ))}
      <Switches />
    </Box>
  );
}

/** The three ML switches, read only here; the owner acts are on the Models tab. */
function Switches() {
  const query = useQuery(rollbackQuery);
  return (
    <Box sx={{ minWidth: 0 }}>
      <Typography component="h4" variant="body2" sx={{ fontWeight: 700, mb: 1 }}>
        ML switches
      </Typography>
      {query.isPending && <LoadingState label="Loading the ML switches…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && <MlSwitchList settings={query.data.settings} />}
    </Box>
  );
}
