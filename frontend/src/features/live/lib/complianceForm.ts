import { ApiError } from '@/shared/api';
import { formatDuration } from '@/shared/lib/format';
import { addDays } from './dates';
import type { Kind } from './labels';

export const maxSummary = 1000;
export const reviewDays = 90;
const maxSources = 10;
const maxSourceLength = 300;

/** What the server said, in words; a wrong password is an answer, not a sign-out. Nothing was saved in any of these cases. */
export function saveProblem(error: unknown): string {
  if (!(error instanceof ApiError)) return 'Could not reach the server. Nothing was saved.';
  const p = error.problem;
  const server = [p?.title, p?.detail].filter(Boolean).join(' ');
  switch (error.status) {
    case 401:
      return 'The password was not accepted. Nothing was saved.';
    case 403:
      return p?.code === 'live.capability_off'
        ? 'Writing records is switched off in this deployment (Live__Writes__Enabled). Nothing was saved.'
        : `Not allowed. ${server}`.trim();
    case 422:
      return `${server} Nothing was saved.`.trim();
    case 423:
      return `Too many wrong passwords; sign-in is locked for now. ${server}`.trim();
    case 429: {
      const wait = p?.retryAfterSeconds;
      return `${server || 'Limit reached.'}${wait ? ` Try again in ${formatDuration(wait)}.` : ''}`.trim();
    }
    default:
      return server || 'The request failed. Nothing was saved.';
  }
}

export interface Fields {
  kind: Kind;
  outcome: string;
  decidedOn: string;
  summary: string;
  sources: string;
  staticIpState: string;
  isp: string;
  monthlyCost: string;
  ipVersion: string;
  whitelistedAt: string;
  nextReviewOn: string;
}

/** The refusals the form can see without the server; the server decides in the end (it also screens for addresses, client ids and tokens). */
export function validate(f: Fields, today: string): Partial<Record<keyof Fields, string>> {
  const out: Partial<Record<keyof Fields, string>> = {};
  const sources = f.sources
    .split('\n')
    .map((s) => s.trim())
    .filter(Boolean);
  if (f.summary.trim().length === 0) out.summary = 'Write what you were told or read.';
  else if (f.summary.length > maxSummary) out.summary = `At most ${String(maxSummary)} characters.`;
  if (sources.length > maxSources) out.sources = `At most ${String(maxSources)} sources.`;
  else if (sources.some((s) => s.length > maxSourceLength))
    out.sources = `Each source is at most ${String(maxSourceLength)} characters.`;
  else if (sources.some((s) => !/^https:\/\/\S+$/.test(s) && !/^(SEBI|NSE|BSE)\/[A-Za-z0-9/._-]{3,60}$/.test(s)))
    out.sources = 'Each source is an https:// URL or a circular id such as NSE/INVG/73992.';
  if (!f.decidedOn) out.decidedOn = 'Pick the date.';
  else if (f.decidedOn > today) out.decidedOn = 'The decision date cannot be in the future.';
  if (!f.nextReviewOn) out.nextReviewOn = 'Pick the review date.';
  else if (f.decidedOn && f.nextReviewOn <= f.decidedOn)
    out.nextReviewOn = 'The review date is after the decision date.';
  else if (f.decidedOn && f.nextReviewOn > addDays(f.decidedOn, 400))
    out.nextReviewOn = 'The review date is at most 400 days after the decision.';
  if (f.kind === 'static_ip') {
    if (f.staticIpState === 'whitelisted' && !f.whitelistedAt)
      out.whitelistedAt = 'Whitelisted needs the date you set it at Dhan.';
    if (f.whitelistedAt && f.decidedOn && f.whitelistedAt > f.decidedOn)
      out.whitelistedAt = 'The whitelisting date cannot follow the decision date.';
    if (f.isp.length > 80) out.isp = 'At most 80 characters.';
    if (f.monthlyCost && !(Number(f.monthlyCost) >= 0)) out.monthlyCost = 'A number of rupees, 0 or more.';
  }
  return out;
}
