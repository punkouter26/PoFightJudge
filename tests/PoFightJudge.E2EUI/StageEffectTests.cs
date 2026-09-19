using Microsoft.Playwright;

namespace PoFightJudge.E2EUI;

/// <summary>
/// The drawn and sounded layers, in a browser that actually has both. Nothing here looks at a picture: what these
/// prove is that the shaders compile and link, that a mounted canvas is really in the page, and that a browser
/// which cannot do any of it says so rather than throwing — every other tier can only assert the decision to ask.
/// </summary>
[Collection(AppCollection.Name)]
public class StageEffectTests(AppFixture app)
{
    /// <summary>
    /// A shader that will not compile mounts nothing, silently, and there is no other way to find out: the page
    /// keeps working, the canvas simply never appears. Software rendering is enough — this is about the GLSL.
    /// </summary>
    [SkippableFact]
    public async Task Every_shader_compiles_and_mounts_in_a_real_browser()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop);
        await using var _ = context;

        var probe = await page.EvaluateAsync<bool>("() => PoGfx.probe().webgl2");
        Skip.IfNot(probe, "this browser has no WebGL2 (headless software rendering is off)");

        var mounted = await page.EvaluateAsync<string[]>(
            """
            () => {
              const host = document.createElement('div');
              host.id = 'gfx-probe';
              host.style.cssText = 'position:fixed;left:0;top:0;width:200px;height:120px;';
              document.body.appendChild(host);
              const drawn = [];
              for (const shader of ['stage', 'backdrop', 'grain']) {
                if (PoGfx.mount('#gfx-probe', shader)) { drawn.push(shader); }
                PoGfx.unmount('#gfx-probe');
              }
              host.remove();
              return drawn;
            }
            """);

        mounted.Should().Equal(["stage", "backdrop", "grain"], "every shader must compile and link, or it draws nothing at all");
        errors.Should().BeEmpty();
    }

    /// <summary>Somebody who asked the whole system for less motion is not given the loudest thing in it.</summary>
    [SkippableFact]
    public async Task Reduced_motion_mounts_nothing()
    {
        Skip.If(app.Browser is null, app.Unavailable);
        var context = await app.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = AppFixture.Desktop,
            ReducedMotion = ReducedMotion.Reduce,
        });
        await using var _ = context;

        var page = await context.NewPageAsync();
        await page.GotoAsync(app.BaseUrl + "/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForFunctionAsync("() => !!window.PoGfx", null, new() { Timeout = 30_000 });

        var mounted = await page.EvaluateAsync<bool>(
            """
            () => {
              const host = document.createElement('div');
              host.id = 'gfx-probe';
              host.style.cssText = 'position:fixed;left:0;top:0;width:200px;height:120px;';
              document.body.appendChild(host);
              const drawn = PoGfx.mount('#gfx-probe', 'stage');
              const thrown = PoParticles.burst('#gfx-probe', 'confetti');
              PoGfx.unmount('#gfx-probe');
              PoParticles.clear('#gfx-probe');
              host.remove();
              return drawn || thrown;
            }
            """);

        mounted.Should().BeFalse("reduced motion is the app's one switch, and this is the most motion in it");
    }

    /// <summary>
    /// T110 — the level-clip observer is wired to the meter, asks the particle layer for sparks on the rising
    /// edge, and stays polite under reduced motion. Probing the observer directly is the only honest way to
    /// prove the bridge: a real microphone peak is not reproducible from Playwright.
    /// </summary>
    [SkippableFact]
    public async Task Level_clip_observer_loads_and_dispatches_to_particles()
    {
        Skip.If(app.Browser is null, app.Unavailable);
        var (context, page, _) = await app.OpenAsync(AppFixture.Desktop);
        await using var _ = context;

        // PoLevelClip is a global the page must load before we can probe it.
        await page.WaitForFunctionAsync("() => !!window.PoLevelClip", null, new() { Timeout = 30_000 });

        // Stub the particle entry point so we can record what the observer asks for, and count canvas mounts.
        var thrown = await page.EvaluateAsync<string[]>(
            """
            () => new Promise((resolve) => {
              const seen = [];
              const original = window.PoParticles.burst;
              window.PoParticles.burst = (selector, kind, at) => {
                seen.push(`${selector}|${kind}|${at ?? ''}`);
                return original ? original(selector, kind, at) : false;
              };
              document.documentElement.style.setProperty('--po-mic-level', '0');
              const host = document.createElement('div');
              host.className = 'meter';
              host.innerHTML = '<span class="lamp"></span>';
              document.body.appendChild(host);
              window.PoLevelClip.watch();
              const fire = () => {
                document.documentElement.style.setProperty('--po-mic-level', '0.95');
                setTimeout(() => {
                  window.PoLevelClip.stop();
                  window.PoParticles.burst = original;
                  host.remove();
                  resolve(seen);
                }, 600);
              };
              requestAnimationFrame(fire);
            })
            """);

        thrown.Should().NotBeEmpty("a level that crosses the threshold must hand off to PoParticles.burst");
        thrown[0].Should().Be(".meter|sparks|.lamp",
            "the observer must target the meter and throw from the lamp; anything else is a different effect");
    }

    /// <summary>Reduced motion also turns the level-clip observer off — it is the same one switch.</summary>
    [SkippableFact]
    public async Task Level_clip_observer_is_quiet_under_reduced_motion()
    {
        Skip.If(app.Browser is null, app.Unavailable);
        var context = await app.Browser!.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = AppFixture.Desktop,
            ReducedMotion = ReducedMotion.Reduce,
        });
        await using var _ = context;

        var page = await context.NewPageAsync();
        await page.GotoAsync(app.BaseUrl + "/login", new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle, Timeout = 60_000 });
        await page.WaitForFunctionAsync("() => !!window.PoLevelClip", null, new() { Timeout = 30_000 });

        var threw = await page.EvaluateAsync<int>(
            """
            () => {
              let count = 0;
              const original = window.PoParticles.burst;
              window.PoParticles.burst = () => { count++; return true; };
              document.documentElement.style.setProperty('--po-mic-level', '0.95');
              window.PoLevelClip.watch();
              return new Promise((resolve) => setTimeout(() => {
                window.PoParticles.burst = original;
                window.PoLevelClip.stop();
                document.documentElement.style.removeProperty('--po-mic-level');
                resolve(count);
              }, 800));
            }
            """);

        threw.Should().Be(0, "reduced motion is the app's one switch — the observer must no-op rather than throw");
    }
}
