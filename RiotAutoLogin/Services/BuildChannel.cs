namespace RiotAutoLogin.Services;

public static class BuildChannel
{
#if DEVELOPMENT
    public static bool IsDevelopment => true;
    public const string DataDirectoryName = "RiotClientAutoLogin-Dev";
    public const string StartupSuffix = "-Dev";
#else
    public static bool IsDevelopment => false;
    public const string DataDirectoryName = "RiotClientAutoLogin";
    public const string StartupSuffix = "";
#endif
}
