import { Card, CardContent, Typography } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import { useId, type ReactNode } from 'react';
import { LoadingState } from '@/shared/ui';
import { ReportState } from './ReportState';

interface PanelProps {
  title: string;
  children: ReactNode;
}

/** A titled card section: the page's unit of layout. The heading names the region. */
export function Panel({ title, children }: PanelProps) {
  const titleId = useId();
  return (
    <Card component="section" aria-labelledby={titleId} sx={{ minWidth: 0 }}>
      <CardContent>
        <Typography id={titleId} variant="h2" component="h2" sx={{ mb: 1.5 }}>
          {title}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

interface QueryPanelProps<T> {
  title: string;
  query: UseQueryResult<T>;
  children: (data: T) => ReactNode;
}

/** A panel over one query: its own loading and error state, so a failing panel does not blank the page. */
export function QueryPanel<T>({ title, query, children }: QueryPanelProps<T>) {
  return (
    <Panel title={title}>
      {query.isPending ? (
        <LoadingState label={`Loading ${title.toLowerCase()}…`} />
      ) : query.isError ? (
        <ReportState error={query.error} />
      ) : (
        children(query.data)
      )}
    </Panel>
  );
}
