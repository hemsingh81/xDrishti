import { ApiError } from '@/shared/api';
import { buildBasket, buildPortfolioBasket } from '@/test/fixtures';
import { createAnnouncements } from './announcements';
import { basketFormFrom, toBasketInput, validateBasket } from './basketForm';
import { resolveDrop, sessionFor, type DragSession, type DropTarget } from './dropRules';
import {
  appliedText,
  basketFieldErrors,
  basketProblemText,
  isConflict,
  isStaleOrder,
  isSystemRefusal,
  PartialAddError,
  reorderProblemText,
} from './errors';
import { manualBaskets, splitBaskets } from './groups';
import { capProblem, maxBasketMembers } from './limits';
import { addedMessage, removedMessage, transferMessage } from './messages';
import { mergeOrder, moveBlock, sameOrder } from './order';

describe('moveBlock', () => {
  const order = ['a', 'b', 'c', 'd', 'e'];

  it('moves one item down to after the target and up to before it', () => {
    expect(moveBlock(order, ['b'], 'b', 'd')).toEqual(['a', 'c', 'd', 'b', 'e']);
    expect(moveBlock(order, ['d'], 'd', 'b')).toEqual(['a', 'd', 'b', 'c', 'e']);
  });

  it('moves several as a block, keeping their order, around the grabbed one', () => {
    expect(moveBlock(order, ['b', 'd'], 'd', 'a')).toEqual(['b', 'd', 'a', 'c', 'e']);
    expect(moveBlock(order, ['b', 'd'], 'b', 'e')).toEqual(['a', 'c', 'e', 'b', 'd']);
  });

  it('returns null when nothing changes or the ids are unknown', () => {
    expect(moveBlock(order, ['b'], 'b', 'b')).toBeNull();
    expect(moveBlock(order, ['b'], 'b', 'c')).toEqual(['a', 'c', 'b', 'd', 'e']);
    expect(moveBlock(order, ['b', 'c'], 'b', 'c')).toBeNull();
    expect(moveBlock(order, ['b'], 'b', 'zzz')).toBeNull();
  });
});

describe('mergeOrder and sameOrder', () => {
  it('puts the wanted order first, keeps what is still there and appends the new', () => {
    expect(mergeOrder(['c', 'a', 'x', 'b'], ['a', 'b', 'c', 'n'])).toEqual(['c', 'a', 'b', 'n']);
    expect(sameOrder(['a', 'b'], ['a', 'b'])).toBe(true);
    expect(sameOrder(['a', 'b'], ['b', 'a'])).toBe(false);
    expect(sameOrder(['a'], ['a', 'b'])).toBe(false);
  });
});

describe('resolveDrop', () => {
  const memberOrder = ['i1', 'i2', 'i3'];
  const basketOrder = ['b1', 'b2', 'b3'];
  const member = (ids: string[], activeId = ids[0] ?? ''): DragSession => ({
    kind: 'member',
    activeId,
    ids,
    sourceId: 'b1',
  });
  const onMember = (id: string, basketId = 'b1'): DropTarget => ({ id, data: { type: 'member', basketId } });
  const onBasket = (id: string): DropTarget => ({ id, data: { type: 'basket', basketId: id } });
  const context = { memberOrder, basketOrder, alt: false };

  it('reorders members dropped on another member', () => {
    expect(resolveDrop(member(['i1']), onMember('i3'), context)).toEqual({
      kind: 'reorder-members',
      ids: ['i2', 'i3', 'i1'],
    });
  });

  it('copies members dropped on another basket, moves them with Alt', () => {
    expect(resolveDrop(member(['i1', 'i2']), onBasket('b2'), context)).toEqual({
      kind: 'transfer',
      fromId: 'b1',
      toId: 'b2',
      ids: ['i1', 'i2'],
      mode: 'copy',
    });
    expect(resolveDrop(member(['i1']), onBasket('b3'), { ...context, alt: true })).toMatchObject({
      mode: 'move',
      toId: 'b3',
    });
  });

  it('does nothing on the own basket, on itself, nowhere or on a row of another basket', () => {
    expect(resolveDrop(member(['i1']), onBasket('b1'), context)).toEqual({ kind: 'none' });
    expect(resolveDrop(member(['i1']), onMember('i1'), context)).toEqual({ kind: 'none' });
    expect(resolveDrop(member(['i1']), null, context)).toEqual({ kind: 'none' });
    expect(resolveDrop(member(['i1']), onMember('x9', 'b2'), context)).toEqual({ kind: 'none' });
  });

  it('only ever copies the rows of a read-only (portfolio) basket: no reorder, Alt ignored', () => {
    const readOnly = { ...context, readOnly: true };
    expect(resolveDrop(member(['i1']), onMember('i3'), readOnly)).toEqual({ kind: 'none' });
    expect(resolveDrop(member(['i1']), onBasket('b2'), { ...readOnly, alt: true })).toMatchObject({
      kind: 'transfer',
      mode: 'copy',
    });
  });

  it('reorders baskets among baskets only', () => {
    const card: DragSession = { kind: 'basket', activeId: 'b1', ids: ['b1'], sourceId: 'b1' };
    expect(resolveDrop(card, onBasket('b2'), context)).toEqual({ kind: 'reorder-baskets', ids: ['b2', 'b1', 'b3'] });
    expect(resolveDrop(card, onMember('i1'), context)).toEqual({ kind: 'none' });
  });
});

