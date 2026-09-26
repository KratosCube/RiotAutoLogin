using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using System.Text.Json;

if (args.Length == 2 && args[0] == "--packages")
{
    await UpdatePackageChecks.RunAsync(args[1]);
    return;
}

int checks = 0;
void Equal<T>(T expected, T actual, string reason)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"{reason}: expected {expected}, got {actual}");
    checks++;
}

string directory = Path.Combine(Path.GetTempPath(), "RiotAutoLogin-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
try
{
    using var entries = JsonDocument.Parse("""
        [
          {"queueType":"RANKED_SOLO_5x5","tier":"EMERALD","rank":"IV","leaguePoints":78,"wins":41,"losses":40},
          {"queueType":"RANKED_TEAM_5x5","tier":"GOLD","rank":"II","leaguePoints":30,"wins":6,"losses":3},
          {"queueType":"RANKED_FLEX_SR","tier":"SILVER","rank":"I","wins":2,"losses":4},
          {"queueType":"RANKED_TFT","tier":"DIAMOND","wins":999}
        ]
        """);
    var ranks = RankedQueues.ParseRanks(entries.RootElement);
    Equal(3, ranks.Count, "Only the three requested ranked queues are counted");
    Equal(6, ranks[RankedQueue.RankedFives].Wins, "Ranked 5s has its own wins");
    Equal(0, ranks[RankedQueue.Flex].LeaguePoints, "Omitted zero LP is supported");
    Equal<RankedQueue?>(RankedQueue.RankedFives, RankedQueues.FromId(710), "2026 Ranked 5s queue ID");
    Equal<RankedQueue?>(null, RankedQueues.FromId(700), "Clash must not be treated as Ranked 5s");
    Equal<RankedQueue?>(null, RankedQueues.FromId(42), "Retired ranked teams must not be mixed with 2026 games");
    using var empty = JsonDocument.Parse("[]");
    var unranked = RankedQueues.ParseRanks(empty.RootElement);
    Equal(0, unranked[RankedQueue.SoloDuo].Wins, "A successful unranked refresh clears old wins");

    var account = new Account { RankInfo = "GOLD IV (40 LP, 10W/11L)", LeaguePoints = 40, Wins = 10, Losses = 11 };
    account.MigrateLegacyRank();
    Equal("10", account.DisplayWins, "Old Solo/Duo data survives migration");
    account.SelectedQueue = RankedQueue.Flex;
    Equal("—", account.DisplayWins, "Unknown Flex stats never display Solo/Duo totals");
    account.QueueRanks = ranks;
    Equal("2", account.DisplayWins, "Changing queues uses the matching cached rank");
    var reloadedAccount = JsonSerializer.Deserialize<Account>(JsonSerializer.Serialize(account))!;
    Equal(6, reloadedAccount.QueueRanks[RankedQueue.RankedFives].Wins, "All rank caches survive restart");
    Equal(false, JsonSerializer.Serialize(account).Contains("DisplayWins"), "Display-only values are not persisted");
    var changes = new List<string?>();
    account.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
    account.RefreshRankDisplay();
    Equal(false, changes.Contains(nameof(Account.AvatarPath)), "A rank refresh does not reload avatar images");
    account.GameName = "Renamed player";
    Equal(true, changes.Contains(nameof(Account.GameName)), "Editing an account updates its card without rebuilding it");

    var session = GameflowSnapshot.Parse("""{"phase":"ChampSelect","gameData":{"gameId":123,"queue":{"id":710,"type":"RANKED_TEAM_5x5"},"isSpectator":false}}""");
    Equal<RankedQueue?>(RankedQueue.RankedFives, session.Queue, "Read queue from LCU session");
    Equal(123L, session.GameId, "Read game identity");
    var classifier = new GameflowTimeClassifier();
    var inProgress = new GameflowSnapshot("InProgress", RankedQueue.SoloDuo, 100, false);
    Equal(TimedActivity.None, classifier.Classify(inProgress, null), "Attaching mid-game with unavailable API is not loading");
    Equal(TimedActivity.InGame, classifier.Classify(inProgress, 250), "Positive clock confirms an active game");
    Equal(TimedActivity.InGame, classifier.Classify(inProgress, null), "Transient live API failure does not restart loading");
    Equal(TimedActivity.None, classifier.Classify(inProgress with { Phase = "Reconnect" }, null), "Reconnect is not initial loading");
    Equal(TimedActivity.InGame, classifier.Classify(inProgress, null), "Returning from reconnect remains in game");
    Equal(TimedActivity.ChampSelect, classifier.Classify(session, null), "Champion select starts a fresh attempt");
    Equal(TimedActivity.Queue, classifier.Classify(session with { Phase = "Matchmaking" }, null), "Dodge returns to queue");
    classifier.Classify(session, null);
    Equal(TimedActivity.Loading, classifier.Classify(session with { Phase = "GameStart" }, null), "Observed launch begins initial loading");
    Equal(TimedActivity.InGame, classifier.Classify(session with { Phase = "InProgress" }, 1), "Loading ends at the running game clock");
    Equal(TimedActivity.None, classifier.Classify(inProgress with { IsSpectator = true }, 30), "Spectating is excluded");

    string path = Path.Combine(directory, "history.json");
    var ledger = new WaitingTimeLedger(path);
    var start = new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
    void Observe(int seconds, TimedActivity activity, string id = "account-a", RankedQueue queue = RankedQueue.SoloDuo) =>
        ledger.Observe(start.AddSeconds(seconds), seconds * 1000L, id, queue, activity);
    Observe(0, TimedActivity.Queue);
    Observe(4, TimedActivity.Queue);
    Observe(8, TimedActivity.ChampSelect);
    Observe(13, TimedActivity.ChampSelect);
    Observe(15, TimedActivity.Loading);
    Observe(20, TimedActivity.InGame);
    Observe(25, TimedActivity.None);
    Equal(new WaitingTimeTotals(8, 7, 5, 5), ledger.GetTotals(RankedQueue.SoloDuo, false, start.LocalDateTime), "Full queue/draft/load/game trace");
    Observe(30, TimedActivity.ChampSelect);
    Observe(35, TimedActivity.Queue);
    Observe(39, TimedActivity.None);
    Equal(12.0, ledger.GetTotals(RankedQueue.SoloDuo, false, start.LocalDateTime).QueueSeconds, "Requeue after dodge is included");
    Equal(12.0, ledger.GetTotals(RankedQueue.SoloDuo, false, start.LocalDateTime).ChampSelectSeconds, "Dodged champion select time is retained");
    Observe(40, TimedActivity.Queue);
    Observe(45, TimedActivity.Queue, "account-b", RankedQueue.Flex);
    Observe(50, TimedActivity.None, "account-b", RankedQueue.Flex);
    Equal(5.0, ledger.GetTotals(RankedQueue.Flex, false, start.LocalDateTime).QueueSeconds, "Account switch does not leak time into the new queue");
    Observe(55, TimedActivity.Queue);
    ledger.Pause();
    Observe(60, TimedActivity.Queue);
    Observe(65, TimedActivity.None);
    Equal(17.0, ledger.GetTotals(RankedQueue.SoloDuo, false, start.LocalDateTime).QueueSeconds, "Client outage is not counted");
    Observe(70, TimedActivity.Loading);
    Observe(3670, TimedActivity.Loading);
    Observe(3675, TimedActivity.None);
    Equal(10.0, ledger.GetTotals(RankedQueue.SoloDuo, false, start.LocalDateTime).LoadingSeconds, "Sleep gap is discarded; resumed observation is counted");
    ledger.Save();
    var persisted = ledger.GetTotals(null, false, start.LocalDateTime);
    ledger = new WaitingTimeLedger(path);
    Observe(8000, TimedActivity.Queue);
    Equal(persisted, ledger.GetTotals(null, false, start.LocalDateTime), "App restart never backfills time while closed");
    Observe(8003, TimedActivity.None);
    Equal(persisted.QueueSeconds + 3, ledger.GetTotals(null, false, start.LocalDateTime).QueueSeconds, "New lifetime adds to saved history");

    var midnightLedger = new WaitingTimeLedger(Path.Combine(directory, "midnight.json"));
    var midnightStart = new DateTimeOffset(new DateTime(2026, 9, 25, 23, 59, 59, DateTimeKind.Local));
    midnightLedger.Observe(midnightStart, 0, "a", RankedQueue.RankedFives, TimedActivity.Queue);
    midnightLedger.Observe(midnightStart.AddSeconds(2), 2000, "a", RankedQueue.RankedFives, TimedActivity.None);
    Equal(1.0, midnightLedger.GetTotals(RankedQueue.RankedFives, true, new DateTime(2026, 9, 25)).QueueSeconds, "Midnight keeps the first day portion");
    Equal(1.0, midnightLedger.GetTotals(RankedQueue.RankedFives, true, new DateTime(2026, 9, 26)).QueueSeconds, "Midnight starts today's portion");
    midnightLedger.Observe(midnightStart.AddSeconds(4), 4000, "a", RankedQueue.RankedFives, TimedActivity.Queue);
    midnightLedger.Observe(midnightStart.AddHours(1), 5000, "a", RankedQueue.RankedFives, TimedActivity.None);
    Equal(2.0, midnightLedger.GetTotals(null, false, DateTime.Now).QueueSeconds, "Wall clock jumps cannot inflate history");

    var lateClock = new WaitingTimeLedger(Path.Combine(directory, "late-clock.json"));
    lateClock.RecordConfirmedGameStart(start, start.AddSeconds(35), "a", RankedQueue.SoloDuo, 15);
    Equal(20.0, lateClock.GetTotals(null, false, DateTime.Now).LoadingSeconds, "A delayed clock read subtracts time already spent playing");
    Equal(15.0, lateClock.GetTotals(null, false, DateTime.Now).InGameSeconds, "Confirmed elapsed gameplay is retained");
    lateClock.RecordConfirmedGameStart(start.AddMinutes(5), start.AddMinutes(5).AddSeconds(2), "a", RankedQueue.SoloDuo, 300);
    Equal(20.0, lateClock.GetTotals(null, false, DateTime.Now).LoadingSeconds, "Mid-game clock cannot produce negative or invented loading");

    string broken = Path.Combine(directory, "broken.json");
    File.WriteAllText(broken, "broken history");
    _ = new WaitingTimeLedger(broken);
    Equal("broken history", File.ReadAllText(Directory.GetFiles(directory, "broken.json.recovery-*").Single()), "Corrupt history is retained for recovery");
    checks += await UpdateChecks.RunAsync(directory);
    Console.WriteLine($"Passed {checks} behavioral checks.");
}
finally { Directory.Delete(directory, true); }
