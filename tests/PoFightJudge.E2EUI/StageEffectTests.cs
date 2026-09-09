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

    /// <summary>The canvas has to end up inside the element it was mounted on, behind everything the page put there.</summary>
    [SkippableFact]
    public async Task A_mounted_canvas_goes_into_the_page_and_comes_back_out()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop);
        await using var _ = context;

        Skip.IfNot(await page.EvaluateAsync<bool>("() => PoGfx.probe().webgl2"), "this browser has no WebGL2");

        var counts = await page.EvaluateAsync<int[]>(
            """
            () => {
              const host = document.createElement('div');
              host.id = 'gfx-probe';
              host.style.cssText = 'position:fixed;left:0;top:0;width:200px;height:120px;';
              document.body.appendChild(host);
              PoGfx.mount('#gfx-probe', 'stage');
              const mounted = host.querySelectorAll('canvas.po-gfx').length;
              PoGfx.unmount('#gfx-probe');
              const after = host.querySelectorAll('canvas.po-gfx').length;
              host.remove();
              return [mounted, after];
            }
            """);

        counts.Should().Equal([1, 0], "one canvas while it is mounted, none once it is taken down");
        errors.Should().BeEmpty();
    }

    /// <summary>
    /// Every sound is made out of oscillators at the moment it is asked for, so the only way to be sure a voice
    /// exists is to schedule it. A name with no voice is silence, and silence looks exactly like working.
    /// </summary>
    [SkippableFact]
    public async Task Every_sound_can_actually_be_scheduled()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop);
        await using var _ = context;

        var played = await page.EvaluateAsync<bool>(
            """
            () => {
              const names = ['bell', 'bell3', 'slap', 'gavel', 'gavel3', 'tick', 'whoosh',
                             'stinger-intro', 'stinger-probe', 'stinger-verdict', 'fanfare',
                             'clipping', 'cue', 'count'];
              PoSfx.arm();
              for (const name of names) { PoSfx.play(name, 0.5); }
              PoSfx.duck(0.3);
              return true;
            }
            """);

        played.Should().BeTrue();
        errors.Should().BeEmpty("a voice that throws while it is being scheduled would say so on the console");
    }

    /// <summary>
    /// The particles are real physics on a real canvas: they have to be spawned, drawn, and then gone. What is
    /// asserted is the end of that — the canvas takes itself away once the last one has died, because a page that
    /// keeps a frame loop running after the confetti has landed is the one way this becomes a battery complaint.
    /// </summary>
    [SkippableFact]
    public async Task Confetti_puts_a_canvas_up_and_takes_it_away_again()
    {
        var (context, page, errors) = await app.OpenAsync(AppFixture.Desktop);
        await using var _ = context;

        var thrown = await page.EvaluateAsync<bool>(
            """
            () => {
              const host = document.createElement('div');
              host.id = 'fx-probe';
              host.style.cssText = 'position:fixed;left:0;top:0;width:300px;height:200px;';
              document.body.appendChild(host);
              return PoParticles.burst('#fx-probe', 'confetti');
            }
            """);

        thrown.Should().BeTrue();
        (await page.Locator("#fx-probe canvas.po-particles").CountAsync()).Should().Be(1, "the confetti is falling");

        await page.EvaluateAsync("() => PoParticles.clear('#fx-probe')");
        (await page.Locator("#fx-probe canvas.po-particles").CountAsync()).Should().Be(0, "and it does not stay up afterwards");

        await page.EvaluateAsync("() => document.getElementById('fx-probe')?.remove()");
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
}
