import { Box } from '@mui/material';
import type { PortfolioBucket } from '@/shared/api';
import { customBuckets } from '../lib/portfolioForm';

/** The buckets an account can use: its own default first, then the custom ones. */
export function BucketList({ ownBucketName, buckets }: { ownBucketName: string; buckets: PortfolioBucket[] }) {
  return (
    <Box component="ul" aria-label="Buckets" sx={{ m: 0, mb: 1.5, pl: 2.5, typography: 'body2' }}>
      <li>
        {ownBucketName}{' '}
        <Box component="span" sx={{ color: 'text.secondary' }}>
          (default for this account)
        </Box>
      </li>
      {customBuckets(buckets).map((bucket) => (
        <li key={bucket.id}>{bucket.name}</li>
      ))}
    </Box>
  );
}
