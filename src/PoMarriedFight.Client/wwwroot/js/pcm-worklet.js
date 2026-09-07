// Microphone samples at whatever rate the device runs, resampled to 16 kHz mono Int16 frames — the only format the
// Live API takes in. Each frame carries its own RMS so a meter can animate without a second analyser node.
//
// FRAME_MS is the one knob. Shorter frames reach the model's voice-activity detector sooner, so the host takes its
// turn a little earlier; they also multiply the interop calls, hub messages and socket writes per second, which is
// what a small App Service plan runs out of first. Keep it in step with DebateOrchestrator.FrameMilliseconds.
const FRAME_MS = 100;
const TARGET_RATE = 16000;

class PcmCaptureProcessor extends AudioWorkletProcessor {
  constructor() {
    super();
    this.frameSamples = Math.round((TARGET_RATE * FRAME_MS) / 1000);
    this.ratio = sampleRate / TARGET_RATE;
    this.pending = new Float32Array(0);
    this.out = new Int16Array(this.frameSamples);
    this.outIndex = 0;
    this.phase = 0;
  }

  process(inputs) {
    const input = inputs[0];
    if (!input || input.length === 0) {
      return true;
    }

    const channel = input[0];
    const merged = new Float32Array(this.pending.length + channel.length);
    merged.set(this.pending, 0);
    merged.set(channel, this.pending.length);

    // Linear interpolation between the two nearest input samples: cheap, and at these rates inaudible.
    let pos = this.phase;
    let sumSq = 0;
    while (pos + 1 < merged.length) {
      const i = Math.floor(pos);
      const frac = pos - i;
      const sample = merged[i] * (1 - frac) + merged[i + 1] * frac;
      const clamped = Math.max(-1, Math.min(1, sample));
      this.out[this.outIndex++] = clamped < 0 ? clamped * 0x8000 : clamped * 0x7fff;
      sumSq += clamped * clamped;

      if (this.outIndex === this.frameSamples) {
        const rms = Math.sqrt(sumSq / this.frameSamples);
        const bytes = new Uint8Array(this.out.buffer.slice(0));
        this.port.postMessage({ pcm: bytes, rms }, [bytes.buffer]);
        this.outIndex = 0;
        sumSq = 0;
      }

      pos += this.ratio;
    }

    const consumed = Math.floor(pos);
    this.pending = merged.subarray(consumed);
    this.phase = pos - consumed;
    return true;
  }
}

registerProcessor('pcm-capture', PcmCaptureProcessor);
