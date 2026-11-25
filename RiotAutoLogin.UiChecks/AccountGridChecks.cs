using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using RiotAutoLogin;
using RiotAutoLogin.Models;
using RiotAutoLogin.Services;
using TabControl = System.Windows.Controls.TabControl;
using Image = System.Windows.Controls.Image;
using Size = System.Windows.Size;

internal static class AccountGridChecks
{
    public static void Run(MainWindow window, TabControl tabs)
    {
        var list = (ItemsControl)window.FindName("icLoginAccounts");
        var viewer = (ScrollViewer)window.FindName("svQuickLogin");
        var summary = (FrameworkElement)window.FindName("statsPanel");
        var previousItems = list.ItemsSource;
        var previousTab = tabs.SelectedItem;
        var previousSize = new Size(window.Width, window.Height);
        string[] names = ["Ashen Fox", "Night Orchard", "North Star", "Pocket Support", "Juniper", "River Ward", "Weekend Crew", "A longer summoner name"];
        string[] tiers = ["IRON", "BRONZE", "SILVER", "GOLD", "PLATINUM", "EMERALD", "DIAMOND", "MASTER", "GRANDMASTER", "CHALLENGER"];
        var accounts = Enumerable.Range(0, 20).Select(i => new Account
        {
            GameName = names[i % names.Length], TagLine = $"R{i + 1}", Region = i % 2 == 0 ? "euw1" : "eun1",
            QueueRanks = new() { [RankedQueue.SoloDuo] = new RankData
            {
                Tier = tiers[i % tiers.Length], Rank = i < 7 ? "II" : "", LeaguePoints = 38 + i,
                Wins = 24 + i, Losses = 16 + i
            } }
        }).ToList();
        for (int i = 0; i < 7; i++)
            accounts[i].ApplyOpponentRanks(RankedQueue.SoloDuo, new OpponentRankSummary
            {
                MeanRankPosition = 350 + i * 400, Games = 10, RankedOpponents = 45,
                TotalOpponents = 50, UpdatedUtc = DateTime.UtcNow
            });
        try
        {
            tabs.SelectedIndex = 0;
            var eight = accounts.Take(8).ToList();
            list.SetCurrentValue(ItemsControl.ItemsSourceProperty, eight);
            foreach (var size in new[] { previousSize, new Size(window.MinWidth, window.MinHeight) })
            {
                window.Width = size.Width;
                window.Height = size.Height;
                window.UpdateLayout();
                for (int i = 0; i < eight.Count; i++)
                    CheckVisibleCard(i);
            }

            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                UIService.UpdateTotalGameStats(window, eight, RankedQueue.SoloDuo);
                window.UpdateLayout();
                Capture(window, "account-grid-preview.png");
                foreach (var account in eight)
                {
                    account.AnnualCs.CompletedUntil = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    account.AnnualCs.YearStart = AnnualCs.StartOfYear(DateTimeOffset.UtcNow);
                    for (int game = 0; game < 120; game++)
                        account.AnnualCs.Matches["sample-" + game] = new CsMatch { Cs = 205, Seconds = 1740, Started = DateTimeOffset.UtcNow.AddDays(-game * 2).ToUnixTimeSeconds() };
                }
                var farm = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("btnStatsFarm");
                farm.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                UIService.UpdateAnnualCsStats(window, eight);
                if (((TextBlock)window.FindName("txtCsTotal")).Text == "—") throw new Exception("Annual CS preview did not bind.");
                if (((TextBlock)window.FindName("txtCsPeriod")).Text != $"Total CS · {DateTime.Now.Year}")
                    throw new Exception("Farming must identify the current calendar year.");
                Capture(window, "farm-statistics-preview.png");
                var results = (System.Windows.Controls.Primitives.ToggleButton)window.FindName("btnStatsResults");
                results.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
            }

            var twoRowHeight = Element<Border>(0, "AccountCard").ActualHeight;
            list.SetCurrentValue(ItemsControl.ItemsSourceProperty, accounts.Take(1).ToList());
            window.UpdateLayout();
            if (Math.Abs(Element<Border>(0, "AccountCard").ActualHeight - twoRowHeight) > 1)
                throw new Exception("A single account was stretched to fill the grid.");

            list.SetCurrentValue(ItemsControl.ItemsSourceProperty, accounts);
            window.UpdateLayout();
            for (int i = 0; i < tiers.Length; i++)
                CheckEmblem(i, tiers[i]);
            accounts[0].SelectedQueue = RankedQueue.RankedFives;
            accounts[0].RefreshRankDisplay();
            window.UpdateLayout();
            CheckOpponentRank(Element<ContentPresenter>(0, "OpponentRank"), "—");
            if (Element<Image>(0, "RankEmblem").IsVisible)
                throw new Exception("The Solo/Duo emblem remained on an unsynced Ranked 5s card.");
            accounts[0].ApplyRanks(new() { [RankedQueue.RankedFives] = new RankData { Tier = "EMERALD", Wins = 9 } });
            window.UpdateLayout();
            CheckEmblem(0, "EMERALD");
            accounts[0].ApplyOpponentRanks(RankedQueue.RankedFives, new OpponentRankSummary
            {
                MeanRankPosition = 2100, Games = 8, RankedOpponents = 40, TotalOpponents = 40, UpdatedUtc = DateTime.UtcNow
            });
            window.UpdateLayout();
            CheckOpponentRank(Element<ContentPresenter>(0, "OpponentRank"), "EMERALD III");
            CheckWinRate(Element<ContentPresenter>(0, "WinRate"), 1);
            accounts[0].ApplyRanks(new() { [RankedQueue.RankedFives] = new RankData { Tier = "EMERALD", Losses = 9 } });
            window.UpdateLayout();
            CheckWinRate(Element<ContentPresenter>(0, "WinRate"), 0);
            accounts[0].ApplyRanks(new() { [RankedQueue.RankedFives] = new RankData { Tier = "UNRANKED" } });
            window.UpdateLayout();
            CheckWinRate(Element<ContentPresenter>(0, "WinRate"), null);
            if (viewer.ScrollableHeight <= 0)
                throw new Exception("The overflow fixture did not create a scrollable account grid.");
            viewer.ScrollToBottom();
            window.UpdateLayout();
            CheckVisibleCard(accounts.Count - 1);
            Console.WriteLine("Both full rows fit at default and minimum size. All ten emblems load, queue changes update the badge and the last account scrolls above the footer.");
        }
        finally
        {
            list.SetCurrentValue(ItemsControl.ItemsSourceProperty, previousItems);
            tabs.SelectedItem = previousTab;
            window.Width = previousSize.Width;
            window.Height = previousSize.Height;
        }