describe('sessionFor', () => {
  const data = { type: 'member', basketId: 'b1' };

  it('takes the whole selection along when a selected row of several is grabbed, in list order', () => {
    expect(sessionFor('i3', data, new Set(['i3', 'i1']), ['i1', 'i2', 'i3'])?.ids).toEqual(['i1', 'i3']);
  });

  it('drags only the grabbed row when it is not selected or alone', () => {
    expect(sessionFor('i2', data, new Set(['i3', 'i1']), ['i1', 'i2', 'i3'])?.ids).toEqual(['i2']);
    expect(sessionFor('i1', data, new Set(['i1']), ['i1', 'i2'])?.ids).toEqual(['i1']);
    expect(sessionFor('i1', { nonsense: true }, new Set(), [])).toBeNull();
  });
});

describe('announcements', () => {
  const announcements = createAnnouncements({
    sessionOf: (active) =>
      sessionFor(String(active.id), active.data.current, new Set(['i1', 'i2']), ['i1', 'i2', 'i3']),
    memberOrder: ['i1', 'i2', 'i3'],
    basketOrder: ['b1', 'b2'],
    labelOf: (kind, id) =>
      ({ i1: 'TCS', i2: 'INFY', i3: 'WIPRO', b1: 'MyChoice', b2: 'Best stocks' })[id] ?? `${kind} ${id}`,
    isMove: () => false,
  });
  const active = {
    id: 'i1',
    data: { current: { type: 'member', basketId: 'b1' } },
    rect: { current: { initial: null, translated: null } },
  };
  const over = (id: string, type: 'member' | 'basket') => ({
    id,
    data: { current: { type, basketId: type === 'basket' ? id : 'b1' } },
    rect: { width: 0, height: 0, top: 0, left: 0, bottom: 0, right: 0 },
    disabled: false,
  });

  it('names what was picked up, with the selection', () => {
    expect(announcements.onDragStart({ active })).toBe('Picked up TCS and 1 more, position 1 of 3.');
  });

  it('says where the drag is and what a drop does', () => {
    expect(announcements.onDragOver({ active, over: over('i3', 'member') })).toBe(
      'TCS and 1 more is over WIPRO, position 3 of 3.',
    );
    expect(announcements.onDragOver({ active, over: over('b2', 'basket') })).toBe(
      'TCS and 1 more is over basket Best stocks. Dropping copies it there; hold Alt to move.',
    );
    expect(announcements.onDragOver({ active, over: null })).toBe('TCS and 1 more is not over a place to drop.');
  });

  it('reports the drop and the cancel', () => {
    expect(announcements.onDragEnd({ active, over: over('b2', 'basket') })).toBe(
      'Dropped TCS and 1 more on basket Best stocks.',
    );
    expect(announcements.onDragEnd({ active, over: null })).toBe('TCS and 1 more was dropped where it was.');
    expect(announcements.onDragCancel({ active, over: null })).toBe('Cancelled. TCS and 1 more stays where it was.');
  });
});

