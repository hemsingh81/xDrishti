import { Alert, Box, Card, CardContent, Typography } from '@mui/material';
import { formatPercent } from '@/shared/lib/format';
import { Facts, type Fact } from '@/shared/ui';
import type { ReplayArmComparison, ReplayLabel } from '../../api/replayApi';

const pct = (v: number | null) => (v === null ? 'n/a' : `${formatPercent(v * 100)}%`);
const daily = (v: number | null) => (v === null ? 'n/a' : `${(v * 100).toFixed(3)}%`);

const answers: Record<string, string> = {
  selection_better: "The system's choosing did better than taking every candidate, on the informative days.",
  baseline_better: "Taking every candidate did better than the system's choosing, on the informative days.",
  no_measurable_difference: "No measurable difference between the system's choosing and taking every candidate.",
  too_little_history: "Too little history to tell the system's choosing from taking every candidate.",
};

/** The stored code of the baseline label, in plain words. Any other text is shown as it comes. */
const labelWords: Record<string, string> = {
  baseline_no_selection:
    "The baseline takes every candidate the system's own cells find, at equal risk. It is no choosing among the system's candidates, not random stocks.",
};
const words = (text: string) => labelWords[text] ?? text;

/**
 * Selection against the no-selection baseline (SR-02). Hindsight labels come first, then the label that says what the baseline is,
 * the realised-only shortcut, n, the informative days and P. "Too little history" and "no measurable difference" are ordinary
 * answers; an answer of null shows its reason.
 */
export function ArmComparisonCard({
  comparison: c,
  hindsight,
}: {
  comparison: ReplayArmComparison;
  hindsight: ReplayLabel[];
}) {
  const facts: Fact[] = [
    { label: 'Paired days', value: c.days ?? 'n/a' },
    { label: 'Informative days (n)', value: c.informativeDays ?? 'n/a' },
    { label: 'Days needed (n at least)', value: c.minDays ?? 'n/a' },
    { label: 'P (probability) selection is better', value: pct(c.probabilitySelectionBetter) },
    { label: 'P (probability) baseline is better', value: pct(c.probabilityBaselineBetter) },
    { label: 'Mean daily difference (selection less baseline)', value: daily(c.meanDailyDifference) },
    {
      label: 'Interval of the daily difference',
      value: c.low === null || c.high === null ? 'n/a' : `${daily(c.low)} to ${daily(c.high)}`,
    },
    { label: 'Bar to call a difference (P at least)', value: pct(c.cut) },
  ];
  return (
    <Card component="section" aria-labelledby="replay-arms-title">
      <CardContent sx={{ display: 'grid', gap: 1.25, minWidth: 0 }}>
        <Typography id="replay-arms-title" variant="h2" component="h2">
          Selection against the no-selection baseline
        </Typography>
        {hindsight.length > 0 && (
          <Alert severity="warning" role="note" aria-label="Comparison hindsight labels">
            <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
              {hindsight.map((label) => (
                <li key={label.code}>{label.text}</li>
              ))}
            </Box>
          </Alert>
        )}
        <Alert severity="info" role="note" aria-label="What the baseline is">
          {words(c.label)}
        </Alert>
        <Typography variant="body2" color="text.secondary">
          {`Shortcut: ${c.shortcut}`}
        </Typography>
        {c.answer === null ? (
          <Alert severity="info">{c.reason ?? 'There is no answer yet: a run is not complete.'}</Alert>
        ) : (
          <>
            <Typography variant="h3" component="p" sx={{ fontWeight: 600 }}>
              {answers[c.answer] ?? c.answer}
            </Typography>
            {c.reason && <Typography variant="body2">{c.reason}</Typography>}
          </>
        )}
        <Facts items={facts} />
      </CardContent>
    </Card>
  );
}
