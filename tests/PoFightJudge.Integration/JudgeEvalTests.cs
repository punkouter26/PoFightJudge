using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PoFightJudge.Api.Features.Ai;
using PoFightJudge.Api.Features.Diagnostics;
using PoFightJudge.Api.Features.Profiles;
using PoFightJudge.Api.Features.Watch;
using PoFightJudge.Shared.Configuration;
using PoFightJudge.Shared.Models;
using Xunit.Abstractions;

namespace PoFightJudge.Integration;

/// <summary>
/// The golden fights: sixteen WATCH transcripts whose winner a person picked on the judge's own criteria, run against
/// the real judge. It is the number a model or prompt change has to hold — without it, a cheaper tier or a reworded
/// rubric is a guess that only the bill ever grades. Half the cases are traps: the funnier, louder or more
/// in-character side has the weaker argument, which is exactly what a judge that reads tone over reasoning gets wrong.
/// </summary>
/// <remarks>
/// It skips without a key and costs about sixteen judge calls with one. <c>EVAL_JUDGE_MODEL</c> points it at another
/// tier, which is how the cheaper model is compared before anybody changes <c>Ai:JudgeModel</c>. Asserted on the
/// aggregate, not per case: one odd ruling is noise, a judge that misses the traps is the regression.
/// </remarks>
public class JudgeEvalTests(ITestOutputHelper output)
{
    /// <summary>The share of golden fights the judge must call the way the person did.</summary>
    public const double MinAgreement = 0.80;

    private const string H = WatchRound.Husband;
    private const string W = WatchRound.Wife;

    private static string? Key => Environment.GetEnvironmentVariable(ConfigKeys.Ai.GeminiApiKeyEnvVar);

