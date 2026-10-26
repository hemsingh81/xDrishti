// The test handlers use relative paths ("/api/..."), and the app's API client reads `location.origin` when it is
// imported; Node has no `location`. Import this module first, before anything from src/.
Object.defineProperty(globalThis, 'location', { value: new URL('http://localhost/'), configurable: true });
