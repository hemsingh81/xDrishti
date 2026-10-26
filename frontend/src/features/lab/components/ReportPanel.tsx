import DownloadIcon from '@mui/icons-material/DownloadOutlined';
import OpenIcon from '@mui/icons-material/OpenInNew';
import {
  Alert,
  Box,
  Button,
  Link,
  List,
  ListItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography,
} from '@mui/material';
import { useQuery } from '@tanstack/react-query';
import { Link as RouterLink } from 'react-router';
import { useCapability } from '@/features/owner-acts';
import { formatNumber } from '@/shared/lib/format';
import { ErrorState, LoadingState } from '@/shared/ui';
import { reportHtmlHref, reportQuery, reportZipHref } from '../api/runQueries';
import { RobustnessSummary } from './RobustnessSummary';
import { noLimits } from './VerdictPanel';

const num = (v: number | null): string => (v === null ? 'n/a' : formatNumber(v));

function Bullets({ label, items }: { label: string; items: string[] }) {
  if (items.length === 0) return null;
  return (
    <Box>
      <Typography variant="subtitle2">{label}</Typography>
      <List dense disablePadding aria-label={label}>
        {items.map((t, i) => (
          <ListItem key={`${i}${t}`} disableGutters>
            {t}
          </ListItem>
        ))}
      </List>
    </Box>
  );
}

/** The report of a finished full or robust test, from the stored result, with the page and the folder to keep. */
export function ReportPanel({ runId }: { runId: string }) {
  const { data: r, error, isPending } = useQuery(reportQuery(runId));
  const downloads = useCapability('exports');
  if (isPending) return <LoadingState label="Loading the report" />;
  if (error) return <ErrorState error={error} />;
  return (
    <Stack spacing={2} component="section" aria-label="Report">
      <Stack direction="row" sx={{ gap: 1, alignItems: 'center', flexWrap: 'wrap' }}>
        <Typography variant="h3" component="h3" sx={{ flexGrow: 1 }}>
          Report
        </Typography>
        {downloads.enabled ? (
          <>
            <Button component="a" href={reportHtmlHref(runId)} target="_blank" rel="noopener" startIcon={<OpenIcon />}>
              Open report page
            </Button>
            <Button component="a" href={reportZipHref(runId)} download startIcon={<DownloadIcon />}>
              Download folder (zip)
            </Button>
          </>
        ) : (
          <Typography variant="body2" color="text.secondary">
            {'Report downloads are switched off: '}
            <Link component={RouterLink} to="/owner-acts">
              owner-act switches
            </Link>
            .
          </Typography>
        )}
      </Stack>
      <Typography>{r.summary}</Typography>
      <Typography color="text.secondary">
        {r.kpis.oosTrades} out-of-sample trades · expectancy {num(r.kpis.expectancyR)} R · profit factor{' '}
        {num(r.kpis.profitFactor)} · max drawdown {num(r.kpis.maxDrawdownR)} R · {r.kpis.positiveFolds} of{' '}
        {r.kpis.folds} folds positive
      </Typography>
      {r.warnings.length > 0 && (
        <Alert severity="warning">
          <List dense disablePadding aria-label="Warnings">
            {r.warnings.map((w, i) => (
              <ListItem key={`${i}${w.code}`} disableGutters>
                {w.text}
              </ListItem>
            ))}
          </List>
        </Alert>
      )}
      <Alert severity="info">
        <Bullets label="Honest limits" items={r.honestLimits.length > 0 ? r.honestLimits : [noLimits]} />
      </Alert>
      {r.bestCells.length > 0 && (
        <Table size="small" aria-label="Best cells">
          <TableHead>
            <TableRow>
              <TableCell>Cell</TableCell>
              <TableCell>Chosen exit</TableCell>
              <TableCell align="right">OOS trades</TableCell>
              <TableCell align="right">OOS mean R</TableCell>
              <TableCell>Stable</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {r.bestCells.map((c, i) => (
              <TableRow key={`${i}${c.cell}`}>
                <TableCell>{c.cell}</TableCell>
                <TableCell>{c.chosen}</TableCell>
                <TableCell align="right">{c.oosTrades}</TableCell>
                <TableCell align="right">{num(c.oosMeanR)}</TableCell>
                <TableCell>{c.stable === null ? 'unknown' : c.stable ? 'Yes' : 'No'}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {r.exploration.length > 0 && (
        <Table size="small" aria-label="Exploration, in-sample">
          <TableHead>
            <TableRow>
              <TableCell>Cell</TableCell>
              <TableCell>Candidate</TableCell>
              <TableCell align="right">Trades</TableCell>
              <TableCell align="right">Mean R</TableCell>
              <TableCell>Label</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {r.exploration.map((e, i) => (
              <TableRow key={`${i}${e.cell}${e.candidate}`}>
                <TableCell>{e.cell}</TableCell>
                <TableCell>{e.candidate}</TableCell>
                <TableCell align="right">{e.trades}</TableCell>
                <TableCell align="right">{num(e.meanR)}</TableCell>
                <TableCell>{e.label}</TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      )}
      {r.robustness && <RobustnessSummary robustness={r.robustness} />}
      <Bullets label="Not in this report" items={r.notIncluded} />
    </Stack>
  );
}
