import type { components } from '@/shared/api/schema';

type Item = components['schemas']['SettingsCatalogItem'];

export const buildCatalogItem = (over: Partial<Item> & Pick<Item, 'key' | 'label'>): Item => ({
  tier: 'Essential',
  value: null,
  default: null,
  range: null,
  safer: 'none',
  learnable: false,
  source: 'stored',
  ...over,
});

/** The settings catalog as the API answers it: the five Essential rows, one Advanced and one Internal row. */
export const buildSettingsCatalog = (): { items: Item[] } => ({
  items: [
    buildCatalogItem({ key: 'auto.enabled', label: 'Auto', default: 'false' }),
    buildCatalogItem({ key: 'suggest.scope', label: 'What to suggest', default: 'eligible' }),
    buildCatalogItem({ key: 'risk.profile', label: 'Risk profile' }),
    buildCatalogItem({ key: 'chargesheet.gate', label: 'Charge sheet', default: 'false' }),
    buildCatalogItem({ key: 'notices.failure', label: 'Failure notices', default: 'true' }),
    buildCatalogItem({
      key: 'ml.auto_promote',
      label: 'Promote models automatically',
      tier: 'Advanced',
      default: 'false',
    }),
    buildCatalogItem({
      key: 'Planning:Grading:Capital',
      label: 'Capital for grading',
      tier: 'Internal',
      source: 'configuration',
    }),
  ],
});