        T Element<T>(int index, string name) where T : FrameworkElement
        {
            var presenter = (ContentPresenter)list.ItemContainerGenerator.ContainerFromIndex(index);
            return (T)list.ItemTemplate.FindName(name, presenter);
        }
        Rect Bounds(FrameworkElement element) => element.TransformToAncestor(window).TransformBounds(new Rect(element.RenderSize));

        void CheckVisibleCard(int index)
        {
            Rect card = Bounds(Element<Border>(index, "AccountCard"));
            Rect stats = Bounds(Element<Border>(index, "AccountStats"));
            Rect winRate = Bounds(Element<ContentPresenter>(index, "WinRate"));
            Rect opponents = Bounds(Element<ContentPresenter>(index, "OpponentRank"));
            Rect viewport = Bounds(viewer);
            viewport.Inflate(1, 1); // Allow layout rounding at non-integer display scales.
            if (card.IsEmpty || stats.Height < 12 || winRate.Height < 10 || !viewport.Contains(card) ||
                !card.Contains(stats) || !card.Contains(winRate) || !card.Contains(opponents) || card.Bottom > Bounds(summary).Top + 1)
                throw new Exception($"Account {index + 1} or its stats are clipped at {window.ActualWidth} × {window.ActualHeight}. Card {card}, stats {stats}, viewport {viewport}.");
        }
        void CheckEmblem(int index, string tier)
        {
            var image = Element<Image>(index, "RankEmblem");
            if (image.Visibility != Visibility.Visible || image.Source is not BitmapImage bitmap || bitmap.PixelWidth <= 0 ||
                !bitmap.UriSource.AbsolutePath.EndsWith($"/{tier.ToLowerInvariant()}.png", StringComparison.Ordinal))
                throw new Exception($"Missing or incorrect {tier} emblem.");
        }
    }

    public static void CheckOpponentRank(ContentPresenter presenter, string expected)
    {
        var label = (TextBlock)presenter.ContentTemplate.FindName("OpponentRankLabel", presenter);
        if (label.Text != expected)
            throw new Exception("The opponent rank display belongs to a different queue.");
    }

    public static void CheckWinRate(ContentPresenter presenter, double? expected)
    {
        var label = (TextBlock)presenter.ContentTemplate.FindName("WinRateLabel", presenter);
        var wins = (Border)presenter.ContentTemplate.FindName("Wins", presenter);
        string expectedLabel = expected.HasValue ? $"{expected.Value:0%} WR" : "—";
        if (label.Text != expectedLabel || Math.Abs(((ScaleTransform)wins.RenderTransform).ScaleX - (expected ?? 0)) > 0.001)
            throw new Exception($"Win-rate text and graphic disagree for {expectedLabel}.");
    }

    public static void Capture(Window window, string name)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true") return;
        // Loaded handlers and deferred templates must finish before capturing a newly opened page.
        window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        window.UpdateLayout();
        var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        image.Render(window);
        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(image));
        Directory.CreateDirectory("build");
        using var file = File.Create(Path.Combine("build", name));
        png.Save(file);
    }
}
