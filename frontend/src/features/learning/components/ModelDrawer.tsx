import { Box, Table, TableBody, TableCell, TableContainer, TableHead, TableRow, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { formatAsOf, NonValue } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { modelQuery, type ModelDetail } from '../api/learningQueries';
import { CalibrationTable } from './CalibrationTable';
import { PreformattedText } from './PreformattedText';
import { DrawerSection, RecordDrawer } from './RecordDrawer';

const topFeatures = 15;
const weight = new Intl.NumberFormat('en-IN', { minimumFractionDigits: 4, maximumFractionDigits: 4 });

/** One model in full: the validation report, parameters, calibration map, feature weights and designations. No model bytes. */
export function ModelDrawer({ id, onClose }: { id: number; onClose: () => void }) {
  const query = useQuery(modelQuery(id));
  return (
    <RecordDrawer
      title={`Model #${id}`}
      subtitle={query.data ? `${query.data.model.trainer}, role ${query.data.model.role}` : undefined}
      closeLabel="Close model"
      onClose={onClose}
    >
      {query.isPending && <LoadingState label="Loading the model…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data && <Body detail={query.data} />}
    </RecordDrawer>
  );
}

function Body({ detail }: { detail: ModelDetail }) {
  return (
    <>
      <DrawerSection title="Validation report">
        <PreformattedText label="Validation report with the fold table" text={detail.reportText} />
      </DrawerSection>
      <DrawerSection title="Parameters">
        <PreformattedText label="Trainer and validation settings" text={detail.parameters} />
      </DrawerSection>
      <DrawerSection title="Calibration">
        <CalibrationTable points={detail.calibration} />
      </DrawerSection>
      <DrawerSection title="Feature importance">
        <Typography variant="caption" color="text.secondary" component="p" sx={{ mb: 1 }}>
          Path-based contributions, not exact, not SHAP. The top {topFeatures}.
        </Typography>
        {detail.importance.length === 0 ? (
          <NonValue state={{ kind: 'empty', reason: 'No feature weights were stored.' }} />
        ) : (
          <TableContainer role="region" aria-label="Feature importance, scrolls sideways" tabIndex={0}>
            <Table size="small" aria-label="Feature importance">
              <TableHead>
                <TableRow>
                  <TableCell>Feature</TableCell>
                  <TableCell align="right">Mean absolute contribution</TableCell>
                </TableRow>
              </TableHead>
              <TableBody>
                {detail.importance.slice(0, topFeatures).map((f) => (
                  <TableRow key={f.feature}>
                    <TableCell sx={{ overflowWrap: 'anywhere' }}>{f.feature}</TableCell>
                    <TableCell align="right">{weight.format(f.meanAbsContribution)}</TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </TableContainer>
        )}
      </DrawerSection>
      <DrawerSection title="Designations">
        <Box
          component="ol"
          aria-label="Designations, oldest first"
          sx={{ m: 0, p: 0, listStyle: 'none', display: 'grid', gap: 1.5 }}
        >
          {detail.designations.map((d) => (
            <Box key={d.id} component="li" sx={{ borderLeft: 3, borderColor: 'divider', pl: 1.5 }}>
              <Typography variant="body2" sx={{ fontWeight: 700 }}>
                {d.role}{' '}
                <Typography component="span" variant="caption" color="text.secondary">
                  by {d.createdBy}, {formatAsOf(d.createdAt)}
                </Typography>
              </Typography>
              <Typography variant="body2" sx={{ overflowWrap: 'anywhere' }}>
                {d.reason}
              </Typography>
            </Box>
          ))}
        </Box>
      </DrawerSection>
      <DrawerSection title="Limits">
        <Box component="ul" sx={{ m: 0, pl: 2.5 }}>
          {detail.limits.map((limit, index) => (
            <li key={index}>{limit}</li>
          ))}
        </Box>
      </DrawerSection>
    </>
  );
}
