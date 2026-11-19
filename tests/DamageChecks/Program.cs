using MandM.Services;

var failed = 0;

void Check(string name, bool ok)
{
    if (ok)
    {
        Console.WriteLine("ok  " + name);
        return;
    }

    failed++;
    Console.WriteLine("FAIL " + name);
}

bool Out(string line, int amount, string who, string? detail = null)
{
    if (!DamageLineParser.TryParse(line, out var hit) || !hit.Outgoing || hit.Amount != amount)
        return false;
    if (!string.Equals(hit.Who, who, StringComparison.Ordinal))
        return false;
    return detail is null || string.Equals(hit.Detail, detail, StringComparison.Ordinal);
}

bool In(string line, int amount, string who)
{
    if (!DamageLineParser.TryParse(line, out var hit) || hit.Outgoing || hit.Amount != amount)
        return false;
    return string.Equals(hit.Who, who, StringComparison.Ordinal);
}

bool None(string line) => !DamageLineParser.TryParse(line, out _);

Check("offhand crush", Out(
    "[07:27:45] You crush a smuggler with your offhand for 16 points of damage.",
    16, "a smuggler", "offhand"));
Check("slash", Out(
    "[07:27:45] You slash a smuggler for 16 points of damage.",
    16, "a smuggler", "slash"));
Check("hit without article", Out(
    "You hit goblin for 22 points of damage.",
    22, "goblin", "hit"));
Check("your spell", Out(
    "Your Arcane Spear II hits a smuggler for 34 points of Magic Damage",
    34, "a smuggler", "Arcane Spear II"));
Check("critical suffix still counts", Out(
    "You slash a rat for 12 points of damage. (Critical)",
    12, "a rat"));
Check("dot from your spell", Out(
    "a smuggler has taken 9 damage from your Denon's Disruptive Discord.",
    9, "a smuggler", "Denon's Disruptive Discord"));
Check("incoming slash", In(
    "A goblin slashes YOU for 12 points of damage.",
    12, "A goblin"));
Check("incoming magic", In(
    "A smuggler hits YOU for 10 points of Magic Damage.",
    10, "A smuggler"));
Check("you have taken", In(
    "You have taken 8 damage from a goblin's disease.",
    8, "a goblin's disease"));
Check("ignore other player", None("Bob slashes a smuggler for 16 points of damage."));
Check("ignore mob vs mob", None("A forsaken revenant slashes a forsaken revenant for 104 points of damage."));
Check("ignore cast line", None("[07:27:44] You begin casting Arcane Spear II."));
Check("ignore buff land", None("You feel the speed of the pack within you"));

var buffer = new DamageLineBuffer();
buffer.Invalidate();
buffer.Prime(
[
    "[07:27:44] You slash a smuggler for 10 points of damage.",
    "A goblin hits YOU for 4 points of damage."
]);
buffer.MarkReady();
var same = buffer.Observe(
[
    "[07:27:44] You slash a smuggler for 10 points of damage.",
    "A goblin hits YOU for 4 points of damage."
]);
Check("baseline lines are not new", same.Count == 0);

var scrolled = buffer.Observe(
[
    "A goblin hits YOU for 4 points of damage.",
    "[07:27:46] You slash a smuggler for 16 points of damage."
]);
Check("scrolled-in line is new", scrolled.Count == 1 && Out(scrolled[0], 16, "a smuggler"));

var party = buffer.Observe(
[
    "[07:27:46] You slash a smuggler for 16 points of damage.",
    "Bob slashes a smuggler for 99 points of damage."
]);
Check("party line can be new text but is not your damage",
    party.Count == 1 && None(party[0]));

var reshuffle = new DamageLineBuffer();
reshuffle.MarkReady();
reshuffle.Prime(["alpha hits YOU for 1 points of damage.", "beta line"]);
var reorder = reshuffle.Observe(["beta line", "alpha hits YOU for 1 points of damage."]);
Check("reordered same lines are not new", reorder.Count == 0);

