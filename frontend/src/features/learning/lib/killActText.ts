import type { KillAct } from '../api/killActsApi';

/** The act the owner asked for, before the confirmation (the password and reason are asked in the dialog). */
export type KillTarget =
  | { kind: 'dismiss'; id: number; rule: string }
  | { kind: 'apply'; id: number; rule: string; cells: string[] }
  | { kind: 'reenable'; key: string }
  | { kind: 'auto'; auto: boolean };

export const askedPassword = (t: KillTarget): boolean => t.kind !== 'apply';

export const actTitle = (t: KillTarget): string => {
  switch (t.kind) {
    case 'dismiss':
      return `Dismiss proposal ${String(t.id)}`;
    case 'apply':
      return `Apply proposal ${String(t.id)}`;
    case 'reenable':
      return 'Re-enable this cell';
    case 'auto':
      return `Turn automatic kill ${t.auto ? 'ON' : 'OFF'}`;
  }
};

export const actConfirm = (t: KillTarget): string => {
  switch (t.kind) {
    case 'dismiss':
      return 'Dismiss';
    case 'apply':
      return 'Apply';
    case 'reenable':
      return 'Re-enable';
    case 'auto':
      return t.auto ? 'Turn ON' : 'Turn OFF';
  }
};

/** What the act does and what it does not do, shown before the owner confirms. */
export const actMeaning = (t: KillTarget): string[] => {
  switch (t.kind) {
    case 'dismiss':
      return [
        'This records you and your reason against the proposal, once. It changes no cell and no lifecycle state, and a dismissed proposal can never be applied.',
        'It places no order. Your password is asked again for this act.',
      ];
    case 'apply':
      return [
        `This pauses the cells the proposal names (${t.cells.join(', ') || 'none'}) and records you as the decider. A cell already paused or not in Paper or Active is skipped.`,
        'It places no order and needs no password; it cannot be undone except by re-enabling each cell.',
      ];
    case 'reenable':
      return [
        `This moves the paused cell ${t.key} back to Paper, with your reason. It does not make it Active and places no order. Your password is asked again for this act.`,
      ];
    case 'auto':
      return t.auto
        ? [
            'This is a serious owner act. With automatic kill ON the daily decay monitor pauses a strategy\'s cells by itself when a kill rule fires, with no approval from you. The decision is recorded as "auto", never as your approval.',
            'The CF-09 failure boundary is never executed automatically. No order is placed. Your password is asked again for this change.',
          ]
        : [
            'This returns to manual approval: a firing rule only writes a proposal and nothing is paused until you apply it. Your password is asked again for this change.',
          ];
  }
};

export const toAct = (t: KillTarget, v: { password: string; reason: string }): KillAct => {
  switch (t.kind) {
    case 'dismiss':
      return { kind: 'dismiss', id: t.id, ...v };
    case 'apply':
      return { kind: 'apply', id: t.id };
    case 'reenable':
      return { kind: 'reenable', key: t.key, ...v };
    case 'auto':
      return { kind: 'auto', auto: t.auto, ...v };
  }
};

/** The longest reason each act takes (the server's limits; the automatic setting takes 500). */
export const reasonLimit = (t: KillTarget): number => (t.kind === 'dismiss' ? 400 : t.kind === 'reenable' ? 300 : 500);
