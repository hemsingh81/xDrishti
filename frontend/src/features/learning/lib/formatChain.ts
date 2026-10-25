const signed = new Intl.NumberFormat('en-IN', {
  signDisplay: 'exceptZero',
  minimumFractionDigits: 2,
  maximumFractionDigits: 4,
});

/** A step of the chain in R with up to four decimals, so small costs are not rounded away and the steps visibly add up. */
export const formatStepR = (value: number): string => `${signed.format(value)} R`;
