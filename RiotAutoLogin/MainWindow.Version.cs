using System;
using System.Reflection;

namespace RiotAutoLogin
{
    public partial class MainWindow
    {
        private void UpdateCurrentVersionDisplay()
        {
            try
            {
                Version? version = Assembly.GetExecutingAssembly().GetName().Version;
                txtCurrentVersion.Text = $"Current version: v{FormatAppVersion(version)}";
            }
            catch
            {
                txtCurrentVersion.Text = "Current version: unknown";
            }
        }

        private static string FormatAppVersion(Version? version)
        {
            if (version == null)
                return "unknown";

            if (version.Revision > 0)
                return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";

            if (version.Build > 0)
                return $"{version.Major}.{version.Minor}.{version.Build}";

            return $"{version.Major}.{version.Minor}";
        }
    }
}
