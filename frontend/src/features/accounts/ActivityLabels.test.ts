import { humanizeEvent } from './lib/labels';

describe('humanizeEvent: the activity feed labels', () => {
  it.each([
    ['AccountCreated', 'Account created'],
    ['CorporateActionAdded', 'Corporate action added'],
    ['CorporateActionConfirmed', 'Corporate action confirmed'],
    ['CorporateActionEdited', 'Corporate action edited'],
    ['CorporateActionIgnored', 'Corporate action ignored'],
    ['CorporateActionsImported', 'Corporate actions imported'],
    ['CalendarDayAdded', 'Calendar day added'],
    ['CalendarDayConfirmed', 'Calendar day confirmed'],
    ['CalendarDayRemoved', 'Calendar day removed'],
    ['CalendarRefreshedFromNse', 'Calendar refreshed from NSE'],
  ])('%s reads %s', (event, label) => {
    expect(humanizeEvent(event)).toBe(label);
  });
});
