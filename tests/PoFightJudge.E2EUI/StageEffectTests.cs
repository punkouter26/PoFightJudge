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
}
