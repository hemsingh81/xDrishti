/** The reports the simulation screen offers (AU-15): the Overview panels that read a training job. */
export const trainingReportTypes = [
  { id: 'key-figures', label: 'Key figures' },
  { id: 'equity', label: 'Equity and drawdown' },
  { id: 'monthly', label: 'Monthly total R' },
  { id: 'weekday', label: 'Win rate by weekday' },
  { id: 'distribution', label: 'R distribution' },
] as const;
export type TrainingReportType = (typeof trainingReportTypes)[number]['id'];
