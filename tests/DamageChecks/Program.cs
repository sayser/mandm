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

var jitter = new DamageLineBuffer();
jitter.MarkReady();
jitter.Prime(["You crush a crystal dervish for 32 points of damage."]);
var jittered = jitter.Observe(["You crush a crystal dervish for 32 points of damage"]);
Check("punctuation jitter is not a new hit", jittered.Count == 0);
jitter.Observe(["You have slain a crystal dervish!"]);
var returned = jitter.Observe(
[
    "You have slain a crystal dervish!",
    "You crush a crystal dervish for 32 points of damage."
]);
Check("same crush after it leaves the screen is new",
    returned.Count == 1 && Out(returned[0], 32, "a crystal dervish", "crush"));

var wrapped = DamageLineParser.JoinWrapped(
[
    "Your Greater Pestilence hits a tormented guardian for 438 points of",
    "Disease Damage."
]);
Check("wrapped pestilence is one hit",
    wrapped.Count == 1
    && Out(wrapped[0], 438, "a tormented guardian", "Greater Pestilence"));
var wrappedCrush = DamageLineParser.JoinWrapped(
[
    "You crush a crystal dervish for 32",
    "points of damage."
]);
Check("wrapped crush is one hit",
    wrappedCrush.Count == 1 && Out(wrappedCrush[0], 32, "a crystal dervish", "crush"));
Check("pestilence line",
    Out("Your Greater Pestilence hits a tormented guardian for 438 points of Disease Damage.",
        438, "a tormented guardian", "Greater Pestilence"));
Check("crush line",
    Out("You crush a crystal dervish for 32 points of damage.", 32, "a crystal dervish", "crush"));
var separateHits = DamageLineParser.JoinWrapped(
[
    "You crush a crystal dervish for 32 points of damage.",
    "Your Greater Pestilence hits a tormented guardian for 438 points of Disease Damage."
]);
Check("adjacent hits stay separate",
    separateHits.Count == 2
    && Out(separateHits[0], 32, "a crystal dervish", "crush")
    && Out(separateHits[1], 438, "a tormented guardian", "Greater Pestilence"));

Check("slain line",
    DamageLineParser.TryParseSlain("[07:28:10] You have slain a crystal dervish!", out var slain)
    && slain == "a crystal dervish");
Check("slain is not damage", None("You have slain a crystal dervish!"));
Check("party member slain line",
    DamageLineParser.TryParseSlain(
        "Your party member Andromocles has slain a tormented guardian!",
        out var member, out var guardian)
    && member == "Andromocles"
    && guardian == "a tormented guardian");
Check("party slain is not damage",
    None("Your party member Andromocles has slain a tormented guardian!"));
Check("another player slain line",
    DamageLineParser.TryParseSlain("Bob has slain a rat!", out var bob, out var rat)
    && bob == "Bob" && rat == "a rat");

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
    ["You have slain a crystal dervish"],
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

var partyBlow = "You slash a tormented guardian for 50 points of damage.";
var partySlain = "Your party member Andromocles has slain a tormented guardian!";
var partyFight = new DamageFight();
partyFight.ApplyFrame([partyBlow], [partyBlow], t0);
Check("fight is open before the party kill", partyFight.Tracking && partyFight.DamageDealt == 50);
partyFight.ApplyFrame([partySlain], [partySlain, partyBlow], t0.AddSeconds(2));
Check("party kill ends the fight",
    !partyFight.Tracking && partyFight.HasFight && partyFight.DamageDealt == 50
    && partyFight.SlainBy == "Andromocles" && partyFight.SlainTarget == "a tormented guardian");
partyFight.ApplyFrame(
    [partySlain],
    [partySlain, partyBlow],
    t0.AddSeconds(3));
Check("leftover party slain does not open a fight", !partyFight.Tracking && partyFight.DamageDealt == 50);
partyFight.ApplyFrame(
    ["Your party member Andromocles has slaln a tormented guardian!"],
    ["Your party member Andromocles has slaln a tormented guardian!"],
    t0.AddSeconds(4));
Check("near party slain OCR does not open a fight", !partyFight.Tracking && partyFight.DamageDealt == 50);
var partyTogether = new DamageFight();
partyTogether.ApplyFrame([partySlain, partyBlow], [partySlain, partyBlow], t0);
Check("party slain before the killing blow in one read does not stay open",
    !partyTogether.Tracking && partyTogether.DamageDealt == 50 && partyTogether.SlainBy == "Andromocles");
