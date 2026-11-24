using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace RiotAutoLogin.Converters;

public sealed class RankEmblemConverter : IValueConverter
{
    private readonly Dictionary<string, BitmapImage> _emblems = new();

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not RankData rank)
            return null;

        // Older saved accounts can have only a display label such as "GOLD IV (40 LP, 10W/11L)".
        string? tier = (string.IsNullOrWhiteSpace(rank.Tier) ? rank.LegacyLabel?.Split(' ', 2)[0] : rank.Tier)?.ToLowerInvariant();
        if (tier is not ("iron" or "bronze" or "silver" or "gold" or "platinum" or "emerald" or "diamond" or "master" or "grandmaster" or "challenger"))
            return null;

        if (!_emblems.TryGetValue(tier, out var emblem))
        {
            emblem = new BitmapImage(new Uri($"pack://application:,,,/RiotAutoLogin;component/resources/ranks/{tier}.png"));
            emblem.Freeze();
            _emblems.Add(tier, emblem);
        }
        return emblem;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
