import { Box, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { EvidenceBlock } from '@/shared/analytics';
import { summaryQuery, type RunsView } from '../api/reportQueries';
import { evidenceOf } from '../lib/evidence';
import { joined, type ReportFilter, type ReportQuery, type FilterPatch } from '../lib/reportFilter';
import { Panel } from './Panel';
import { RunPicker } from './RunPicker';

interface DataUsedPanelProps {
  runs: RunsView;
  query: ReportQuery;
  filter: ReportFilter;
  set: (patch: FilterPatch) => void;
}

/** Which runs the numbers come from, which were left out and why, and the limits that always apply. */
export function DataUsedPanel({ runs, query, filter, set }: DataUsedPanelProps) {
  const summary = useQuery(summaryQuery(query));
  const evidence = summary.data?.evidence;
  return (
    <Panel title="Data used">
      <Box sx={{ display: 'grid', gap: 1.5 }}>
        <Typography variant="body2" color="text.secondary">
          {filter.runs.length > 0 ? 'You chose these runs.' : runs.defaultSetRule}
        </Typography>
        <RunPicker
          runs={runs.runs}
          chosen={filter.runs}
          used={evidence ? new Set(evidence.runKeys) : null}
          onChange={(keys) => {
            set({ runs: keys ? joined(keys) : null });
          }}
        />
        {evidence && (
          <>
            <EvidenceBlock {...evidenceOf(evidence)} label="Limits of these numbers" />
            {evidence.netBasisConfirmation && <Typography variant="body2">{evidence.netBasisConfirmation}</Typography>}
            {evidence.unavailableInputs.length > 0 && (
              <Box component="ul" aria-label="Inputs that do not exist" sx={{ m: 0, pl: 2.5 }}>
                {evidence.unavailableInputs.map((text) => (
                  <li key={text}>
                    <Typography variant="body2">{text}</Typography>
                  </li>
                ))}
              </Box>
            )}
            {evidence.openAtEndLeftOut > 0 && (
              <Typography variant="body2" color="text.secondary">
                {`${String(evidence.openAtEndLeftOut)} trades still open at the end of the data are never counted.`}
              </Typography>
            )}
          </>
        )}
      </Box>
    </Panel>
  );
}
