// Cropping a portrait before it is sent.
//
// The server decodes no images: it stores a PNG whose header says the right size and nothing else. So the picked file
// is decoded here, by the browser that is about to show it anyway, cover-cropped to a square and re-encoded as PNG.
// A file the browser cannot decode rejects, and the dialog says so.
window.PoFace = {
  async square(dataUrl, size) {
    const image = new Image();
    image.src = dataUrl;
    await image.decode();

    // An SVG with no intrinsic size decodes to nothing; treat it as not an image.
    const side = Math.min(image.naturalWidth, image.naturalHeight);
    if (!side) {
      throw new Error('The image has no size.');
    }

    const canvas = document.createElement('canvas');
    canvas.width = size;
    canvas.height = size;
    canvas.getContext('2d').drawImage(
      image, (image.naturalWidth - side) / 2, (image.naturalHeight - side) / 2, side, side, 0, 0, size, size);
    return canvas.toDataURL('image/png');
  },
};
