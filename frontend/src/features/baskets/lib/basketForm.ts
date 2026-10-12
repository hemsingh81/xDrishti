import type { Basket, BasketInput } from '@/shared/api';
import { basketColors } from '@/theme';
import type { BasketPurpose } from './labels';

export const maxNameLength = 60;
export const maxDescriptionLength = 500;
const hexColour = /^#[0-9a-fA-F]{6}$/;

export interface BasketForm {
  name: string;
  colour: string;
  purpose: BasketPurpose;
  description: string;
}

const isPurpose = (value: string): value is BasketPurpose =>
  value === 'trading' || value === 'long_term' || value === 'watch';

/** A blank form (the first palette colour, trading) or the values of a basket. */
export function basketFormFrom(basket?: Basket): BasketForm {
  return {
    name: basket?.name ?? '',
    colour: basket?.colour ?? basketColors[0].value,
    purpose: basket && isPurpose(basket.purpose) ? basket.purpose : 'trading',
    description: basket?.description ?? '',
  };
}

export const toBasketInput = (form: BasketForm): BasketInput => ({
  name: form.name.trim(),
  colour: form.colour.trim().toLowerCase(),
  purpose: form.purpose,
  description: form.description.trim() === '' ? null : form.description.trim(),
});

/** Field → message for what the server would refuse anyway, so the owner hears it without a round trip. */
export function validateBasket(form: BasketForm): Record<string, string> {
  const errors: Record<string, string> = {};
  const name = form.name.trim();
  if (name === '') errors.name = 'Enter a name.';
  else if (name.length > maxNameLength) errors.name = `Use at most ${String(maxNameLength)} characters.`;
  if (!hexColour.test(form.colour.trim())) errors.colour = 'Use a hex colour such as #6366f1.';
  if (form.description.length > maxDescriptionLength) {
    errors.description = `Use at most ${String(maxDescriptionLength)} characters.`;
  }
  return errors;
}