    public static IReadOnlyList<GoldenFight> Cases { get; } =
    [
        new("evidence-vs-insults", "dishes", H,
        [
            (H, "The dishwasher ran four times this week and I loaded three of them. The receipt from the detergent I bought Sunday is on the fridge."),
            (W, "Oh wow, a receipt. You want a medal? You're so annoying when you do this."),
            (H, "You said I never load it. Monday, Wednesday and Friday were me — you were at your sister's on Wednesday."),
            (W, "You always twist things. Everyone at work thinks you're exhausting."),
            (H, "So the point stands: three of four loads. If you want an even split, take Saturday and Tuesday."),
            (W, "Whatever. You're impossible (snaps)."),
        ]),
        new("evidence-vs-insults-mirror", "budget", W,
        [
            (H, "You're terrible with money and you know it. Classic you."),
            (W, "Our card statement shows $412 on takeout last month; $340 of it was your lunch orders. I printed it."),
            (H, "Here we go, Miss Spreadsheet. Nobody likes a show-off."),
            (W, "That's not an answer. If we pack lunches three days a week we save about $200 a month."),
            (H, "You sound like my mother. Unbelievable."),
            (W, "Your mother isn't on the statement. The $340 is (coldly)."),
        ]),
        new("funny-dodger-loses", "thermostat", W,
        [
            (H, "If I wanted to live in a sauna I'd have married a Finnish spa. Seventy-eight degrees? Are we growing orchids?"),
            (W, "The bill went from $90 to $160 when you set it to 64 in January — the heater ran all night fighting the draft."),
            (H, "Ah yes, the famous Draft. Should we name it? Give it a room? It's practically family (smirks)."),
            (W, "The draft is the window in the hall. Seal it for $15 and 70 degrees costs less than your 64 did."),
            (H, "Fifteen dollars and a weekend of me on a ladder. Romance is dead."),
            (W, "So you agree the window is the problem. Then the thermostat isn't."),
        ]),
        new("funny-dodger-loses-mirror", "the dog", H,
        [
            (H, "The vet said Biscuit is twelve pounds over. She's fed twice by you and twice by me — that's four dinners."),
            (W, "Biscuit is not fat, she's emotionally voluminous. She contains multitudes (laughs)."),
            (H, "The vet's chart says 52 pounds; the breed range tops out at 40. One feeder fixes it — I'll take mornings, you take nights."),
            (W, "Next you'll put the dog on a spreadsheet. Biscuit deserves joy, not a fiscal policy."),
            (H, "Joy for a dog is not having bad hips at eight. One meal each, logged on the fridge."),
            (W, "Fine, but I'm naming the log 'The Biscuit Accords' (smug)."),
        ]),
        new("emotion-vs-numbers", "vacation", W,
        [
            (H, "If you loved me you'd want Hawaii. I don't understand how you can say no to this."),
            (W, "Hawaii for a week is about $6,200 with flights. We have $3,800 saved and the car registration is due in May."),
            (H, "This hurts. I feel like you never want me to be happy."),
            (W, "I want us to go. The coast trip is $1,900, or we wait until October and Hawaii fits the savings."),
            (H, "You just don't care about what matters to me."),
            (W, "October Hawaii is what matters to you, and it's paid for. I'm saying yes — in October."),
        ]),
        new("strawman-corrected", "screen time", H,
        [
            (H, "I'd like phones off the table at dinner. That's all — dinner."),
            (W, "So you want to ban phones from the house and live like it's 1850? Typical control freak."),
            (H, "I didn't say the house. I said dinner: about forty minutes a day. Last night you were on yours for thirty of them."),
            (W, "Next you'll be confiscating the TV and the internet. Should I hand over my laptop too?"),
            (H, "No. The TV is fine, the laptop is fine. Forty minutes, no phones, and I'll put mine in the drawer first."),
            (W, "You're impossible (snaps)."),
        ]),
        new("whataboutism-loses", "the car", W,
        [
            (H, "Fine, I dented the bumper. But what about the time you locked the keys in the car in 2019?"),
            (W, "The bumper is $480 to fix and it's the third scrape this year, all reversing in the same garage."),
            (H, "And what about your parking at the mall? You take up two spots."),
            (W, "Two spots doesn't cost $480. A $40 parking sensor would stop the garage scrapes."),
            (H, "And you never fill the tank either. So."),
            (W, "The tank isn't dented. The sensor is $40; the next bumper is $480."),
        ]),
        new("persona-loyalty-trap", "in-laws", H,
        [
            (H, "Your parents stayed nine days last time; we agreed five. I'm asking for five, with their flights booked to match."),
            (W, "As a woman whose motto is 'family above everything', I could never put a limit on family. That is who I am."),
            (H, "Five days isn't a limit on family; it's what we both agreed in March, in writing, in the group chat."),
            (W, "Family above everything. My philosophy has not changed and it never will (proudly)."),
            (H, "Then let's honour both: five days here, and we visit them for a long weekend in the fall."),
            (W, "I stand by who I am. Family. Above. Everything."),
        ]),
        new("loud-last-word-trap", "moving", H,
        [
            (H, "The Oak Street place is twelve minutes from both our jobs; the one you want is forty-five for me each way."),
            (W, "I just like the other one better."),
            (H, "That's seven extra hours of driving a week, and the rent is $300 more. What does it give us that Oak Street doesn't?"),
            (W, "It has a nicer kitchen."),
            (H, "Oak Street's kitchen is smaller but we'd save $3,600 a year — enough to redo the counters."),
            (W, "I AM NOT LIVING IN A SHOEBOX AND THAT IS FINAL. END OF DISCUSSION. YOU LOSE (shouting)!"),
        ]),
        new("strongest-vs-weakest-rebuttal", "a second job", W,
        [
            (H, "I want the weekend job: it's $1,100 a month and it's only Saturdays, and we're behind on the credit card."),
            (W, "The card is $4,000 at 24%. But Saturdays are the only day you see the kids — and the job's in Dayton, ninety minutes away."),
            (H, "You said 'Dayton' like it's the moon. I've driven further for a concert."),
            (W, "Concerts aren't every week. Three hours of driving cuts the $1,100 to about $850 after gas, and the kids lose their one day."),
            (H, "You never liked my concerts anyway."),
            (W, "The balance-transfer card at 0% for 18 months saves more than the job nets in a year, and you keep Saturdays."),
        ]),
        new("anecdote-vs-specifics", "the gym", W,
        [
            (H, "Everyone knows gym memberships are a waste. My buddy Dave never went once."),
            (W, "We've each gone 11 times since March according to the check-in app. That's $4 a visit on the $45 plan."),
            (H, "Everybody I know quits by summer. It's just how it is."),
            (W, "The cheaper plan is $25 and we'd still be under $3 a visit at our current rate."),
            (H, "Trust me, it's a known thing."),
            (W, "Then let's look at it again in September with the check-ins in front of us."),
        ]),
        new("anecdote-vs-specifics-mirror", "groceries", H,
        [
            (H, "Delivery added $86 in fees and tips in April — it's on the app's order history, six orders."),
            (W, "Everyone does delivery now. My friends all say it's basically free."),
            (H, "It wasn't free for us: $86 is more than one full weekly shop at Aldi, which runs us about $70."),
            (W, "I read somewhere it saves money because you buy less on impulse."),
            (H, "Our April delivery orders averaged $112, the in-store ones $94. The impulse buys went up, not down."),
            (W, "Well, that's what people say (sighs)."),
        ]),

        // The subtle half: both sides sound reasonable, and the case turns on who answers the other's actual point.
        new("polite-but-non-responsive", "rent split", H,
        [
            (H, "I earn $4,000, you earn $6,000. Splitting the $2,000 rent 50/50 leaves me with nothing. Split it 40/60, like our incomes."),
            (W, "I hear you, and I really appreciate you raising it calmly. But 50/50 is fair because we both use the apartment equally."),
            (H, "Using it equally isn't the point. 50/50 is 25% of my pay and 17% of yours. That's not equal, that's convenient for you."),
            (W, "I understand you're frustrated, sweetheart. I just feel equal use means equal share."),
            (H, "Then answer the numbers. 40/60 is 20% of each of our pays — that is the equal share."),
            (W, "I love you, and I think we can agree that fairness matters to both of us (warmly)."),
        ]),
        new("fair-concern-answered", "solar panels", H,
        [
            (H, "Solar is $18,000 and our quote saves $150 a month. With twenty-five years of panel life that's well worth it."),
            (W, "Installers always overstate savings. My cousin's barely saved anything."),
            (H, "Our quote uses our own last twelve bills — $1,800 a year — and the 30% tax credit takes the cost to $12,600. Seven years to pay back."),
            (W, "But we might move in five years. Then we've lost it all."),
            (H, "Fair worry. The agent's comps put solar at about 4% on a sale here — roughly $16,000 on ours. It's in the email."),
            (W, "I just think it's risky to spend that much."),
        ]),
        new("concession-wins", "christmas", W,
        [
            (W, "We did Christmas at your parents' the last two years. It's my parents' turn."),
            (H, "Your parents are a five-hour flight; mine are twenty minutes away, and my dad has just had hip surgery."),
            (W, "You're right about your dad — that changes it. Christmas Eve with yours, then we fly to mine on the 26th, when fares drop by about half."),
            (H, "But it's always been at my parents'. That's tradition."),
            (W, "The tradition is two years old. My plan gets your dad his Christmas Eve and costs us less."),
            (H, "It's just how we do things."),
        ]),
        new("specific-but-irrelevant", "the garage", W,
        [
            (H, "The garage is a mess because of your craft supplies: there are nine plastic bins of them, I counted."),
            (W, "Seven bins are mine; the two by the door are your old camping gear. The real problem is the car can't fit, and that's the bikes."),
            (H, "The bins are 18 by 24 inches each and the shelf is rated for 200 pounds. I checked the label."),
            (W, "That's not the problem I raised. Hang the four bikes on wall hooks — $30 at the hardware store — and the car fits."),
            (H, "The shelf was $89 at Costco in 2021. We have the receipt."),
            (W, "Then we keep the shelf. Hooks for the bikes, and the car goes back inside."),
        ]),
    ];