Check("slain line",
    DamageLineParser.TryParseSlain("[07:28:10] You have slain a crystal dervish!", out var slain)
    && slain == "a crystal dervish");
Check("slain is not damage", None("You have slain a crystal dervish!"));
Check("other slain ignored", !DamageLineParser.TryParseSlain("Bob has slain a rat!", out _));

var t0 = new DateTime(2026, 11, 19, 11, 0, 0);
var fight = new DamageFight();
DamageLineParser.TryParse("You slash a smuggler for 16 points of damage.", out var slash);
DamageLineParser.TryParse("Your Arcane Spear II hits a smuggler for 34 points of Magic Damage", out var spear);
DamageLineParser.TryParse("You slash a smuggler for 10 points of damage.", out var slash2);
fight.OnDamage(slash, t0);
fight.OnDamage(spear, t0.AddSeconds(2));
fight.OnDamage(slash2, t0.AddSeconds(4));
Check("fight damage is outgoing only", fight.DamageDealt == 60 && fight.Tracking);
var abilities = fight.Abilities;
Check("ability breakdown",
    abilities.Count == 2
    && abilities[0].Name == "Arcane Spear II" && abilities[0].Damage == 34
    && abilities[1].Name == "Slash" && abilities[1].Damage == 26);
Check("dps is damage over seconds", Math.Abs(fight.DpsAt(t0.AddSeconds(4)) - 15.0) < 0.01);

fight.OnSlain("a crystal dervish", t0.AddSeconds(5));
Check("slain stops the fight", !fight.Tracking && fight.SlainTarget == "a crystal dervish");
Check("ended duration stays put", Math.Abs(fight.SecondsAt(t0.AddSeconds(30)) - 5) < 0.01);

DamageLineParser.TryParse("A goblin hits YOU for 8 points of damage.", out var incoming);
fight.OnDamage(incoming, t0.AddSeconds(40));
Check("next hit starts a new fight", fight.Tracking && fight.DamageDealt == 0 && fight.Abilities.Count == 0);
fight.OnDamage(slash, t0.AddSeconds(41));
Check("new fight counts only new damage", fight.DamageDealt == 16 && fight.Abilities[0].Name == "Slash");

var screen = new DamageFight();
var kill = "You slash a crystal dervish for 40 points of damage.";
var slainText = "You have slain a crystal dervish!";
screen.ApplyFrame([kill], [kill], t0);
Check("fight is open before the slain line", screen.Tracking && screen.DamageDealt == 40);
screen.ApplyFrame([slainText], [slainText, kill], t0.AddSeconds(3));
Check("slain line stops the fight",
    !screen.Tracking && screen.HasFight && screen.SlainTarget == "a crystal dervish" && screen.DamageDealt == 40);
screen.ApplyFrame(
    ["You have slain a crystal dervish", "You slash a crystal dervish for 40 points of damage"],
    ["You have slain a crystal dervish", kill],
    t0.AddSeconds(4));
Check("leftover slain OCR does not open a fight", !screen.Tracking && screen.DamageDealt == 40);
var together = new DamageFight();
together.ApplyFrame([slainText, kill], [slainText, kill], t0);
Check("slain before the killing blow in one read does not stay open",
    !together.Tracking && together.DamageDealt == 40);
screen.ApplyFrame(
    ["You have slaln a crystal dervish!"],
    ["You have slaln a crystal dervish!"],
    t0.AddSeconds(5));
Check("near slain OCR does not open a fight", !screen.Tracking && screen.DamageDealt == 40);
screen.ApplyFrame(
    ["A wolf hits YOU for 7 points of damage."],
    ["You have slain a crystal dervish!", "A wolf hits YOU for 7 points of damage."],
    t0.AddSeconds(8));
Check("a later hit starts the next fight", screen.Tracking && screen.DamageDealt == 0);

if (failed > 0)
{
    Console.WriteLine(failed + " failed");
    return 1;
}

Console.WriteLine("all passed");
return 0;
