import { Box, Button, Card, CardContent, Chip, Link, Stack, Typography } from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { NonValue, useUrlState } from '@/shared/analytics';
import { ErrorState, LoadingState } from '@/shared/ui';
import { hypothesesQuery } from '../api/learningQueries';
import { hypothesisStates, learningLink, moveNote, parseId } from '../lib/roleText';
import { FilterBar } from './FilterBar';
import { HypothesisDrawer } from './HypothesisDrawer';
import { Panel } from './Panel';
import { TermRow } from './TermRow';

/** Hypotheses: what was proposed to try, newest 50, with the state filter. A section of the Experiments tab. */
export function HypothesesSection() {
  const url = useUrlState();
  const state = url.choice('state', hypothesisStates, 'all');
  const open = parseId(url.text('hypothesis'));
  const query = useQuery(hypothesesQuery(state));
  return (
    <Panel
      title="Hypotheses"
      actions={
        <FilterBar
          label="Hypothesis state"
          values={hypothesisStates}
          value={state}
          onChange={(next) => {
            url.update({ state: next === 'all' ? null : next });
          }}
        />
      }
    >
      {query.isPending && <LoadingState label="Loading the hypotheses…" />}
      {query.isError && <ErrorState error={query.error} />}
      {query.data?.length === 0 && (
        <NonValue
          state={{
            kind: 'empty',
            reason:
              state === 'all'
                ? 'No hypothesis stored yet; run `xd learn hypothesis`.'
                : `No ${state} hypothesis. Try another state.`,
          }}
        />
      )}
      {query.data && query.data.length > 0 && (
        <>
          <Box component="ul" aria-label="Hypotheses, newest first" sx={{ m: 0, p: 0, display: 'grid', gap: 1.5 }}>
            {query.data.map((h) => (
              <Card key={h.id} component="li" variant="outlined" sx={{ listStyle: 'none', minWidth: 0 }}>
                <CardContent sx={{ display: 'grid', gap: 1 }}>
                  <Stack direction="row" sx={{ gap: 1, flexWrap: 'wrap', alignItems: 'center' }}>
                    <Typography component="h3" variant="h3">
                      Hypothesis #{h.id}
                    </Typography>
                    <Chip size="small" variant="outlined" label={`State: ${h.state}`} />
                    <Chip size="small" variant="outlined" label={`Source: ${h.source}`} />
                  </Stack>
                  <Typography sx={{ overflowWrap: 'anywhere' }}>{h.rationale}</Typography>
                  <Box component="dl" sx={{ m: 0, display: 'grid', gap: 0.5 }}>
                    <TermRow term="Cell">
                      {h.cellKey === null ? (
                        'not about one cell'
                      ) : (
                        <Link component={RouterLink} to={learningLink({ tab: 'setups', cell: h.cellKey })}>
                          {h.cellKey}
                        </Link>
                      )}
                    </TermRow>
                    <TermRow term="Knob">
                      {h.knobName} ({h.knobKind}): {h.baselineValue} to {h.candidateValue}
                    </TermRow>
                    <TermRow term="Judged by">
                      {h.experimentId === null ? (
                        'not judged yet'
                      ) : (
                        <Link
                          component={RouterLink}
                          to={learningLink({ tab: 'experiments', experiment: h.experimentId })}
                        >
                          Experiment #{h.experimentId}
                        </Link>
                      )}
                    </TermRow>
                    <TermRow term="May move to">
                      {h.nextStates.length > 0 ? h.nextStates.join(', ') : 'nowhere'}. {moveNote('hypothesis')}
                    </TermRow>
                  </Box>
                  <Box>
                    <Button
                      size="small"
                      sx={{ minHeight: 44 }}
                      onClick={() => {
                        url.update({ hypothesis: String(h.id) });
                      }}
                    >
                      Open hypothesis #{h.id}
                    </Button>
                  </Box>
                </CardContent>
              </Card>
            ))}
          </Box>
          <Typography variant="caption" color="text.secondary" component="p" sx={{ mt: 1 }}>
            {query.data[0]?.note}
          </Typography>
        </>
      )}
      {open !== null && (
        <HypothesisDrawer
          id={open}
          onClose={() => {
            url.update({ hypothesis: null });
          }}
        />
      )}
    </Panel>
  );
}
