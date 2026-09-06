using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using PoMarriedFight.Client.Shared;

namespace PoMarriedFight.Unit.Client;

public class RedirectToLoginTests : BunitContext
{
    [Fact]
    public void Unauthenticated_visitors_are_sent_to_login_with_a_return_url()
    {
        var nav = Services.GetRequiredService<BunitNavigationManager>();
        nav.NavigateTo("history?tab=fight");

        Render<RedirectToLogin>();

        nav.Uri.Should().Be(nav.BaseUri + "login?returnUrl=%2Fhistory%3Ftab%3Dfight");
    }
}
