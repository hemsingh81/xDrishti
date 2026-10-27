/** The words of the drafting panel (P9-07). A quick test is exploration, not a verdict; a valid text is not a good strategy. */
export const draftWords = {
  title: 'Draft a strategy',
  hint: 'Describe a trading idea in plain English. The model writes a candidate text that the Lab validator checks. Nothing is stored and nothing is run until you read it and confirm.',
  drafting: 'Drafting… this can take a few minutes. You can leave this screen; the draft is kept.',
  valid: 'Valid: the strategy language accepts this text. That does not make it a good strategy; the Lab tests do.',
  invalid: 'Not valid, not confirmable. Edit the text and press Validate; Confirm re-validates what you send.',
  declined: 'The idea cannot be written in the language today.',
  failed: 'The draft could not be made.',
  confirmed: 'Confirmed: stored as a library version.',
  discarded: 'Discarded. Nothing was stored.',
  quickTest: 'A quick test is exploration, not a verdict.',
  switchOff: 'Confirming drafts is switched off. Turn on "Allow confirming strategy drafts" in the assistant settings.',
} as const;

export const failReasons: Record<string, string> = {
  model_unavailable: 'The model server is not running or not ready.',
  timeout: 'The model took too long.',
  answer_shape: 'The model did not answer in the required form.',
};
