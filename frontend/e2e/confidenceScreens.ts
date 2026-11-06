import { buildFullLoopReport, buildProposal, buildProposals, mountConfidence } from '../src/test/confidenceFixtures';
import { learningFull } from './learningMounts';
import type { Screen } from './matrix';
import { p15Screens } from './p15Screens';

/** The CF-12 Learning tabs (Decay and Loop); the Loop tab is also covered in today's all-unavailable state. */
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
];

/** The screens added after P8 that the browser runner covers (P15-11, CF-12). */
export const laterScreens: Screen[] = [...p15Screens, ...confidenceScreens];
