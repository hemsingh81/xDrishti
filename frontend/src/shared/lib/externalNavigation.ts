/**
 * Leaving the app for another site in the same tab (e.g. a broker's sign-in page). An object rather than a bare
 * function so that tests can replace `assign` and check the URL without navigating.
 */
export const externalNavigation = {
  assign(url: string): void {
    window.location.assign(url);
  },
};
