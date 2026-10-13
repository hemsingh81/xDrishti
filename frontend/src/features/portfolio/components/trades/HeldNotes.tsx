import { Box, Typography } from '@mui/material';
import type { TradePreviewReport, TradeSecurity } from '@/shared/api';
import { formatNumber } from '@/shared/lib/format';
import { countOf } from '../../lib/tradeText';
import { Disclosure } from './Disclosure';

/** The securities that are not held, and of those the ones that are not listed shares or ETFs: stored by an import, never applied. */
export function HeldNotes({ report, unmatched }: { report: TradePreviewReport; unmatched: readonly TradeSecurity[] }) {
  const { notHeld } = report;
  if (notHeld.trades === 0 && unmatched.length === 0) return null;
  return (
    <Box>
      {notHeld.trades > 0 && (
        <Typography variant="body2">
          {`${countOf(notHeld.trades, 'trade')} for ${countOf(notHeld.securities, 'security', 'securities')} not held are stored, not applied. If you hold one of them later, Rebuild lots will use its trades.`}
        </Typography>
      )}
      {unmatched.length > 0 && (
        <Box sx={{ mt: 0.5 }}>
          <Typography variant="body2" color="text.secondary">
            {`${countOf(report.unmatched.trades, 'trade')} for ${countOf(report.unmatched.securities, 'security', 'securities')} of those are not NSE shares or ETFs of the instrument list (not recognised).`}
          </Typography>
          <Disclosure
            label={`Show the ${countOf(unmatched.length, 'security not recognised', 'securities not recognised')}`}
            openLabel="Hide the securities not recognised"
          >
            <Box component="ul" aria-label="Securities not recognised" sx={{ m: 0, pl: 3 }}>
              {unmatched.map((security) => (
                <li key={`${security.symbol}-${security.isin ?? ''}`}>
                  <span>{security.symbol}</span>
                  {security.isin ? ` (${security.isin})` : ''}
                  {` · ${countOf(security.trades, 'trade')}`}
                </li>
              ))}
            </Box>
            {report.unmatched.securities > unmatched.length && (
              <Typography variant="caption" color="text.secondary" sx={{ display: 'block', mt: 0.5 }}>
                {`Showing the first ${formatNumber(unmatched.length)} of ${formatNumber(report.unmatched.securities)}.`}
              </Typography>
            )}
          </Disclosure>
        </Box>
      )}
    </Box>
  );
}
