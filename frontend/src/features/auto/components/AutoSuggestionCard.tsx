import { Accordion, AccordionDetails, AccordionSummary, Chip, Stack, Typography } from '@mui/material';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import type { AutoSuggestion } from '../api/autoApi';

/** One suggestion: what and why, with the evidence one click away. There is no control that acts on it. */
export function AutoSuggestionCard({ item }: { item: AutoSuggestion }) {
  const e = item.evidence;
  return (
    <Accordion disableGutters variant="outlined">
      <AccordionSummary expandIcon={<ExpandMoreIcon />}>
        <Stack direction="row" spacing={1} sx={{ alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography component="h4" sx={{ fontWeight: 600 }}>{`${item.symbol} ${item.side}`}</Typography>
          <Chip size="small" label={item.strategyId} />
        </Stack>
      </AccordionSummary>
      <AccordionDetails>
        <Stack spacing={1}>
          {item.why && <Typography>Not suggested: {item.why}</Typography>}
          <ul style={{ margin: 0, paddingLeft: '1.25rem' }}>
            {item.reasons.map((reason) => (
              <li key={reason}>{reason}</li>
            ))}
          </ul>
          {item.levels && (
            <Typography variant="body2">
              Reference {item.levels.entryRef}
              {item.levels.stop !== null && `, stop ${item.levels.stop}`}
              {item.levels.target !== null && `, target ${item.levels.target}`}.
            </Typography>
          )}
          <Typography variant="body2" color="text.secondary">
            Evidence: {e.n} trades (independent {e.nEff}), conservative expectancy {e.conservativeExpectancyR} R, regime{' '}
            {e.regime}, as of {e.dataAsOf}.
          </Typography>
        </Stack>
      </AccordionDetails>
    </Accordion>
  );
}
