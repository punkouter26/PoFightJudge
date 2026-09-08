// Handing a file to the browser.
//
// A plain <a href="/api/..." download> cannot be used for anything here: the recording lives in a private container
// and the route carries the caller's own credentials, which a link element would not send in Production. So the bytes
// are fetched by the app and handed over here instead.
//
// The object URL is revoked on the next frame rather than immediately: Safari has not started reading it by the time
// click() returns, and revoking too early gives a download of zero bytes with no error anywhere.
window.PoSave = {
  file(name, mime, base64) {
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }

    const url = URL.createObjectURL(new Blob([bytes], { type: mime }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);

    requestAnimationFrame(() => URL.revokeObjectURL(url));
    return true;
  },
};
