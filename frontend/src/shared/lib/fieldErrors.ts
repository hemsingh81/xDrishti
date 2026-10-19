/** Server field errors for fields the form has no input for, as one sentence for an alert; null when there are none. */
export function unshownFieldErrorsText(
  serverErrors: Record<string, string>,
  shownFields: readonly string[],
): string | null {
  const text = Object.entries(serverErrors)
    .filter(([field]) => !shownFields.includes(field))
    .map(([field, message]) => `${field}: ${message}`)
    .join(' ');
  return text === '' ? null : text;
}
