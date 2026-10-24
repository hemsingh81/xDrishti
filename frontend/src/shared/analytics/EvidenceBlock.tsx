import WarningIcon from '@mui/icons-material/WarningAmberOutlined';
import { Box, Typography } from '@mui/material';
import { numeric } from '@/theme';
import { formatCount, formatRange } from './format';

/** What the figures rest on. Every field is required: a report without its evidence does not type-check. */
export interface EvidenceProps {
  /** Observations (trades, plans) behind the numbers; `null` = the server did not say. */
  n: number | null;
  /** Calendar dates (yyyy-MM-dd) of the data; `null` = not stated. */
  range: { from: string; to: string } | null;
  /** `gross` = R before costs, `net` = after the stored charge sheet, `unknown` = the server did not say. */
  basis: 'gross' | 'net' | 'unknown';
  /** True when the figures rest on a provisional input (a provisional plan, a partial window). */
  provisional?: boolean;
  /** The runs or plans behind the numbers ("Data used"). */
  population?: readonly string[];
  /** The limits text the server sends with the report (in-sample, modelled fills, survivorship ...). */
  limits?: readonly string[];
  /** The landmark name when a page holds more than one block ("Evidence" by default). */
  label?: string;
  /** One line, for beside a tile or a table cell, instead of the labelled block. */
  compact?: boolean;
}

const basisText = {
  gross: 'Gross R (before costs)',
  net: 'Net R (after the stored charge sheet)',
  unknown: 'Basis not stated',
} as const;

/**
 * The evidence beside a number: n, the data range, the basis (gross or net), whether it is provisional, the data used and
 * the limits. The wording of the limits is the server's (a field), not page prose. Provisional and "basis not stated" carry
 * an icon and the words; nothing here is a green "ok".
 */
export function EvidenceBlock({
  n,
  range,
  basis,
  provisional = false,
  population,
  limits,
  compact = false,
  label = 'Evidence',
}: EvidenceProps) {
  const nText = n === null ? 'n not reported' : `n ${formatCount(n)}`;
  const rangeText = range ? formatRange(range) : 'range not stated';
  if (compact) {
    const parts = [nText, rangeText, basisText[basis], ...(provisional ? ['Provisional'] : [])];
    return (
      <Typography component="p" variant="caption" color="text.secondary" sx={{ m: 0, ...numeric }}>
        {parts.join(' · ')}
      </Typography>
    );
  }
  return (
    <Box component="section" aria-label={label} sx={{ color: 'text.secondary', fontSize: 'caption.fontSize' }}>
      <Box
        component="dl"
        sx={{
          m: 0,
          display: 'flex',
          flexWrap: 'wrap',
          columnGap: 3,
          rowGap: 0.5,
          '& dt': { fontWeight: 600, display: 'inline', mr: 0.75 },
          '& dd': { display: 'inline', m: 0, color: 'text.primary', ...numeric },
          '& .item': { m: 0 },
        }}
      >
        <Box className="item">
          <dt>Sample</dt>
          <dd>{nText}</dd>
        </Box>
        <Box className="item">
          <dt>Range</dt>
          <dd>{rangeText}</dd>
        </Box>
        <Box className="item">
          <dt>Basis</dt>
          <dd>{basisText[basis]}</dd>
        </Box>
        {provisional && (
          <Box className="item">
            <dt>Status</dt>
            <dd>
              <Box component="span" sx={{ color: 'warning.main', fontWeight: 600 }}>
                <WarningIcon aria-hidden sx={{ fontSize: '1.1em', verticalAlign: 'text-bottom', mr: 0.5 }} />
                Provisional
              </Box>
            </dd>
          </Box>
        )}
      </Box>
      {population && population.length > 0 && (
        <Typography component="p" variant="caption" sx={{ m: 0, mt: 0.5 }}>
          <strong>Data used:</strong> {population.join(', ')}
        </Typography>
      )}
      {limits && limits.length > 0 && (
        <Box component="ul" aria-label="Limits" sx={{ m: 0, mt: 0.5, pl: 2.5 }}>
          {limits.map((limit, index) => (
            <li key={index}>{limit}</li>
          ))}
        </Box>
      )}
    </Box>
  );
}
