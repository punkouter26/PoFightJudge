// Stamps the stored theme on <html> before Blazor boots so there is no flash of the wrong palette.
// Dark is the default (no attribute needed); only an explicit "light" choice is stamped. Kept in a file for the CSP.
// Blazored.LocalStorage writes the value with SetItemAsStringAsync, so it is the bare word, not a JSON string —
// the quote-strip below tolerates either.
(function () {
  try {
    var t = localStorage.getItem('po-theme');
    if (t) t = t.replace(/^"|"$/g, '');
    if (t === 'light' || t === 'dark') document.documentElement.setAttribute('data-theme', t);
  } catch (e) { /* storage blocked: keep the default */ }
})();
