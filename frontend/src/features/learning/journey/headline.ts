import type { Counterfactual } from '../api/learningQueries';

/** Below this many judged windows a verdict that retraining helped (or hurt) is shown as weak (spec P8-12 decision 5). */
export const WEAK_BELOW = 6;

export interface Headline {
  /** The sentence. It never says "improved" on a thin verdict. */
  text: string;
  /** `neutral` is the only tone a thin or no-difference answer gets; a gain figure is coloured only when `judged`. */
  tone: 'neutral' | 'judged';
  /** Whether the pooled gain may be coloured as a gain or a loss. */
  colourGain: boolean;
}

/** The headline from the stored outcome and counts; the wording of the reasons is the server's and is listed beneath. */
export function headlineOf(report: Counterfactual): Headline {
  const judged = report.judgedWindows;
  const needed = report.knobs.minJudgedWindows;
  if (judged < needed) {
    return { text: `Inconclusive: ${judged} judged windows, ${needed} needed`, tone: 'neutral', colourGain: false };
  }
  switch (report.outcome) {
    case 'NoDifference':
      return { text: 'No measurable difference on this data', tone: 'neutral', colourGain: false };
    case 'RetrainingHelps':
      return weak('Retraining helped out of sample', judged);
    case 'FrozenBetter':
      return weak('The frozen model did better out of sample', judged);
    default:
      return { text: 'Inconclusive: a rule capped the verdict (reasons below)', tone: 'neutral', colourGain: false };
  }
}

function weak(text: string, judged: number): Headline {
  const suffix = judged < WEAK_BELOW ? `, weak (${judged} windows)` : '';
  return { text: text + suffix, tone: 'judged', colourGain: judged >= WEAK_BELOW };
}