partyFight.ApplyFrame(
    ["A wolf hits YOU for 7 points of damage."],
    [partySlain, "A wolf hits YOU for 7 points of damage."],
    t0.AddSeconds(8));
Check("a hit after a party kill starts the next fight", partyFight.Tracking && partyFight.DamageDealt == 0);

var pest = "Your Greater Pestilence hits a tormented guardian for 438 points of Disease Damage.";
var crush = "You crush a crystal dervish for 32 points of damage.";
var stuck = new DamageFight();
stuck.ApplyFrame([crush], [crush], t0);
stuck.ApplyFrame(
    ["You have slain a crystal dervish!"],
    [crush, "You have slain a crystal dervish!"],
    t0.AddSeconds(2));
Check("kill screen is stopped", !stuck.Tracking && stuck.DamageDealt == 32);
stuck.ApplyFrame([crush], [crush, "You have slain a crystal dervish!"], t0.AddSeconds(3));
Check("repeated crush after the kill still counts",
    stuck.Tracking && stuck.DamageDealt == 32 && stuck.Abilities[0].Name == "Crush");
stuck.ApplyFrame([pest], [crush, pest], t0.AddSeconds(4));
Check("pestilence counts in the new fight",
    stuck.Tracking && stuck.DamageDealt == 470
    && stuck.Abilities.Any(row => row.Name == "Greater Pestilence" && row.Damage == 438));
var splitFight = new DamageFight();
splitFight.ApplyFrame(["You have slain a rat!"], ["You have slain a rat!"], t0);
var splitHit = DamageLineParser.JoinWrapped(
[
    "Your Greater Pestilence hits a tormented guardian for 438 points of",
    "Disease Damage."
]);
splitFight.ApplyFrame(splitHit, splitHit, t0.AddSeconds(2));
Check("a wrapped hit starts the fight that stayed stopped",
    splitFight.Tracking && splitFight.DamageDealt == 438);
var still = new DamageFight();
still.ApplyFrame([crush], [crush], t0);
still.ApplyFrame(
    [pest],
    ["You have slain a crystal dervish!", crush, pest],
    t0.AddSeconds(1));
Check("slain text still on screen does not end the fight",
    still.Tracking && still.DamageDealt == 470);
still.ApplyFrame(
    ["Your party member Andromocles has slain a tormented guardian!"],
    [pest, "Your party member Andromocles has slain a tormented guardian!"],
    t0.AddSeconds(2));
Check("a new party slain line ends the fight",
    !still.Tracking && still.SlainBy == "Andromocles" && still.DamageDealt == 470);

const string landPhrase = "looks weak";
const string ratLine = "a bloated plague rat looks weak.";
Check("land phrase matches inside the rat line",
    DebuffPhrase.TryRead(ratLine, landPhrase, null, out var ratMonster)
    && ratMonster == "a bloated plague rat");
Check("land phrase does not match a longer word",
    !DebuffPhrase.TryRead("a bloated plague rat looks weaker.", landPhrase, null, out _));
Check("land phrase does not claim a longer fade phrase",
    !DebuffPhrase.TryRead(
        "a bloated plague rat no longer looks weak.",
        landPhrase,
        ["no longer looks weak"],
        out _));
Check("fade phrase keeps the monster in front",
    DebuffPhrase.TryRead(
        "a bloated plague rat no longer looks weak.",
        "no longer looks weak",
        [landPhrase],
        out var fadedMonster)
    && fadedMonster == "a bloated plague rat");
Check("cast line is not a land",
    !DebuffPhrase.TryRead("You begin casting Greater Hex of Enfeeblement.", landPhrase, null, out _));
Check("article differences are the same monster",
    DebuffPhrase.SameMonster("a bloated plague rat", "the bloated plague rat"));
Check("a different monster is not the same",
    !DebuffPhrase.SameMonster("a bloated plague rat", "a crystal dervish"));
DamageLineParser.TryParseSlain("You have slain a bloated plague rat!", out var slainRat);
Check("slain names the monster in front of the land phrase",
    DebuffPhrase.SameMonster(ratMonster, slainRat));
DamageLineParser.TryParseSlain(
    "Your party member Andromocles has slain a bloated plague rat!",
    out var partyRat);
Check("a party slain names that same monster",
    DebuffPhrase.SameMonster("a bloated plague rat", partyRat));
