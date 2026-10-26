import { screen } from '@testing-library/react';
import { http, HttpResponse } from 'msw';
import { Link } from 'react-router';
import type { Schemas } from '@/shared/api';
import { ReportsPage } from '@/features/reports';
import {
  buildDistribution,
  buildEquity,
  buildEvidence,
  buildGroup,
  buildGroups,
  buildInsights,
  buildMaeMfe,
  buildPivot,
  buildRunsView,
  buildWhatWorks,
  partsOf,
  value,
} from './reportFixtures';
import { scrollToEnd } from './intersection';
import { renderRoutes } from './render';
import { problemResponse, server } from './server';

// The server's own refusals (ReportErrors.DimensionUnavailable); the page prints the problem's `title`.
export const dimensionReasons = {
  regime: 'Regime is unavailable: no market context is stored, so no trade has a regime.',
  account: 'Account is unavailable: a simulated trade has no account.',
} as const;

export interface ReportOptions {
  runs?: Schemas['RunsView'];
  whatWorks?: Schemas['WhatWorksView'];
  summary?: Schemas['SummaryView'];
}

/** Registers the report endpoints over the fixtures (a run, a "nothing qualifies" answer); returns every URL the page asked. */
export function reportServer(opts: ReportOptions = {}) {
  const requests: URL[] = [];
  const seen = (request: Request) => {
    requests.push(new URL(request.url));
  };
  server.use(
    http.get('/api/reports/runs', ({ request }) => {
      seen(request);
      return HttpResponse.json(opts.runs ?? buildRunsView());
    }),
    http.get('/api/reports/summary', ({ request }) => {
      seen(request);
      return HttpResponse.json(opts.summary ?? { all: buildGroup(), evidence: buildEvidence() });
    }),
    http.get('/api/reports/groups', ({ request }) => {
      seen(request);
      const lens = new URL(request.url).searchParams.get('lens') ?? '';
      if (lens === 'regime' || lens === 'account') {
        return problemResponse({ title: dimensionReasons[lens], code: 'reports.dimension_unavailable' }, 422);
      }
      if (lens === 'basket') {
        return HttpResponse.json(
          buildGroups('basket', [
            buildGroup({
              key: 'b1',
              parts: partsOf('basket', '00000000-0000-4000-8000-000000000401'),
              expectancyR: value(0.3),
            }),
          ]),
        );
      }
      if (lens === 'weekday') {
        return HttpResponse.json(
          buildGroups('weekday', [
            buildGroup({ key: 'w2', parts: partsOf('weekday', 'Tue'), winRate: value(0.6) }),
            buildGroup({ key: 'w1', parts: partsOf('weekday', 'Mon'), winRate: value(0.5) }),
          ]),
        );
      }
      return HttpResponse.json(
        buildGroups('month', [
          buildGroup({ key: 'm1', parts: partsOf('month', '2026-03'), totalR: value(4.2) }),
          buildGroup({ key: 'm2', parts: partsOf('month', '2026-04'), totalR: value(-2.1) }),
        ]),
      );
    }),
    http.get('/api/reports/pivot', ({ request }) => {
      seen(request);
      return HttpResponse.json(buildPivot());
    }),
    http.get('/api/reports/equity', ({ request }) => {
      seen(request);
      return HttpResponse.json(buildEquity());
    }),
    http.get('/api/reports/distribution', ({ request }) => {
      seen(request);
      return HttpResponse.json(buildDistribution());
    }),
    http.get('/api/reports/mae-mfe', ({ request }) => {
      seen(request);
      return HttpResponse.json(buildMaeMfe());
    }),
    http.get('/api/reports/insights', ({ request }) => {
      seen(request);
      return HttpResponse.json(buildInsights());
    }),
    http.get('/api/reports/what-works', ({ request }) => {
      seen(request);
      return HttpResponse.json(opts.whatWorks ?? buildWhatWorks());
    }),
  );
  return requests;
}

/** Renders the Reports screen at `/reports` + `search`; `/` holds a link standing for the navigation entry. */
export function renderReports(search = '') {
  return renderRoutes(
    [
      { path: '/', element: <Link to="/reports">Reports</Link> },
      { path: '/reports', element: <ReportsPage /> },
    ],
    { initialPath: `/reports${search}` },
  );
}

/** Renders from the start page, so the navigation click is part of the interactions. */
export function renderReportsFromStart() {
  return renderRoutes(
    [
      { path: '/', element: <Link to="/reports">Reports</Link> },
      { path: '/reports', element: <ReportsPage /> },
    ],
    { initialPath: '/' },
  );
}

/** The owner scrolls down the Overview: the panels below the first view mount (P8-17f). Call after `renderReports()`. */
export async function scrollOverviewToEnd() {
  await screen.findByRole('region', { name: 'Equity and drawdown' });
  scrollToEnd();
}
