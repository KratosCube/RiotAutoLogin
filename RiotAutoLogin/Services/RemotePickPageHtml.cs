using System;
using System.IO;
using System.Text;

namespace RiotAutoLogin.Services
{
    public static class RemotePickPageHtml
    {
        private static readonly Lazy<string> Page = new(() =>
        {
            using Stream stream = typeof(RemotePickPageHtml).Assembly
                .GetManifestResourceStream("RiotAutoLogin.Services.RemotePickPage.html")
                ?? throw new InvalidOperationException("Remote Pick page resource is missing.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        });

        public static string Get() => Page.Value;
    }
}
