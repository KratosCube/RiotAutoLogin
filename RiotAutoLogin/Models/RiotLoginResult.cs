namespace RiotAutoLogin.Models
{
    public enum RiotLoginStage
    {
        Preparing,
        LaunchingClient,
        WaitingForClient,
        Submitting,
        Completed
    }

    public sealed record RiotLoginProgress(RiotLoginStage Stage, string Message);

    public sealed record RiotLoginResult(bool Success, string Message)
    {
        public static RiotLoginResult Succeeded(string message) => new(true, message);
        public static RiotLoginResult Failed(string message) => new(false, message);
    }
}
