import { buildFullLoopReport, buildProposal, buildProposals, mountConfidence } from '../src/test/confidenceFixtures';
import { buildCard, buildList, mixedRows, mountReadiness, scoredSubs } from '../src/test/readinessFixtures';
import { learningFull } from './learningMounts';
import type { Screen } from './matrix';
import { p15Screens } from './p15Screens';
import { recoveryScreens } from './recoveryScreen';
import { autoScreens } from './autoMounts';
import { simulationScreens } from './simulationMounts';
import { settingsScreens } from './settingsScreen';

/** The CF-12 Learning tabs (Decay and Loop; the Loop tab is also covered in today's all-unavailable state) and the CF-11 Readiness screen. */
const confidenceScreens: Screen[] = [
  {
    id: 'CF-12-decay',
    route: '/learning?tab=decay&strategy=orb',
    mounts: {
      full: () => {
        learningFull();
        mountConfidence({ proposals: buildProposals({ items: [buildProposal()] }) });
      },
    },
  },
  {
    id: 'CF-12-loop',
    route: '/learning?tab=loop',
    mounts: {
      full: () => {
        learningFull();
        mountConfidence({ loop: buildFullLoopReport() });
      },
      unavailable: () => {
        learningFull();
        mountConfidence();
      },
    },
  },
  {
    id: 'CF-11-readiness',
    route: '/readiness',
    mounts: {
      full: () => {
        mountReadiness({
          cards: { 'orb@3': buildCard({ score: 20.2, subScores: scoredSubs(), hardFails: [], rows: mixedRows() }) },
        });
      },
      empty: () => {
        mountReadiness({ list: buildList({ strategies: [] }) });
      },
      // Today's reality: every input unavailable, every score 0.
      unavailable: () => {
        mountReadiness();
      },
    },
  },
];

/** The screens added after P8 that the browser runner covers (P15-11, CF-12). */
export const laterScreens: Screen[] = [
  ...p15Screens,
  ...confidenceScreens,
  ...recoveryScreens,
  ...settingsScreens,
  ...simulationScreens,
  ...autoScreens,
];
