/** A full page load of a server-rendered page (Razor), leaving the SPA. A module of its own so tests can observe it. */
export function loadFromServer(path: string): void {
  window.location.replace(path);
}
