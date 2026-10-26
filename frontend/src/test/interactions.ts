import userEvent from '@testing-library/user-event';

type Setup = ReturnType<typeof userEvent.setup>;

/** The keys a `keyboard()` string presses: `{Escape}` is one, `ab` is two. */
export function keyTokens(keys: string): number {
  return keys.replace(/\{[^}]*\}/g, 'k').length;
}

export interface CountingUser {
  user: Setup;
  /** The interactions made so far, by the counting rule of spec P8-17. */
  count: () => number;
}

/**
 * A user-event whose calls are counted (spec P8-17), written once and reused by every interaction-budget test: `click`, `dblClick`,
 * `selectOptions`, `type` (one per call, however many characters) and `upload` are 1 each; `keyboard('...')` is 1 per key token; `tab()` is 1 per
 * call. `hover` is not counted (a menu that opens on hover is opened by the test with a click). Opening a menu and choosing an item are two.
 * Navigation by the router's initial entries is 0 (the cold start); a click on the sidebar entry is 1.
 */
export function countingUser(): CountingUser {
  const user = userEvent.setup();
  let total = 0;
  const counted =
    <A extends unknown[], R>(fn: (...args: A) => R, by: (...args: NoInfer<A>) => number) =>
    (...args: A): R => {
      total += by(...args);
      return fn(...args);
    };
  const wrapped: Setup = {
    ...user,
    click: counted(user.click.bind(user), () => 1),
    dblClick: counted(user.dblClick.bind(user), () => 1),
    selectOptions: counted(user.selectOptions.bind(user), () => 1),
    type: counted(user.type.bind(user), () => 1),
    upload: counted(user.upload.bind(user), () => 1),
    tripleClick: counted(user.tripleClick.bind(user), () => 1),
    pointer: counted(user.pointer.bind(user), () => 1),
    paste: counted(user.paste.bind(user), () => 1),
    clear: counted(user.clear.bind(user), () => 1),
    keyboard: counted(user.keyboard.bind(user), (keys: string) => keyTokens(keys)),
    tab: counted(user.tab.bind(user), () => 1),
  };
  return { user: wrapped, count: () => total };
}
