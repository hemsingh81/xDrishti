import type { ReactNode } from 'react';
import { StatTile } from '@/shared/ui/StatTile';
import { EvidenceBlock, type EvidenceProps } from './EvidenceBlock';
import { MetricValue, type MetricValueProps } from './MetricValue';

interface MetricTileProps extends MetricValueProps {
  label: string;
  hint?: ReactNode;
  /** The evidence under the figure (compact line). Pass it for any real figure; tiles of non-values may omit it. */
  evidence?: Omit<EvidenceProps, 'compact'>;
}

/**
 * A KPI tile whose value is a `MetricState`. The tile's own `good` / `bad` tone is never used (it would colour a non-value);
 * the figure is coloured by `MetricValue` and only when it is a real value.
 */
export function MetricTile({ label, hint, evidence, ...metric }: MetricTileProps) {
  return (
    <StatTile
      label={label}
      value={<MetricValue {...metric} />}
      hint={
        hint || evidence ? (
          <>
            {hint}
            {evidence && <EvidenceBlock {...evidence} compact />}
          </>
        ) : undefined
      }
    />
  );
}
