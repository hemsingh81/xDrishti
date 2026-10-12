import { Box, Typography } from '@mui/material';
import type { QualityInstrumentDetail } from '@/shared/api';
import type { DetailOptions } from '../api/qualityQueries';
import { AggregatesSection } from './inspect/AggregatesSection';
import { CompletenessSection } from './inspect/CompletenessSection';
import { FreshnessSection } from './inspect/FreshnessSection';
import { OfficialDailySection } from './inspect/OfficialDailySection';
import { SanitySection } from './inspect/SanitySection';
import { SpikesSection } from './inspect/SpikesSection';

interface InspectChecksProps {
  detail: QualityInstrumentDetail;
  options: DetailOptions;
  onOptionsChange: (options: DetailOptions) => void;
  /** The bucket table is reloading for new options. */
  loading: boolean;
}

/** One section per check, each with its result and the evidence; an instrument never checked says so instead. */
export function InspectChecks({ detail, options, onOptionsChange, loading }: InspectChecksProps) {
  const { checks } = detail;
  if (checks === null) {
    return (
      <Box sx={{ py: 2, borderTop: 1, borderColor: 'divider' }}>
        <Typography sx={{ fontWeight: 600 }}>This instrument has not been checked yet.</Typography>
        <Typography variant="body2" color="text.secondary">
          Press Check now to run every check over the last sessions.
        </Typography>
      </Box>
    );
  }
  return (
    <Box>
      <CompletenessSection check={checks.completeness} missing={detail.missing} />
      <SanitySection check={checks.sanity} />
      <SpikesSection check={checks.spikes} />
      <AggregatesSection
        check={checks.aggregates}
        buckets={detail.buckets}
        options={options}
        onOptionsChange={onOptionsChange}
        loading={loading}
      />
      <FreshnessSection check={checks.freshness} />
      <OfficialDailySection check={checks.officialDaily} />
    </Box>
  );
}
