// Install-only. This worker exists so the app can be installed to a home screen and run in its own window;
// it deliberately caches nothing.
//
// Caching would be actively wrong here. Everything this app shows is either a live conversation, a recording being
// read, or a record computed on the server from rows that change — a cached answer is a wrong answer, and a cached
// build is a client talking to an API it no longer matches. There is no offline story to tell: a fight needs the
// microphone, the host and the network, and a verdict is worth nothing without them.
//
// So there is no fetch handler at all. Every request goes to the network exactly as it would with no worker
// installed, and in particular nothing under /api is ever intercepted.

self.addEventListener('install', () => {
  // No pre-cache to build, so take over as soon as the browser will allow it.
  self.skipWaiting();
});

self.addEventListener('activate', (event) => {
  // Clear anything an earlier version of this worker may have stored, then control the open pages.
  event.waitUntil(
    caches
      .keys()
      .then((keys) => Promise.all(keys.map((key) => caches.delete(key))))
      .then(() => self.clients.claim()),
  );
});
