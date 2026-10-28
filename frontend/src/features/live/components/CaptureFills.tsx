import { Typography } from '@mui/material';
import { formatPrice } from '@/shared/lib/format';
import type { CaptureFill } from '../api/captureQueries';

/** A list of fills no ticket owns, each with the reason in words (unmatched: it fits two tickets; discretionary: your own trade). */
export function CaptureFills({ title, fills, empty }: { title: string; fills: readonly CaptureFill[]; empty: string }) {
  return (
    <section aria-label={title}>
      <Typography variant="h3" component="h3">
        {title}
      </Typography>
      {fills.length === 0 ? (
        <Typography>{empty}</Typography>
      ) : (
        <table aria-label={title}>
          <thead>
            <tr>
              <th scope="col">Time</th>
              <th scope="col">Symbol</th>
              <th scope="col">Side</th>
              <th scope="col">Product</th>
              <th scope="col">Quantity</th>
              <th scope="col">Price</th>
              <th scope="col">Why</th>
            </tr>
          </thead>
          <tbody>
            {fills.map((f) => (
              <tr key={`${f.time ?? ''}-${f.symbol}-${f.side}-${String(f.qty)}-${String(f.price)}`}>
                <td>{f.time ?? 'no time'}</td>
                <th scope="row">{f.symbol}</th>
                <td>{f.side}</td>
                <td>{f.product}</td>
                <td>{f.qty}</td>
                <td>{formatPrice(f.price)}</td>
                <td>{f.reason}</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </section>
  );
}