describe('messages', () => {
  const transfer = {
    mode: 'copy',
    requested: 3,
    added: 2,
    alreadyInTarget: 1,
    removed: 0,
    notInSource: 0,
    newlyTracked: 1,
    sourceMemberCount: 3,
    targetMemberCount: 5,
  };

  it('words a copy and a move', () => {
    expect(transferMessage(transfer, 'copy', 'Best stocks', '')).toBe(
      'Copied 3 instruments to Best stocks (1 already there; 1 was not tracked and now is)',
    );
    expect(
      transferMessage(
        { ...transfer, mode: 'move', removed: 3, alreadyInTarget: 0, newlyTracked: 0 },
        'move',
        'MyChoice',
        '',
      ),
    ).toBe('Moved 3 instruments to MyChoice');
    expect(
      transferMessage(
        { ...transfer, added: 1, alreadyInTarget: 0, newlyTracked: 0, notInSource: 2 },
        'copy',
        'X',
        'NSE:TCS',
      ),
    ).toBe('Copied NSE:TCS to X (2 no longer in this basket)');
  });

  it('words an add and a removal', () => {
    const add = { requested: 1, added: 1, alreadyMembers: 0, newlyTracked: 0, memberCount: 1 };
    expect(addedMessage(add, ['A', 'B'], 'NSE:TCS')).toBe('Added NSE:TCS to A and B');
    expect(addedMessage({ ...add, requested: 4, alreadyMembers: 1, newlyTracked: 2 }, ['A'], '')).toBe(
      'Added 4 instruments to A (1 already there; 2 were not tracked and now are)',
    );
    expect(removedMessage({ requested: 2, removed: 1, notMembers: 1, memberCount: 0 }, 'A', 'NSE:TCS')).toBe(
      'Removed NSE:TCS from A (1 were not in it)',
    );
  });
});

describe('errors', () => {
  const problem = (status: number, code: string, title: string, errors?: Record<string, string[]>) =>
    new ApiError(status, { title, code, status, errors });

  it('recognises a conflict and explains it', () => {
    const conflict = problem(409, 'baskets.conflict', 'x');
    expect(isConflict(conflict)).toBe(true);
    expect(isConflict(problem(409, 'baskets.name_taken', 'x'))).toBe(false);
    expect(basketProblemText(conflict, 'fallback')).toMatch(/reloaded — please try again/);
  });

  it('recognises a refused change of a portfolio basket and words it the same whatever the title', () => {
    const refused = problem(409, 'baskets.system', 'Conflict');
    expect(isSystemRefusal(refused)).toBe(true);
    expect(isSystemRefusal(problem(409, 'baskets.conflict', 'x'))).toBe(false);
    expect(isConflict(refused)).toBe(false);
    expect(basketProblemText(refused, 'f')).toBe("Portfolio baskets can't be changed");
    expect(reorderProblemText(refused, 'f')).toBe("Portfolio baskets can't be changed");
  });

  it('prefers what the API says about the instruments, then its title, then the fallback', () => {
    expect(
      basketProblemText(
        problem(400, 'instruments.retired', 'Retired.', { instrumentIds: ['Cannot add: OLDCO.'] }),
        'f',
      ),
    ).toBe('Cannot add: OLDCO.');
    expect(basketProblemText(problem(404, 'baskets.not_found', 'No such basket.'), 'f')).toBe('No such basket.');
    expect(basketProblemText(new ApiError(500), 'f')).toBe('f');
    expect(basketProblemText(new TypeError('network'), 'f')).toMatch(/Could not reach the server/);
  });

  it('sees through a partly applied add to the failure that stopped it', () => {
    const applied = { requested: 500, added: 500, alreadyMembers: 0, newlyTracked: 0, memberCount: 500 };
    const partial = new PartialAddError(applied, problem(409, 'baskets.conflict', 'x'));
    expect(isConflict(partial)).toBe(true);
    expect(basketProblemText(new PartialAddError(applied, problem(400, 'x', 'Too many.')), 'f')).toBe('Too many.');
    expect(basketProblemText(new PartialAddError(applied, new TypeError('net')), 'f')).toMatch(/Could not reach/);
  });

  it('treats a refused list on a reorder as a basket that changed', () => {
    const stale = problem(400, 'baskets.invalid', 'The list must hold every instrument…');
    expect(isStaleOrder(stale)).toBe(true);
    expect(isStaleOrder(problem(409, 'baskets.invalid', 'x'))).toBe(false);
    expect(isStaleOrder(problem(400, 'baskets.name_taken', 'x'))).toBe(false);
    expect(reorderProblemText(stale, 'f')).toBe('The basket changed; it has been reloaded.');
    expect(reorderProblemText(problem(409, 'baskets.conflict', 'x'), 'f')).toMatch(/reloaded — please try again/);
    expect(reorderProblemText(new ApiError(500), 'The order could not be saved.')).toBe(
      'The order could not be saved.',
    );
  });

  it('reports what stayed applied, leaving out baskets that got nothing', () => {
    expect(appliedText([{ basket: 'A', added: 0 }])).toBe('');
    expect(
      appliedText([
        { basket: 'A', added: 1_000 },
        { basket: 'B', added: 0 },
        { basket: 'C', added: 5 },
      ]),
    ).toBe('Before the error 1,000 added to A and 5 added to C; those stay.');
  });

  it('joins the messages of each field', () => {
    expect(basketFieldErrors(problem(400, 'baskets.invalid', 'x', { name: ['One.', 'Two.'] }))).toEqual({
      name: 'One. Two.',
    });
    expect(basketFieldErrors(new TypeError('x'))).toEqual({});
  });
});

