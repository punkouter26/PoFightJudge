// PoGfx — the drawn layer. One WebGL2 canvas per mounted element, a shared frame loop, and nothing that any page
// depends on: every entry point answers false rather than throwing, and a browser with no WebGL2 simply gets the
// CSS the markup already had. Mounted from .NET; drawn entirely here, because sixty frames a second is not a
// conversation to have across the interop boundary.
//
// Colours come from the design tokens, but they cannot be read as text: every token is one light-dark() pair, and
// getPropertyValue hands back the declaration rather than the colour this theme resolved it to. So a hidden probe
// element is given `color: var(--po-p1)` and its computed colour is read back — the browser does the resolving,
// and the same trick re-runs when the theme is switched underneath us.
//
// Reduced motion turns the whole thing off. It is the app's one motion switch and this is the most motion in it.
window.PoGfx = (function () {
  "use strict";

  /** Retina is not worth the fill rate for a soft glow; 1.5× is past the point anybody can see the difference. */
  const MAX_DPR = 1.5;

  const VERTEX = `#version 300 es
in vec2 a_position;
out vec2 v_uv;
void main() {
  v_uv = a_position * 0.5 + 0.5;
  gl_Position = vec4(a_position, 0.0, 1.0);
}`;

  // Value noise and a four-octave fbm, which is what keeps an aura from looking like a radial gradient with the
  // opacity turned down. Cheap enough that the whole thing runs on a phone's integrated GPU.
  const NOISE = `
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float noise(vec2 p) {
  vec2 i = floor(p);
  vec2 f = fract(p);
  vec2 u = f * f * (3.0 - 2.0 * f);
  return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x),
             mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}
float fbm(vec2 p) {
  float sum = 0.0;
  float amp = 0.5;
  for (int i = 0; i < 4; i++) { sum += amp * noise(p); p *= 2.03; amp *= 0.5; }
  return sum;
}`;

  // The watch stage: an aura under whoever is speaking, breathing with their voice, and the ring a slap sends out.
  const STAGE = `#version 300 es
precision highp float;
in vec2 v_uv;
out vec4 outColour;
uniform vec2 u_res;
uniform vec2 u_focus;
uniform float u_time;
uniform float u_level;
uniform float u_side;
uniform float u_shock;
uniform vec3 u_c1;
uniform vec3 u_c2;
${NOISE}
void main() {
  float aspect = u_res.x / max(u_res.y, 1.0);
  vec2 centre = vec2(0.5, 0.5);
  vec2 p = vec2((v_uv.x - u_focus.x) * aspect, v_uv.y - u_focus.y);
  float d = length(p);
  vec3 tint = mix(u_c1, u_c2, clamp(u_side * 0.5 + 0.5, 0.0, 1.0));

  float n = fbm(p * 3.0 + vec2(u_time * 0.13, u_time * 0.09));
  float reach = 0.16 + u_level * 0.5;
  float aura = smoothstep(reach + 0.28 * n, 0.0, d) * (0.2 + u_level * 0.8);
  vec3 colour = tint * aura;
  float alpha = aura * 0.6;

  if (u_shock >= 0.0) {
    float life = clamp(1.0 - u_shock / 0.7, 0.0, 1.0);
    vec2 q = vec2((v_uv.x - centre.x) * aspect, v_uv.y - centre.y);
    float r = length(q);
    float radius = u_shock * 1.7;
    // Three rings a hair apart: the channels pull away from each other at the edge of the shock.
    vec3 split = vec3(
      smoothstep(0.05, 0.0, abs(r - radius * 1.04)),
      smoothstep(0.05, 0.0, abs(r - radius)),
      smoothstep(0.05, 0.0, abs(r - radius * 0.96)));
    colour += split * life * 1.5;
    float flash = life * life * 0.4 * smoothstep(0.6, 0.0, r);
    colour += vec3(flash);
    alpha = max(alpha, max(max(split.r, split.g), max(split.b, flash)) * life);
  }

  outColour = vec4(colour, clamp(alpha, 0.0, 1.0));
}`;

  // The live fight's backdrop: a slow flowing field between the two fighters' colours, biased toward whoever has
  // the floor and heating up as the clock runs down. It sits behind the cards, so most of what shows is the gaps.
  const BACKDROP = `#version 300 es
precision highp float;
in vec2 v_uv;
out vec4 outColour;
uniform vec2 u_res;
uniform float u_time;
uniform float u_level;
uniform float u_mix;
uniform float u_heat;
uniform vec3 u_c1;
uniform vec3 u_c2;
uniform vec3 u_c3;
${NOISE}
void main() {
  float aspect = u_res.x / max(u_res.y, 1.0);
  vec2 p = vec2(v_uv.x * aspect, v_uv.y);
  float t = u_time * (0.05 + u_heat * 0.2);

  // Warping the domain before sampling it again is what turns four octaves of value noise into something that
  // looks like it is moving rather than scrolling.
  vec2 warp = vec2(fbm(p * 1.5 + t), fbm(p * 1.5 + 4.7 - t));
  float field = fbm(p * 2.1 + warp * 1.3 + vec2(0.0, t * 0.6));

  float pull = (clamp(u_mix, 0.0, 1.0) - 0.5) * 0.8;
  vec3 colour = mix(u_c1, u_c2, clamp(v_uv.x * 0.8 + field * 0.5 - 0.15 - pull, 0.0, 1.0));
  colour = mix(colour, u_c3, u_heat * 0.5 * (0.35 + 0.65 * field));

  float vignette = smoothstep(1.2, 0.2, length((v_uv - 0.5) * vec2(aspect, 1.0)));
  float alpha = (0.14 + 0.2 * field + u_heat * 0.13 + u_level * 0.08) * vignette;
  outColour = vec4(colour, clamp(alpha, 0.0, 1.0));
}`;

  // The verdict, finished like a piece of film: a vignette that pulls the eye in and grain over the top of it.
  // This one is mounted OVER the page rather than under it, which is the only way grain reads as grain.
  const GRAIN = `#version 300 es
precision highp float;
in vec2 v_uv;
out vec4 outColour;
uniform vec2 u_res;
uniform float u_time;
uniform vec3 u_c3;
${NOISE}
void main() {
  float aspect = u_res.x / max(u_res.y, 1.0);
  float vignette = smoothstep(0.45, 1.15, length((v_uv - 0.5) * vec2(aspect, 1.0)));

  // Sampled at pixel scale and moved every frame, because grain that holds still is a texture, not grain.
  float grain = hash(v_uv * u_res + fract(u_time) * 431.0) - 0.5;
  float alpha = vignette * 0.3 + abs(grain) * 0.05;
  vec3 colour = mix(vec3(0.0), u_c3 * 0.35, vignette * 0.25) + vec3(grain * 0.12);
  outColour = vec4(colour, clamp(alpha, 0.0, 0.42));
}`;

  const SHADERS = { "stage": STAGE, "backdrop": BACKDROP, "grain": GRAIN };

  const mounts = new Map();
  let frame = 0;
  let probeEl = null;
  let watchingTheme = false;

  function reducedMotion() {
    try {
      return window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    } catch (e) {
      return false;
    }
  }

  /** What this browser can actually do, for a page that wants to know before it asks for it. */
  function probe() {
    let webgl2 = false;
    try {
      webgl2 = !!document.createElement("canvas").getContext("webgl2");
    } catch (e) {
      webgl2 = false;
    }

    return {
      webgl2: webgl2,
      webgpu: !!(navigator && navigator.gpu),
      backdropFilter: supportsBackdrop(),
      reducedMotion: reducedMotion(),
    };
  }

  function supportsBackdrop() {
    try {
      return CSS.supports("backdrop-filter", "blur(2px)") || CSS.supports("-webkit-backdrop-filter", "blur(2px)");
    } catch (e) {
      return false;
    }
  }

  /**
   * A token's colour as this theme resolved it. The probe is given `color: var(--token)` and asked what colour it
   * ended up; light-dark() cannot be unpicked any other way from script.
   */
  function tokenColour(token, fallback) {
    try {
      if (!probeEl) {
        probeEl = document.createElement("span");
        probeEl.style.cssText = "position:absolute;left:-9999px;top:-9999px;width:0;height:0;";
        document.body.appendChild(probeEl);
      }

      probeEl.style.color = "var(" + token + ")";
      const computed = getComputedStyle(probeEl).color;
      const parts = computed.match(/[\d.]+/g);
      if (parts && parts.length >= 3) {
        return [parseFloat(parts[0]) / 255, parseFloat(parts[1]) / 255, parseFloat(parts[2]) / 255];
      }
    } catch (e) {
      // Fall through to the caller's colour.
    }

    return fallback;
  }

  function compile(gl, type, source) {
    const shader = gl.createShader(type);
    gl.shaderSource(shader, source);
    gl.compileShader(shader);
    if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) {
      gl.deleteShader(shader);
      return null;
    }

    return shader;
  }

  function program(gl, fragment) {
    const vs = compile(gl, gl.VERTEX_SHADER, VERTEX);
    const fs = compile(gl, gl.FRAGMENT_SHADER, fragment);
    if (!vs || !fs) {
      return null;
    }

    const prog = gl.createProgram();
    gl.attachShader(prog, vs);
    gl.attachShader(prog, fs);
    gl.linkProgram(prog);
    gl.deleteShader(vs);
    gl.deleteShader(fs);
    if (!gl.getProgramParameter(prog, gl.LINK_STATUS)) {
      gl.deleteProgram(prog);
      return null;
    }

    return prog;
  }

  function readColours(mount) {
    mount.c1 = tokenColour(mount.tokens[0], [0.25, 0.66, 0.96]);
    mount.c2 = tokenColour(mount.tokens[1], [1.0, 0.31, 0.55]);
    mount.c3 = tokenColour(mount.tokens[2], [1.0, 0.35, 0.35]);
  }

  // The theme toggle stamps data-theme on <html>; every mounted canvas re-reads its colours when it does.
  function watchTheme() {
    if (watchingTheme || typeof MutationObserver !== "function") {
      return;
    }

    watchingTheme = true;
    new MutationObserver(function () {
      mounts.forEach(readColours);
    }).observe(document.documentElement, { attributes: true, attributeFilter: ["data-theme"] });
  }

  function resize(mount) {
    const rect = mount.host.getBoundingClientRect();
    const dpr = Math.min(window.devicePixelRatio || 1, MAX_DPR);
    const width = Math.max(1, Math.round(rect.width * dpr));
    const height = Math.max(1, Math.round(rect.height * dpr));
    if (mount.canvas.width !== width || mount.canvas.height !== height) {
      mount.canvas.width = width;
      mount.canvas.height = height;
    }
  }

  function levelOf(mount) {
    try {
      if (mount.level === "watch" && window.PoAudio && typeof window.PoAudio.level === "function") {
        return window.PoAudio.level();
      }

      if (mount.level === "live" && window.PoLive && typeof window.PoLive.hostLevel === "function") {
        return window.PoLive.hostLevel();
      }
    } catch (e) {
      // A bus that will not answer reads as silence, which is what it sounds like.
    }

    return 0;
  }

  function draw(now) {
    frame = 0;
    if (mounts.size === 0) {
      return;
    }

    if (!document.hidden) {
      mounts.forEach(function (mount) {
        const gl = mount.gl;
        if (gl.isContextLost()) {
          return;
        }

        resize(mount);
        gl.viewport(0, 0, mount.canvas.width, mount.canvas.height);
        gl.useProgram(mount.program);
        gl.uniform2f(mount.u.res, mount.canvas.width, mount.canvas.height);
        gl.uniform1f(mount.u.time, (now - mount.start) / 1000);
        // Eased rather than taken raw: the level is sampled at whatever rate the bus updates it, and a glow that
        // steps between two values reads as a fault.
        mount.smoothed += (levelOf(mount) - mount.smoothed) * 0.18;
        gl.uniform1f(mount.u.level, mount.smoothed);
        gl.uniform1f(mount.u.side, mount.side);
        gl.uniform2f(mount.u.focus, mount.focus[0], mount.focus[1]);
        gl.uniform1f(mount.u.shock, mount.shockAt > 0 ? (now - mount.shockAt) / 1000 : -1);
        gl.uniform3f(mount.u.c1, mount.c1[0], mount.c1[1], mount.c1[2]);
        gl.uniform3f(mount.u.c2, mount.c2[0], mount.c2[1], mount.c2[2]);
        gl.uniform3f(mount.u.c3, mount.c3[0], mount.c3[1], mount.c3[2]);
        gl.uniform1f(mount.u.mix, mount.mix);
        gl.uniform1f(mount.u.heat, mount.heat);
        gl.clearColor(0, 0, 0, 0);
        gl.clear(gl.COLOR_BUFFER_BIT);
        gl.drawArrays(gl.TRIANGLES, 0, 6);

        if (mount.shockAt > 0 && now - mount.shockAt > 900) {
          mount.shockAt = 0;
        }
      });
    }

    frame = requestAnimationFrame(draw);
  }

  function pump() {
    if (!frame && mounts.size > 0) {
      frame = requestAnimationFrame(draw);
    }
  }

  function mount(selector, shader, options) {
    if (reducedMotion() || mounts.has(selector)) {
      return false;
    }

    const source = SHADERS[shader];
    const host = document.querySelector(selector);
    if (!source || !host) {
      return false;
    }

    const settings = options || {};
    let gl = null;
    const canvas = document.createElement("canvas");
    canvas.className = "po-gfx";
    canvas.setAttribute("aria-hidden", "true");
    // Over the page or under it. Under is the default — an aura belongs behind the faces — but grain has to be on
    // top of what it is graining, and it never takes a click either way.
    const layer = settings.over ? 3 : 0;
    canvas.style.cssText = "position:absolute;inset:0;width:100%;height:100%;display:block;pointer-events:none;z-index:" + layer + ";";
    try {
      gl = canvas.getContext("webgl2", { alpha: true, antialias: false, premultipliedAlpha: false, powerPreference: "low-power" });
    } catch (e) {
      gl = null;
    }

    if (!gl) {
      return false;
    }

    const prog = program(gl, source);
    if (!prog) {
      return false;
    }

    const buffer = gl.createBuffer();
    gl.bindBuffer(gl.ARRAY_BUFFER, buffer);
    gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1, -1, 1, -1, -1, 1, -1, 1, 1, -1, 1, 1]), gl.STATIC_DRAW);
    const position = gl.getAttribLocation(prog, "a_position");
    gl.enableVertexAttribArray(position);
    gl.vertexAttribPointer(position, 2, gl.FLOAT, false, 0, 0);
    gl.enable(gl.BLEND);
    gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);

    const entry = {
      host: host,
      canvas: canvas,
      gl: gl,
      program: prog,
      start: performance.now(),
      level: settings.level || null,
      tokens: settings.tokens || ["--po-p1", "--po-p2", "--po-danger"],
      side: 0,
      mix: 0.5,
      heat: 0,
      smoothed: 0,
      focus: [0.5, 0.5],
      shockAt: 0,
      c1: [0.25, 0.66, 0.96],
      c2: [1.0, 0.31, 0.55],
      c3: [1.0, 0.35, 0.35],
      u: {
        res: gl.getUniformLocation(prog, "u_res"),
        time: gl.getUniformLocation(prog, "u_time"),
        level: gl.getUniformLocation(prog, "u_level"),
        side: gl.getUniformLocation(prog, "u_side"),
        focus: gl.getUniformLocation(prog, "u_focus"),
        shock: gl.getUniformLocation(prog, "u_shock"),
        c1: gl.getUniformLocation(prog, "u_c1"),
        c2: gl.getUniformLocation(prog, "u_c2"),
        c3: gl.getUniformLocation(prog, "u_c3"),
        mix: gl.getUniformLocation(prog, "u_mix"),
        heat: gl.getUniformLocation(prog, "u_heat"),
      },
    };

    readColours(entry);
    host.appendChild(canvas);
    mounts.set(selector, entry);
    watchTheme();
    pump();
    return true;
  }

  function unmount(selector) {
    const entry = mounts.get(selector);
    if (!entry) {
      return;
    }

    mounts.delete(selector);
    if (entry.canvas.parentNode) {
      entry.canvas.parentNode.removeChild(entry.canvas);
    }

    try {
      const lose = entry.gl.getExtension("WEBGL_lose_context");
      if (lose) {
        lose.loseContext();
      }
    } catch (e) {
      // The context is going away with the page anyway.
    }

    if (mounts.size === 0 && frame) {
      cancelAnimationFrame(frame);
      frame = 0;
    }
  }

  return {
    probe: probe,

    /**
     * A design token as a CSS colour string, resolved by this theme. Exposed because PoParticles needs the same
     * answer and there is no sense in two probes: light-dark() can only be unpicked by asking the browser.
     */
    colour: function (token) {
      const rgb = tokenColour(token, null);
      return rgb ? "rgb(" + Math.round(rgb[0] * 255) + "," + Math.round(rgb[1] * 255) + "," + Math.round(rgb[2] * 255) + ")" : null;
    },

    /** Mounts a shader on the first element matching `selector`. False when it could not be done, for any reason. */
    mount: function (selector, shader, options) {
      try {
        return mount(selector, shader, options);
      } catch (e) {
        return false;
      }
    },

    unmount: function (selector) {
      try {
        unmount(selector);
      } catch (e) {
        // Nothing left to take down.
      }
    },

    /** One numeric uniform a page drives: "side", "mix" or "heat". Anything else is ignored. */
    set: function (selector, name, value) {
      const entry = mounts.get(selector);
      if (entry && typeof value === "number" && (name === "side" || name === "mix" || name === "heat")) {
        entry[name] = value;
      }
    },

    /**
     * Points the aura at a child element — measured here, once, when the speaker changes, rather than every frame:
     * a layout read per frame is the one thing that would make this expensive.
     */
    focus: function (selector, childSelector) {
      const entry = mounts.get(selector);
      if (!entry) {
        return;
      }

      const child = childSelector ? entry.host.querySelector(childSelector) : null;
      if (!child) {
        entry.focus = [0.5, 0.5];
        return;
      }

      const host = entry.host.getBoundingClientRect();
      const rect = child.getBoundingClientRect();
      if (host.width <= 0 || host.height <= 0) {
        return;
      }

      // The canvas has y running up from the bottom; the page has it running down from the top.
      entry.focus = [
        (rect.left + rect.width / 2 - host.left) / host.width,
        1 - (rect.top + rect.height / 2 - host.top) / host.height,
      ];
    },

    /** Sends a ring out from the middle. One at a time: a second slap restarts the first. */
    shock: function (selector) {
      const entry = mounts.get(selector);
      if (entry) {
        entry.shockAt = performance.now();
        pump();
      }
    },
  };
})();
