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
                var image = new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth), (int)Math.Ceiling(window.ActualHeight), 96, 96, PixelFormats.Pbgra32);
                image.Render(window);
                var png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(image));
                Directory.CreateDirectory("build");
                using var file = File.Create("build/account-grid-preview.png");
                png.Save(file);
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
            if (Element<Image>(0, "RankEmblem").IsVisible)
                throw new Exception("The Solo/Duo emblem remained on an unsynced Ranked 5s card.");
            accounts[0].ApplyRanks(new() { [RankedQueue.RankedFives] = new RankData { Tier = "EMERALD", Wins = 9 } });
            window.UpdateLayout();
            CheckEmblem(0, "EMERALD");
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
            Rect viewport = Bounds(viewer);
            viewport.Inflate(1, 1); // Allow layout rounding at non-integer display scales.
            if (card.IsEmpty || stats.Height < 12 || !viewport.Contains(card) || !card.Contains(stats) || card.Bottom > Bounds(summary).Top + 1)
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
}
