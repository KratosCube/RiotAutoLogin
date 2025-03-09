using System;
using System.Globalization;
using System.Windows.Data;

namespace RiotAutoLogin.Converters
{
    public class RankInfoConverter : IValueConverter
    {
        // Convert: Vrátí pouze část řetězce před první závorkou (pokud existuje)
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string rankInfo = value as string;
            if (!string.IsNullOrEmpty(rankInfo))
            {
                int idx = rankInfo.IndexOf('(');
                if (idx > 0)
                {
                    return rankInfo.Substring(0, idx).Trim();
                }
            }
            return rankInfo;
        }

        // ConvertBack není potřeba
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
