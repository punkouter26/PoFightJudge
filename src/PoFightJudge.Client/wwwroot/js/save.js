// Handing a file to the browser.
//
// A plain <a href="/api/..." download> cannot be used for anything here: the recording lives in a private container
// and the route carries the caller's own credentials, which a link element would not send in Production. So the bytes
// are fetched by the app and handed over here instead.
//
// The object URL is revoked on the next frame rather than immediately: Safari has not started reading it by the time
// click() returns, and revoking too early gives a download of zero bytes with no error anywhere.
(function () {
  "use strict";

  function download(blob, name) {
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = name;
    anchor.rel = 'noopener';
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);

    requestAnimationFrame(() => URL.revokeObjectURL(url));
  }

  // ── The story picture ──
  // Always the app's night palette, whichever theme the page is in: the picture goes somewhere else, and it should
  // look like the show there. Fixed colours here for that reason — they are the dark half of the tokens in app.css.
  const W = 1080;
  const H = 1920;
  const MARGIN = 90;
  const INK = {
    night: '#0b0d12', fg: '#eef0f4', muted: '#98a1b3', p1: '#3fa9f5', p2: '#ff4f8b',
    accent: '#ffcc33', warning: '#facc15', surface: '#151923',
  };
  const DISPLAY = '"Barlow Condensed", "Arial Narrow", sans-serif';
  const BODY = '"Barlow", "Segoe UI", sans-serif';

  /** Greedy word wrap: as many words to a line as fit. A word too long for any line gets one to itself. */
  function wrap(ctx, text, width) {
    const lines = [];
    let line = '';
    for (const word of String(text).split(/\s+/).filter(Boolean)) {
      const next = line ? line + ' ' + word : word;
      if (line && ctx.measureText(next).width > width) {
        lines.push(line);
        line = word;
      } else {
        line = next;
      }
    }

    if (line) {
      lines.push(line);
    }

    return lines;
  }

  /** Wrapped text, at most `most` lines, the last one ending in an ellipsis if there was more. Returns where it ended. */
  function paragraph(ctx, text, y, lineHeight, most) {
    const lines = wrap(ctx, text, W - MARGIN * 2);
    lines.slice(0, most).forEach(function (line, i) {
      const last = i === most - 1 && lines.length > most;
      ctx.fillText(last ? line.replace(/\W*\S*$/, '') + '…' : line, MARGIN, y + i * lineHeight);
    });
    return y + Math.min(lines.length, most) * lineHeight;
  }

  function glow(ctx, x, y, radius, colour, strength) {
    const light = ctx.createRadialGradient(x, y, 0, x, y, radius);
    light.addColorStop(0, colour);
    light.addColorStop(1, 'transparent');
    ctx.globalAlpha = strength;
    ctx.fillStyle = light;
    ctx.fillRect(0, 0, W, H);
    ctx.globalAlpha = 1;
  }

  /** The biggest size at which `text` fits in two lines, from `start` down to `least`. */
  function fit(ctx, text, start, least) {
    let size = start;
    for (; size > least; size -= 6) {
      ctx.font = `700 ${size}px ${DISPLAY}`;
      if (wrap(ctx, text, W - MARGIN * 2).length <= 2) {
        break;
      }
    }

    ctx.font = `700 ${size}px ${DISPLAY}`;
    return size;
  }

  async function draw(card) {
    try {
      await Promise.all([document.fonts.load(`700 120px ${DISPLAY}`), document.fonts.load(`500 40px ${BODY}`)]);
    } catch (e) {
      // The fallbacks in the stacks will do.
    }

    const canvas = document.createElement('canvas');
    canvas.width = W;
    canvas.height = H;
    const ctx = canvas.getContext('2d');
    ctx.textBaseline = 'alphabetic';

    // The ground: the night, the two fighters' colours glowing in from opposite corners, gold behind the name, and
    // the broadcast's diagonal stripes, barely there.
    ctx.fillStyle = INK.night;
    ctx.fillRect(0, 0, W, H);
    glow(ctx, 0, 0, 950, INK.p1, 0.32);
    glow(ctx, W, H, 1050, INK.p2, 0.28);
    glow(ctx, W / 2, 820, 650, INK.accent, 0.16);
    ctx.save();
    ctx.globalAlpha = 0.035;
    ctx.strokeStyle = INK.fg;
    ctx.lineWidth = 2;
    for (let x = -H; x < W; x += 36) {
      ctx.beginPath();
      ctx.moveTo(x, 0);
      ctx.lineTo(x + H, H);
      ctx.stroke();
    }
    ctx.restore();

    // The wordmark, and a rehearsal says so.
    let y = 160;
    ctx.font = `700 46px ${DISPLAY}`;
    ctx.fillStyle = INK.fg;
    ctx.fillText('POFIGHT', MARGIN, y);
    ctx.fillStyle = INK.accent;
    ctx.fillText('JUDGE', MARGIN + ctx.measureText('POFIGHT ').width, y);
    if (card.rehearsal) {
      ctx.font = `700 30px ${DISPLAY}`;
      const label = 'REHEARSAL';
      const width = ctx.measureText(label).width + 40;
      ctx.fillStyle = INK.surface;
      ctx.fillRect(W - MARGIN - width, y - 40, width, 54);
      ctx.fillStyle = INK.muted;
      ctx.fillText(label, W - MARGIN - width + 20, y - 3);
    }

    y += 140;
    ctx.font = `600 34px ${BODY}`;
    ctx.fillStyle = INK.accent;
    ctx.letterSpacing = '8px';
    ctx.fillText('THE RULING', MARGIN, y);
    ctx.letterSpacing = '0px';

    y += 90;
    ctx.font = `700 78px ${DISPLAY}`;
    ctx.fillStyle = INK.fg;
    y = paragraph(ctx, (card.topic || 'The argument').toUpperCase(), y, 82, 3);

    // The name, as big as two lines allow, in gold with a glow behind it.
    y += 150;
    const size = fit(ctx, card.headline.toUpperCase(), 230, 120);
    ctx.save();
    ctx.shadowColor = INK.accent;
    ctx.shadowBlur = 40;
    ctx.fillStyle = INK.accent;
    wrap(ctx, card.headline.toUpperCase(), W - MARGIN * 2).forEach(function (line, i) {
      ctx.fillText(line, MARGIN, y + i * size * 0.92);
    });
    ctx.restore();
    y += size * 0.92 * Math.max(1, wrap(ctx, card.headline.toUpperCase(), W - MARGIN * 2).length - 1) + 120;

    // The room: a rope in the two colours, split where it was split, the tags and their shares at its ends.
    const share = card.oneShare + card.twoShare > 0 ? card.oneShare / (card.oneShare + card.twoShare) : 0.5;
    ctx.font = `700 68px ${DISPLAY}`;
    ctx.fillStyle = INK.p1;
    ctx.fillText(card.one, MARGIN, y);
    ctx.fillStyle = INK.p2;
    ctx.fillText(card.two, W - MARGIN - ctx.measureText(card.two).width, y);
    y += 40;
    const rope = W - MARGIN * 2;
    ctx.fillStyle = INK.p2;
    ctx.fillRect(MARGIN, y, rope, 22);
    ctx.fillStyle = INK.p1;
    ctx.fillRect(MARGIN, y, rope * share, 22);
    ctx.fillStyle = INK.accent;
    ctx.beginPath();
    ctx.arc(MARGIN + rope * share, y + 11, 22, 0, Math.PI * 2);
    ctx.fill();
    y += 80;
    ctx.font = `600 32px ${BODY}`;
    ctx.fillStyle = INK.muted;
    const left = `${Math.round(share * 100)}% OF THE ROOM`;
    const right = `${100 - Math.round(share * 100)}%`;
    ctx.fillText(left, MARGIN, y);
    ctx.fillText(right, W - MARGIN - ctx.measureText(right).width, y);

    // What was said about it.
    y += 120;
    ctx.font = `500 46px ${BODY}`;
    ctx.fillStyle = INK.fg;
    y = paragraph(ctx, `“${card.summary}”`, y, 64, card.fallacy ? 4 : 7);

    // The worst slip, stamped.
    if (card.fallacy) {
      y += 90;
      ctx.save();
      ctx.translate(MARGIN + 110, y - 22);
      ctx.rotate(-0.07);
      ctx.fillStyle = INK.warning;
      ctx.fillRect(-110, -38, 220, 70);
      ctx.font = `700 44px ${DISPLAY}`;
      ctx.fillStyle = INK.night;
      ctx.fillText('FALLACY', -ctx.measureText('FALLACY').width / 2, 15);
      ctx.restore();
      ctx.font = `700 56px ${DISPLAY}`;
      ctx.fillStyle = INK.fg;
      ctx.fillText(String(card.fallacy).toUpperCase(), MARGIN + 260, y);
      if (card.fallacyQuote) {
        y += 80;
        ctx.font = `500 40px ${BODY}`;
        ctx.fillStyle = INK.muted;
        paragraph(ctx, `“${card.fallacyQuote}”`, y, 54, 3);
      }
    }

    ctx.font = `600 30px ${BODY}`;
    ctx.fillStyle = INK.muted;
    ctx.letterSpacing = '4px';
    ctx.fillText(`${String(card.when).toUpperCase()}  ·  JUDGED BY POFIGHTJUDGE`, MARGIN, H - 110);
    ctx.letterSpacing = '0px';
    return canvas;
  }

  window.PoSave = {
    file(name, mime, base64) {
      const binary = atob(base64);
      const bytes = new Uint8Array(binary.length);
      for (let i = 0; i < binary.length; i++) {
        bytes[i] = binary.charCodeAt(i);
      }

      download(new Blob([bytes], { type: mime }), name);
      return true;
    },

    /**
     * Draws the ruling as a 1080 by 1920 picture and hands it to the share sheet, or saves it where there is none.
     * Answers 'shared', 'saved', 'cancelled' or 'failed'. `only === 'data'` returns the picture as a data URL instead,
     * for the screenshot runner and nothing else.
     */
    async story(card, only) {
      try {
        const canvas = await draw(card);
        if (only === 'data') {
          return canvas.toDataURL('image/png');
        }

        const blob = await new Promise((resolve) => canvas.toBlob(resolve, 'image/png'));
        const name = card.fileName || 'ruling.png';
        const file = new File([blob], name, { type: 'image/png' });
        if (navigator.canShare && navigator.canShare({ files: [file] })) {
          try {
            await navigator.share({ files: [file], title: card.headline });
            return 'shared';
          } catch (e) {
            if (e && e.name === 'AbortError') {
              return 'cancelled';
            }
          }
        }

        download(blob, name);
        return 'saved';
      } catch (e) {
        return 'failed';
      }
    },
  };
})();
