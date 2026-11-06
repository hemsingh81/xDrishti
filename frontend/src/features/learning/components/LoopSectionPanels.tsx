import { Typography } from '@mui/material';
import { formatCount, formatRatio, formatSigned, MetricValue, NonValue } from '@/shared/analytics';
import { Facts } from '@/shared/ui/Facts';
import type { LoopReport } from '../api/confidenceQueries';
import { curveVerdictLabel, slopeVerdictLabel } from '../lib/confidenceText';
import { figure, formatFine, formatFineSigned } from '../lib/figure';
import { Panel } from './Panel';

const unavailable = (reason: string | null) => (
  <NonValue state={{ kind: 'unavailable', reason: reason ?? 'no reason given' }} />
);

export function CurvePanel({ curve }: { curve: LoopReport['curve'] }) {
  return (
    <Panel title="Learning curve">
      {curve.state !== 'available' ? (
        unavailable(curve.reason)
      ) : (
        <>
          <Facts
            items={[
              { label: 'Verdict', value: <strong>{curveVerdictLabel(curve.verdict)}</strong> },
              { label: 'Points fitted', value: formatCount(curve.pointsUsed) },
              {
                label: 'Gain per 50 rows',
                value: <MetricValue state={figure(curve.slopePer50Rows, 'not fitted')} format={formatFineSigned} />,
              },
              {
                label: 'Standard error',
                value: (
                  <MetricValue
                    state={figure(curve.standardError, 'no standard error with only three points')}
                    format={formatFine}
                  />
                ),
              },
              {
                label: 'Rows to reach the target',
                value: (
                  <MetricValue state={figure(curve.projectedRows, 'the line does not reach it')} format={formatCount} />
                ),
              },
              {
                label: 'More rows needed',
                value: (
                  <MetricValue state={figure(curve.moreRows, 'the line does not reach it')} format={formatCount} />
                ),
              },
            ]}
          />
          <Typography variant="body2" sx={{ mt: 1 }}>
            {curve.text}
          </Typography>
        </>
      )}
    </Panel>
  );
}

export function SlopePanel({ slope }: { slope: LoopReport['slope'] }) {
  return (
    <Panel title="Reliability slope">
      {slope.state !== 'available' ? (
        unavailable(slope.reason)
      ) : (
        <>
          <Facts
            items={[
              { label: 'Verdict', value: <strong>{slopeVerdictLabel(slope.verdict)}</strong> },
              { label: 'Outcomes in the fit', value: formatCount(slope.outcomes) },
              { label: 'Slope', value: <MetricValue state={figure(slope.slope, 'not fitted')} format={formatRatio} /> },
              {
                label: 'Intercept',
                value: <MetricValue state={figure(slope.intercept, 'not fitted')} format={formatRatio} />,
              },
              {
                label: 'Standard error',
                value: <MetricValue state={figure(slope.standardError, 'not computed')} format={formatRatio} />,
              },
            ]}
          />
          <Typography variant="body2" sx={{ mt: 1 }}>
            {slope.text}
          </Typography>
        </>
      )}
    </Panel>
  );
}

export function LiftPanel({ lift }: { lift: LoopReport['lift'] }) {
  return (
    <Panel title="Decision-quality lift">
      {lift.state !== 'available' ? (
        unavailable(lift.reason)
      ) : (
        <>
          <Facts
            items={[
              { label: 'Top half trades', value: formatCount(lift.topTrades) },
              { label: 'Bottom half trades', value: formatCount(lift.bottomTrades) },
              {
                label: 'Lift (top minus bottom, R)',
                value: <MetricValue state={figure(lift.lift, 'not computed')} format={formatSigned} />,
              },
              {
                label: 'Standard error',
                value: <MetricValue state={figure(lift.standardError, 'not computed')} format={formatRatio} />,
              },
              {
                label: 'z of the lift',
                value: <MetricValue state={figure(lift.liftZ, 'not computed')} format={formatRatio} />,
              },
              {
                label: 'Approved tickets in the top half',
                value: `${formatCount(lift.approvedInTop)} of ${formatCount(lift.approvedTickets)} with a stored score`,
              },
              {
                label: 'z of that share against 50%',
                value: <MetricValue state={figure(lift.approvedZ, 'not computed')} format={formatRatio} />,
              },
            ]}
          />
          <Typography variant="body2" sx={{ mt: 1 }}>
            {lift.text}
          </Typography>
        </>
      )}
    </Panel>
  );
}