    [SkippableFact]
    public async Task The_real_judge_agrees_with_the_golden_fights()
    {
        Skip.If(string.IsNullOrWhiteSpace(Key), $"{ConfigKeys.Ai.GeminiApiKeyEnvVar} is not set, so there is no judge to evaluate.");

        var models = GeminiModelOptions.Defaults with { Judge = Environment.GetEnvironmentVariable("EVAL_JUDGE_MODEL") ?? GeminiModelOptions.Defaults.Judge };
        var services = new ServiceCollection()
            .AddLogging(b => b.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<AiLatencyTracker>()
            .AddGeminiClients(Key);
        await using var provider = services.BuildServiceProvider();
        var judge = new WatchAi(ActivatorUtilities.CreateInstance<GeminiTextClient>(provider), models);

        var husband = Profile.Create("JD", ProfileRole.Husband, "golf, grilling", "clutter, being late", "money, chores", "measure twice, cut once");
        var wife = Profile.Create("MK", ProfileRole.Wife, "gardening, true crime", "waste, loud chewing", "in-laws, plans", "family above everything");

        var agreed = 0;
        foreach (var fight in Cases)
        {
            var rounds = fight.Lines.Select(l => new RoundContext(l.Speaker, l.Text, "angry")).ToList();
            var verdict = await judge.JudgeAsync(husband, wife, rounds);
            var expected = string.Equals(fight.Winner, H, StringComparison.Ordinal) ? husband.Initials : wife.Initials;
            var hit = string.Equals(verdict.Winner, expected, StringComparison.Ordinal);
            agreed += hit ? 1 : 0;
            output.WriteLine($"{(hit ? "ok  " : "MISS")} {fight.Name,-32} expected {expected} got {(verdict.Winner.Length == 0 ? "draw" : verdict.Winner)} ({verdict.HusbandScore}-{verdict.WifeScore})");
        }

        var agreement = (double)agreed / Cases.Count;
        output.WriteLine($"{models.Judge}: {agreed}/{Cases.Count} = {agreement:P0}");
        agreement.Should().BeGreaterThanOrEqualTo(MinAgreement, "the judge has to call the golden fights the way a person did, traps included");
    }

    public sealed record GoldenFight(string Name, string Topic, string Winner, IReadOnlyList<(string Speaker, string Text)> Lines);
}