describe('splitBaskets', () => {
  it('separates the portfolio baskets from the owner’s and keeps both orders', () => {
    const baskets = [
      buildPortfolioBasket({ id: 'p1' }),
      buildBasket({ id: 'm1' }),
      buildPortfolioBasket({ id: 'p2' }),
      buildBasket({ id: 'm2' }),
    ];
    const groups = splitBaskets(baskets);
    expect(groups.portfolio.map((b) => b.id)).toEqual(['p1', 'p2']);
    expect(groups.manual.map((b) => b.id)).toEqual(['m1', 'm2']);
    expect(manualBaskets(baskets).map((b) => b.id)).toEqual(['m1', 'm2']);
  });
});

describe('capProblem', () => {
  it('is null while the basket still has room, up to the limit exactly', () => {
    expect(capProblem('A', 1_990, 10)).toBeNull();
    expect(capProblem('A', 0, maxBasketMembers)).toBeNull();
  });

  it('says how many fewer to select, and whether the count is an upper bound', () => {
    expect(capProblem('A', 1_990, 15)).toBe(
      'A holds 1,990 and a basket holds at most 2,000 instruments, so 15 more do not fit. Select 5 fewer.',
    );
    expect(capProblem('A', 1_990, 15, true)).toContain('so up to 15 more do not fit');
  });
});

describe('basket form', () => {
  it('starts blank with the first palette colour, or from a basket', () => {
    expect(basketFormFrom()).toEqual({ name: '', colour: '#6366f1', purpose: 'trading', description: '' });
    expect(basketFormFrom(buildBasket({ name: 'X', purpose: 'long_term', description: 'd' }))).toMatchObject({
      name: 'X',
      purpose: 'long_term',
      description: 'd',
    });
  });

  it('trims, lowercases the colour and turns a blank description into none', () => {
    expect(toBasketInput({ name: ' A ', colour: ' #ABCDEF ', purpose: 'watch', description: '  ' })).toEqual({
      name: 'A',
      colour: '#abcdef',
      purpose: 'watch',
      description: null,
    });
  });

  it('finds what the server would refuse', () => {
    const ok = basketFormFrom(buildBasket());
    expect(validateBasket(ok)).toEqual({});
    expect(validateBasket({ ...ok, name: ' ' })).toEqual({ name: 'Enter a name.' });
    expect(validateBasket({ ...ok, name: 'x'.repeat(61) })).toEqual({ name: 'Use at most 60 characters.' });
    expect(validateBasket({ ...ok, colour: 'blue' })).toEqual({ colour: 'Use a hex colour such as #6366f1.' });
    expect(validateBasket({ ...ok, description: 'x'.repeat(501) })).toEqual({
      description: 'Use at most 500 characters.',
    });
  });
});
