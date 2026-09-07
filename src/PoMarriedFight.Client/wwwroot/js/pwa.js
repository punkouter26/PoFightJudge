// Registers the install-only worker. It lives in its own file rather than inline in index.html because the app's
// own Content-Security-Policy is script-src 'self': an inline script is blocked, silently, in every environment
// that serves the real headers.
window.addEventListener('load', function () {
  if (!('serviceWorker' in navigator)) {
    // A page served insecurely has no service worker at all; not being installable is not worth showing anybody.
    return;
  }

  navigator.serviceWorker.register('sw.js').catch(function () {
    // Same again: a registration that fails must never keep the app from booting.
  });
});