var seen = new HashSet<string>(StringComparer.Ordinal);
var firstSight = DebuffPhrase.FreshLines(seen, [ratLine, "You begin casting Greater Hex of Enfeeblement."]);
var stillVisible = DebuffPhrase.FreshLines(seen, [ratLine]);
var secondMob = DebuffPhrase.FreshLines(seen, [ratLine, "a crystal dervish looks weak."]);
Check("a new land line is fresh and a still-visible line is not",
    firstSight.Count == 2 && stillVisible.Count == 0 && secondMob.Count == 1
    && secondMob[0].Contains("crystal dervish", StringComparison.Ordinal));

var held = new DamageFight();
held.ApplyFrame([crush], [crush], t0);
held.Hold(t0.AddSeconds(4));
Check("stop holds the fight without a slain line",
    !held.Tracking && held.HasFight && held.DamageDealt == 32 && held.SlainBy.Length == 0);
held.ApplyFrame(
    ["You have slain a crystal dervish!"],
    ["You have slain a crystal dervish!"],
    t0.AddSeconds(5));
Check("a slain line while held does nothing",
    !held.Tracking && held.DamageDealt == 32 && held.SlainBy.Length == 0);
held.ApplyFrame(
    ["You slash a tormented guardian for 10 points of damage."],
    ["You slash a tormented guardian for 10 points of damage."],
    t0.AddSeconds(6));
Check("the next hit after a hold starts a new fight",
    held.Tracking && held.DamageDealt == 10 && held.SlainBy.Length == 0);

var landMemory = new Dictionary<string, int>(StringComparer.Ordinal);
List<DebuffPhrase.DebuffSight> LandRead(params string[] lines)
{
    var empty = lines.Length == 0;
    var sights = new List<DebuffPhrase.DebuffSight>();
    if (!empty)
    {
        foreach (var line in lines)
        {
            if (DebuffPhrase.TryRead(line, landPhrase, null, out var monster))
                sights.Add(new DebuffPhrase.DebuffSight(line, monster));
        }
    }

    return DebuffPhrase.FreshSights(landMemory, sights, landPhrase, empty);
}

var landOnce = LandRead("a bloated plague rat looks weak.");
var landStill = LandRead("a bloated plague rat looks weak.");
var landStillAgain = LandRead("a bloated plague rat looks weak.");
Check("screenshot land line alerts once and not on the next two reads",
    landOnce.Count == 1
    && landOnce[0].Monster == "a bloated plague rat"
    && landStill.Count == 0
    && landStillAgain.Count == 0);
Check("a punctuation re-read of that land line does not alert",
    LandRead("a bloated plague rat looks weak!").Count == 0);
Check("a slain line does not clear the land alert memory",
    LandRead("a bloated plague rat looks weak.", "You have slain a bloated plague rat!").Count == 0);
var secondMonster = LandRead(
    "a bloated plague rat looks weak.",
    "a crystal dervish looks weak.");
Check("a different monster alerts once while the first line stays",
    secondMonster.Count == 1 && secondMonster[0].Monster == "a crystal dervish");
Check("that second monster does not alert on the next read",
    LandRead("a bloated plague rat looks weak.", "a crystal dervish looks weak.").Count == 0);
var landAfterEmpty = LandRead();
var landSameAfterEmpty = LandRead("a bloated plague rat looks weak.");
Check("an empty read does not make the same land line alert again",
    landAfterEmpty.Count == 0 && landSameAfterEmpty.Count == 0);
var landMissedOnce = LandRead("You begin casting Greater Hex of Enfeeblement.");
var landHeld = LandRead("a bloated plague rat looks weak.");
Check("one successful read without the land line does not drop it",
    landMissedOnce.Count == 0 && landHeld.Count == 0);
LandRead("You begin casting Greater Hex of Enfeeblement.");
LandRead("You begin casting Greater Hex of Enfeeblement.");
var landReturned = LandRead("a bloated plague rat looks weak.");
Check("the land line alerts again after two successful reads without it",
    landReturned.Count == 1 && landReturned[0].Monster == "a bloated plague rat");

