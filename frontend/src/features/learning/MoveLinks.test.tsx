import { screen } from '@testing-library/react';
import type { Schemas } from '@/shared/api';
import { createCapabilitiesFake } from '@/test/capabilitiesFake';
import {
  buildAutonomy,
  buildCell,
  buildEntry,
  buildHypothesis,
  buildLesson,
  mountLearning,
  mountRecords,
} from '@/test/learningFixtures';
import { server } from '@/test/server';
import { renderLearning } from './testing';

function capabilities(on: ('learning_moves_ui' | 'autonomy_form')[]) {
  const fake = createCapabilitiesFake();
  fake.state.items = fake.state.items.map((i) => ({ ...i, enabled: on.some((name) => name === i.name) }));
  server.use(...fake.handlers);
}

describe('Learning keeps no write control: it links to the owner-act forms (P8-19)', () => {
  it('a lesson links to its form only while the switch is on, and says so (with a link to the switches) while it is off', async () => {
    capabilities(['learning_moves_ui']);
    mountRecords({ lessons: [buildLesson({ id: 4 })] });
    renderLearning('?tab=lessons&lesson=4');

    expect(await screen.findByRole('link', { name: 'Move this lesson' })).toHaveAttribute(
      'href',
      '/owner-acts/lesson/4',
    );
    expect(screen.queryByRole('button', { name: /^(dismiss|link a fix|close with)/i })).toBeNull();
  });

  it('off: only the words and the link to the switches', async () => {
    capabilities([]);
    mountRecords({ lessons: [buildLesson({ id: 4 })] });
    renderLearning('?tab=lessons&lesson=4');

    expect(await screen.findByRole('link', { name: /switched off: owner-act switches/ })).toHaveAttribute(
      'href',
      '/owner-acts',
    );
    expect(screen.queryByRole('link', { name: 'Move this lesson' })).toBeNull();
  });

  it('a hypothesis links to its form while the switch is on', async () => {
    capabilities(['learning_moves_ui']);
    mountRecords({ hypotheses: [buildHypothesis({ id: 6 })] });
    renderLearning('?tab=experiments&hypothesis=6');

    expect(await screen.findByRole('link', { name: 'Move this hypothesis' })).toHaveAttribute(
      'href',
      '/owner-acts/hypothesis/6',
    );
  });

  it('the newest transition of a cell links to the revert form; an older one and a revert do not', async () => {
    capabilities(['learning_moves_ui']);
    const key = buildCell().cellKey;
    mountLearning({
      cells: { all: [buildCell()] },
      changelog: [
        buildEntry({ id: 9, subject: key }),
        buildEntry({ id: 8, subject: key }),
        buildEntry({ id: 7, subject: key, kind: 'revert' }),
      ],
    });
    renderLearning('?tab=setups&cell=' + encodeURIComponent(key));

    const links = await screen.findAllByRole('link', { name: 'Revert this move' });
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAttribute('href', '/owner-acts/revert/9');
  });

  it('the Autonomy tab links to the form only while its switch is on', async () => {
    capabilities(['autonomy_form']);
    mountLearning({ autonomy: buildAutonomy() });
    renderLearning('?tab=autonomy');

    expect(await screen.findByRole('link', { name: 'Change these switches' })).toHaveAttribute('href', '/owner-acts');
  });
});

const link = (over: Partial<Schemas['UniverseLinkView']> = {}): Schemas['UniverseLinkView'] => ({
  childKey: 'ORB|15m|BUY|intraday|FIXED_RR|*|liq_high',
  parentKey: 'ORB|15m|BUY|intraday|FIXED_RR|*',
  universe: 'liq_high',
  window: 'all',
  asOf: '2026-03-10',
  reason: 'child_better',
  readKey: 'ORB|15m|BUY|intraday|FIXED_RR|*|liq_high',
  childN: 120,
  parentN: 200,
  childLower: 0.53,
  parentLower: 0.44,
  observation: true,
  holdOut: 'stable',
  holdOutFirstN: 60,
  holdOutSecondN: 60,
  text: 'ORB, 15m, BUY, intraday, liq_high: stronger than all shares; an observation, nothing is applied',
  ...over,
});

describe('Setups lists the tier links and links an observation to the proposal form (BK-11b)', () => {
  it('an observation links to the owner act while the switch is on; a thin tier has no link', async () => {
    capabilities(['learning_moves_ui']);
    mountLearning({
      universeLinks: [link(), link({ childKey: 'ORB|15m|BUY|intraday|FIXED_RR|*|liq_low', observation: false })],
    });
    renderLearning('?tab=setups');

    expect(await screen.findByRole('link', { name: 'Propose as hypothesis' })).toHaveAttribute(
      'href',
      '/owner-acts/universe/ORB%7C15m%7CBUY%7Cintraday%7CFIXED_RR%7C*%7Cliq_high',
    );
    expect(screen.getAllByRole('link', { name: 'Propose as hypothesis' })).toHaveLength(1);
    expect(screen.queryByRole('button', { name: /propose/i })).toBeNull();
  });
});
