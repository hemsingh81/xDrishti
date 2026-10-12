import CloseIcon from '@mui/icons-material/Close';
import { Box, IconButton, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { formatIstDateTime, formatNumber } from '@/shared/lib/format';
import { LoadingState } from '@/shared/ui';
import { monoFontFamily } from '@/theme';
import { detailQuery, type DetailOptions } from '../api/qualityQueries';
import type { QualityWatch } from '../hooks/useQualityWatch';
import { problemText } from '../lib/errors';
import { sourceLabel } from '../lib/labels';
import { FixActions } from './FixActions';
import { InspectChecks } from './InspectChecks';
import { QualityStatusChip } from './QualityStatusChip';
import { QueryAlert } from './QueryAlert';

interface InspectPanelProps {
  id: string;
  watch: QualityWatch;
  onClose: () => void;
}

/** The content of the Inspect drawer for one instrument; mounted per instrument, so its options start fresh. */
export function InspectPanel({ id, watch, onClose }: InspectPanelProps) {
  const [options, setOptions] = useState<DetailOptions>({ differencesOnly: false, timeframe: null });
  const query = useQuery(detailQuery(id, options));
  const { data, isPending, isError, error, isPlaceholderData } = query;

  return (
    <>
      <Stack direction="row" sx={{ p: 2, gap: 1, alignItems: 'flex-start', justifyContent: 'space-between' }}>
        <Box sx={{ minWidth: 0 }}>
          <Typography variant="h2" sx={{ fontFamily: monoFontFamily, overflowWrap: 'anywhere' }}>
            {data?.symbol ?? 'Inspect'}
          </Typography>
          {data && <Typography color="text.secondary">{data.name}</Typography>}
        </Box>
        <IconButton aria-label="Close inspect" edge="end" onClick={onClose}>
          <CloseIcon />
        </IconButton>
      </Stack>
      <Stack spacing={2} sx={{ px: 2, pt: 1, pb: 3, overflowY: 'auto' }}>
        {isPending && <LoadingState label="Loading the checks" />}
        {isError && !data && (
          <QueryAlert
            message={problemText(error, 'The checks could not be loaded.')}
            onRetry={() => {
              void query.refetch();
            }}
          />
        )}
        {data && (
          <>
            <Box>
              <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
                <QualityStatusChip status={data.status} />
                {data.score !== null && (
                  <Typography sx={{ fontWeight: 600 }}>Score {formatNumber(data.score)} of 100</Typography>
                )}
              </Stack>
              <Typography variant="body2" color="text.secondary" sx={{ mt: 0.5 }}>
                {data.checkedAt ? (
                  <>
                    Checked <time dateTime={data.checkedAt}>{formatIstDateTime(data.checkedAt)}</time>
                    {data.barsChecked === null ? '' : ` · ${formatNumber(data.barsChecked)} one-minute bars`}
                  </>
                ) : (
                  'Not checked yet.'
                )}
                {data.source ? ` · Source: ${sourceLabel(data.source)}` : ''}
              </Typography>
            </Box>
            <FixActions instrumentId={id} watch={watch} />
            <InspectChecks detail={data} options={options} onOptionsChange={setOptions} loading={isPlaceholderData} />
          </>
        )}
      </Stack>
    </>
  );
}
