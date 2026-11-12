import { Card, CardContent, Typography } from '@mui/material';
import type { UseQueryResult } from '@tanstack/react-query';
import { useId, type ReactNode } from 'react';
import { LoadingState } from '@/shared/ui';
import { ReportState } from './ReportState';

interface PanelProps {
  title: string;
  children: ReactNode;
  minHeight?: Reserve;
}

/** A titled card section: the page's unit of layout. The heading names the region. */
export function Panel({ title, children, minHeight }: PanelProps) {
  const titleId = useId();
  return (
    <Card
      component="section"
      aria-labelledby={titleId}
      sx={{ minWidth: 0, minHeight: minHeight ? { xs: minHeight.xs, md: minHeight.md } : undefined }}
    >
      <CardContent>
        <Typography id={titleId} variant="h2" component="h2" sx={{ mb: 1.5 }}>
          {title}
        </Typography>
        {children}
      </CardContent>
    </Card>
  );
}

/** Heights (px) of a settled panel on a phone and from `md`, measured; the loading panel holds them so what follows does not jump. */
export interface Reserve {
  xs: number;
  md: number;
}

interface QueryPanelProps<T> {
  title: string;
  /** Height to hold while the query is pending (layout shift, spec P8-16 C14). Keep it at or under about one screen. */
  reserve?: Reserve;
  query: UseQueryResult<T>;
  children: (data: T) => ReactNode;
}

/** A panel over one query: its own loading and error state, so a failing panel does not blank the page. */
export function QueryPanel<T>({ title, query, reserve, children }: QueryPanelProps<T>) {
  return (
    <Panel title={title} minHeight={query.isPending ? reserve : undefined}>
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
