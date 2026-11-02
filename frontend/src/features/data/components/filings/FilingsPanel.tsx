import { Alert, Box } from '@mui/material';
import { FilingCoverageSection } from './FilingCoverageSection';
import { FilingsListSection } from './FilingsListSection';
import { ResultsCalendarSection } from './ResultsCalendarSection';
import { syntheticNote, syntheticParser } from './words';

/** Data → Filings: coverage, the results calendar, the stored filings with their parsed facts, and (once its endpoint exists, P15-08) the extraction coverage, which is left out until then. Read only. */
export default function FilingsPanel() {
  return (
    <Box sx={{ flex: '1 1 auto', minHeight: 0, overflowY: 'auto', '& > * + *': { mt: 3 } }}>
      <Alert severity="info">
        Figures parsed from results filings come from the {syntheticParser} parser, {syntheticNote}.
      </Alert>
      <FilingCoverageSection />
      <ResultsCalendarSection />
      <FilingsListSection />
    </Box>
  );
}
