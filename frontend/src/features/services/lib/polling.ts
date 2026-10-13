/** While any service runs, the list (and the data job of a running end-of-day run) is read this often. */
export const servicesRunningPollMs = 2000;

/** While none runs, the list is read this often (and only while the page is visible): a scheduled start is noticed. */
export const servicesIdlePollMs = 60_000;