var fadePhrase = "You appear";
var fadeMemory = new Dictionary<string, int>(StringComparer.Ordinal);
List<DebuffPhrase.DebuffSight> FadeRead(params string[] lines)
{
    var empty = lines.Length == 0;
    var sights = new List<DebuffPhrase.DebuffSight>();
    if (!empty)
    {
        foreach (var line in lines)
        {
            if (DebuffPhrase.TryRead(line, fadePhrase, ["You begin to feel yourself appearing"], out var monster))
                sights.Add(new DebuffPhrase.DebuffSight(line, monster));
        }
    }

    return DebuffPhrase.FreshSights(fadeMemory, sights, fadePhrase, empty);
}

var fadeOnce = FadeRead("You appear.");
var fadeEmpty = FadeRead();
var fadeSame = FadeRead("You appear.");
Check("a visible fade line, then an empty read, then the same line, does not alert again",
    fadeOnce.Count == 1 && fadeEmpty.Count == 0 && fadeSame.Count == 0);
var fadeGoneOnce = FadeRead("You begin casting Invisibility.");
var fadeStillHeld = FadeRead("You appear.");
Check("one successful read without the fade line does not alert again",
    fadeGoneOnce.Count == 0 && fadeStillHeld.Count == 0);
FadeRead("You begin casting Invisibility.");
FadeRead("You begin casting Invisibility.");
var fadeBack = FadeRead("You appear.");
Check("a fade line gone for two successful reads alerts when it comes back",
    fadeBack.Count == 1);
Check("the invis warn line is not the fade line",
    FadeRead("You begin to feel yourself appearing.").Count == 0);

var region = new RegionLineMemory();
int CastAlerts(params string[] lines)
{
    var fresh = region.Observe(lines);
    var count = 0;
    foreach (var line in fresh)
    {
        if (TextMatch.ContainsLoose(line, "You begin casting"))
            count++;
    }

    return count;
}

var castOnce = CastAlerts("You begin casting Invisibility.");
var castTwice = CastAlerts("You begin casting Invisibility.");
var castThrice = CastAlerts("You begin casting Invisibility!");
Check("the same line read three times alerts once",
    castOnce == 1 && castTwice == 0 && castThrice == 0);

var castAppended = CastAlerts(
    "You begin casting Invisibility.",
    "a bloated plague rat looks weak.",
    "You begin casting Greater Hex of Enfeeblement.");
var castAppendedAgain = CastAlerts(
    "You begin casting Invisibility.",
    "a bloated plague rat looks weak.",
    "You begin casting Greater Hex of Enfeeblement.");
Check("a new cast appended under old lines alerts once",
    castAppended == 1 && castAppendedAgain == 0);

var blank = region.Observe([]);
var afterBlank = CastAlerts(
    "You begin casting Invisibility.",
    "a bloated plague rat looks weak.",
    "You begin casting Greater Hex of Enfeeblement.");
Check("a blank read then the same text does not alert",
    blank.Count == 0 && afterBlank == 0);

var castWrap = new RegionLineMemory();
castWrap.Observe(["You begin casting Invisibility."]);
var wrappedFresh = castWrap.Observe(["You begin casting", "Invisibility."]);
var wrappedAlerts = 0;
foreach (var line in wrappedFresh)
{
    if (TextMatch.ContainsLoose(line, "You begin casting"))
        wrappedAlerts++;
}
Check("a wrapped fragment of the same cast line does not alert",
    wrappedAlerts == 0);

CastAlerts("a bloated plague rat looks weak.");
var heldCast = CastAlerts("You begin casting Invisibility.");
Check("one good read without the cast line does not drop it", heldCast == 0);
CastAlerts("a bloated plague rat looks weak.");
CastAlerts("You crush a crystal dervish for 32 points of damage.");
var castBack = CastAlerts("You begin casting Invisibility.");
Check("a line gone for two good reads then back alerts once", castBack == 1);

var startup = new RegionLineMemory();
startup.Observe(["You begin casting Invisibility."]);
var startupAgain = startup.Observe(["You begin casting Invisibility."]);
Check("text remembered at startup does not alert on the next read", startupAgain.Count == 0);

var crowded = new RegionLineMemory();
var many = new string[80];
for (var i = 0; i < many.Length; i++)
    many[i] = "combat line number " + i;
crowded.Observe(many);
Check("a full read keeps the lines that are still on screen", crowded.Count == 80);
crowded.Observe(["only this line remains"]);
crowded.Observe(["only this line remains"]);
Check("lines that have left the screen are dropped", crowded.Count == 1);

if (failed > 0)
{
    Console.WriteLine(failed + " failed");
    return 1;
}

Console.WriteLine("all passed");
return 0;
