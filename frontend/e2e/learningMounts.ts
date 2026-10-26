import './shim';
import {
  buildCell,
  buildEntry,
  buildExperiment,
  buildGradedPlanVsActual,
  buildHypothesis,
  buildLesson,
  buildModel,
  buildModelDetail,
  buildOverview,
  buildDigest,
  buildThinCell,
  mountLearning,
  mountRecords,
} from '../src/test/learningFixtures';

// Fixture mounts of the Learning screens for the browser runner (P8-16): full, thin and empty.
type Cell = ReturnType<typeof buildCell>;
const cells = (): Cell[] => [
  buildCell(),
  buildCell({ strategy: 'vwap', timeframe: '5m', expectancyR: -0.12 }),
  buildCell({ strategy: 'orb', timeframe: '5m', side: 'SELL' }),
  buildThinCell({ strategy: 'gap' }),
];
export const learningFull = () => {
  mountLearning({
    overview: buildOverview({ cellRows: 12, cellsAsOf: '2026-10-20' }),
    cells: { all: cells(), '250d': cells(), '60d': cells() },
    changelog: [buildEntry()],
    planVsActual: buildGradedPlanVsActual(),
  });
};
export const learningThin = () => {
  mountLearning({
    overview: buildOverview({ cellRows: 2, cellsAsOf: '2026-10-20' }),
    cells: { all: [buildThinCell(), buildThinCell({ strategy: 'vwap' })] },
  });
};
export const learningEmpty = () => {
  mountLearning({});
};
export const records = () => {
  mountRecords({
    lessons: [buildLesson(), buildLesson({ id: 2 })],
    experiments: [buildExperiment(), buildExperiment({ id: 2 })],
    hypotheses: [buildHypothesis()],
    models: [buildModel()],
    modelDetails: [buildModelDetail()],
    digests: [buildDigest()],
  });
  learningFull();
};
export const recordsEmpty = () => {
  mountRecords({});
  learningEmpty();
};
