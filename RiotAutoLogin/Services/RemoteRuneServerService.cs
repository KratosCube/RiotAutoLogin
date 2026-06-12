using RiotAutoLogin.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RiotAutoLogin.Services
{
    public sealed class RemoteRuneServerService : IDisposable
    {
        public const int DefaultPort = 5056;

        private readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        private TcpListener? _listener;
        private CancellationTokenSource? _cts;
        private Task? _serverTask;

        public bool IsRunning { get; private set; }
        public int Port { get; private set; } = DefaultPort;

        public Task StartAsync(int port = DefaultPort)
        {
            if (IsRunning)
                return Task.CompletedTask;

            Port = port;
            _cts = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, Port);
            _listener.Start();
            IsRunning = true;
            _serverTask = Task.Run(() => AcceptLoopAsync(_cts.Token));
            Debug.WriteLine($"Remote Rune API server started on port {Port}");
            return Task.CompletedTask;
        }

        public void Stop()
        {
            if (!IsRunning)
                return;

            IsRunning = false;

            try
            {
                _cts?.Cancel();
                _listener?.Stop();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error stopping Remote Rune API server: {ex.Message}");
            }
            finally
            {
                _listener = null;
                _cts?.Dispose();
                _cts = null;
                _serverTask = null;
            }
        }

        private async Task AcceptLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (_listener == null)
                        break;

                    TcpClient client = await _listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (SocketException)
                {
                    if (!cancellationToken.IsCancellationRequested)
                        throw;
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Remote Rune API accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
        {
            await using NetworkStream stream = client.GetStream();
            using StreamReader reader = new(stream, Encoding.UTF8, leaveOpen: true);

            try
            {
                string? requestLine = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(requestLine))
                    return;

                string[] requestParts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
                if (requestParts.Length < 2)
                {
                    await WriteTextResponseAsync(stream, 400, "Bad Request", "Bad request", cancellationToken);
                    return;
                }

                string method = requestParts[0].ToUpperInvariant();
                string path = requestParts[1];
                int contentLength = 0;

                string? headerLine;
                while (!string.IsNullOrEmpty(headerLine = await reader.ReadLineAsync()))
                {
                    int separatorIndex = headerLine.IndexOf(':');
                    if (separatorIndex <= 0)
                        continue;

                    string name = headerLine[..separatorIndex].Trim();
                    string value = headerLine[(separatorIndex + 1)..].Trim();
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(value, out contentLength);
                }

                string body = string.Empty;
                if (contentLength > 0)
                {
                    char[] buffer = new char[contentLength];
                    int totalRead = 0;
                    while (totalRead < contentLength)
                    {
                        int read = await reader.ReadAsync(buffer, totalRead, contentLength - totalRead);
                        if (read == 0)
                            break;
                        totalRead += read;
                    }

                    body = new string(buffer, 0, totalRead);
                }

                if (method == "OPTIONS")
                {
                    await WriteTextResponseAsync(stream, 204, "No Content", string.Empty, cancellationToken);
                }
                else if (method == "GET" && path.StartsWith("/api/runes", StringComparison.OrdinalIgnoreCase))
                {
                    await WriteJsonResponseAsync(stream, 200, "OK", GetRunePages(), cancellationToken);
                }
                else if (method == "POST" && path.StartsWith("/api/rune-page", StringComparison.OrdinalIgnoreCase))
                {
                    RemotePickActionResult result = SelectRunePage(body);
                    await WriteJsonResponseAsync(stream, result.Success ? 200 : 400, result.Success ? "OK" : "Bad Request", result, cancellationToken);
                }
                else
                {
                    await WriteTextResponseAsync(stream, 404, "Not Found", "Not found", cancellationToken);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Remote Rune API request error: {ex.Message}");
                try
                {
                    await WriteTextResponseAsync(stream, 500, "Internal Server Error", "Internal server error", cancellationToken);
                }
                catch
                {
                    // Ignore disconnected clients.
                }
            }
            finally
            {
                client.Close();
            }
        }

        private object GetRunePages()
        {
            var pages = new List<RemoteRunePageDto>();
            RemoteRunePageDto? currentPage = null;

            if (!LCUService.CheckIfLeagueClientIsOpen())
                return new { pages, currentPage, message = "League Client is not running." };

            string[] pagesResult = LCUService.ClientRequest("GET", "lol-perks/v1/pages");
            if (pagesResult[0] != "200")
                return new { pages, currentPage, message = $"Could not load rune pages. Status: {pagesResult[0]}" };

            long currentPageId = GetCurrentRunePageId();

            try
            {
                using JsonDocument pagesDoc = JsonDocument.Parse(pagesResult[1]);
                if (pagesDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement page in pagesDoc.RootElement.EnumerateArray())
                    {
                        if (!page.TryGetProperty("id", out JsonElement idElement) || !TryGetLong(idElement, out long pageId))
                            continue;

                        string pageName = page.TryGetProperty("name", out JsonElement nameElement)
                            ? nameElement.GetString() ?? $"Rune Page {pageId}"
                            : $"Rune Page {pageId}";

                        bool isCurrent = currentPageId == pageId ||
                                         GetBoolProperty(page, "current") ||
                                         GetBoolProperty(page, "isCurrent");

                        var dto = new RemoteRunePageDto
                        {
                            Id = pageId,
                            Name = pageName,
                            IsCurrent = isCurrent,
                            IsEditable = GetBoolProperty(page, "isEditable"),
                            IsDeletable = GetBoolProperty(page, "isDeletable")
                        };

                        pages.Add(dto);
                        if (dto.IsCurrent)
                            currentPage = dto;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse rune pages: {ex.Message}");
                return new { pages, currentPage, message = "Could not parse rune pages." };
            }

            return new { pages, currentPage, message = string.Empty };
        }

        private RemotePickActionResult SelectRunePage(string body)
        {
            RemoteRunePageRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<RemoteRunePageRequest>(body, _jsonOptions);
            }
            catch
            {
                return new RemotePickActionResult { Success = false, Message = "Invalid rune page request." };
            }

            if (request == null || request.PageId <= 0)
                return new RemotePickActionResult { Success = false, Message = "Invalid rune page." };

            if (!LCUService.CheckIfLeagueClientIsOpen())
                return new RemotePickActionResult { Success = false, Message = "League Client is not running." };

            string[] pagesResult = LCUService.ClientRequest("GET", "lol-perks/v1/pages");
            if (pagesResult[0] != "200")
                return new RemotePickActionResult { Success = false, Message = $"Could not load rune pages. Status: {pagesResult[0]}" };

            string? selectedPageJson = null;
            try
            {
                using JsonDocument pagesDoc = JsonDocument.Parse(pagesResult[1]);
                if (pagesDoc.RootElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (JsonElement page in pagesDoc.RootElement.EnumerateArray())
                    {
                        if (page.TryGetProperty("id", out JsonElement idElement) &&
                            TryGetLong(idElement, out long pageId) &&
                            pageId == request.PageId)
                        {
                            selectedPageJson = page.GetRawText();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse rune pages for selection: {ex.Message}");
            }

            if (string.IsNullOrWhiteSpace(selectedPageJson))
                return new RemotePickActionResult { Success = false, Message = "Rune page was not found." };

            string[] result = LCUService.ClientRequest("POST", $"lol-perks/v1/pages/{request.PageId}/current");
            if (!result[0].StartsWith("2"))
                result = LCUService.ClientRequest("PUT", "lol-perks/v1/currentpage", selectedPageJson);

            if (!result[0].StartsWith("2"))
                result = LCUService.ClientRequest("PATCH", "lol-perks/v1/currentpage", selectedPageJson);

            bool success = result[0].StartsWith("2");
            return new RemotePickActionResult
            {
                Success = success,
                Message = success ? "Rune page selected." : $"League Client rejected rune page change. Status: {result[0]}"
            };
        }

        private static long GetCurrentRunePageId()
        {
            string[] currentPageResult = LCUService.ClientRequest("GET", "lol-perks/v1/currentpage");
            if (currentPageResult[0] != "200")
                return 0;

            try
            {
                using JsonDocument currentDoc = JsonDocument.Parse(currentPageResult[1]);
                if (currentDoc.RootElement.TryGetProperty("id", out JsonElement idElement) &&
                    TryGetLong(idElement, out long parsedCurrentPageId))
                {
                    return parsedCurrentPageId;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Could not parse current rune page: {ex.Message}");
            }

            return 0;
        }

        private static bool GetBoolProperty(JsonElement element, string propertyName)
        {
            return element.TryGetProperty(propertyName, out JsonElement valueElement) &&
                   valueElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
                   valueElement.GetBoolean();
        }

        private static bool TryGetLong(JsonElement element, out long value)
        {
            if (element.ValueKind == JsonValueKind.Number)
                return element.TryGetInt64(out value);

            if (element.ValueKind == JsonValueKind.String)
                return long.TryParse(element.GetString(), out value);

            value = 0;
            return false;
        }

        private async Task WriteJsonResponseAsync(NetworkStream stream, int statusCode, string statusText, object payload, CancellationToken cancellationToken)
        {
            string json = JsonSerializer.Serialize(payload, _jsonOptions);
            await WriteResponseAsync(stream, statusCode, statusText, "application/json; charset=utf-8", json, cancellationToken);
        }

        private async Task WriteTextResponseAsync(NetworkStream stream, int statusCode, string statusText, string text, CancellationToken cancellationToken)
        {
            await WriteResponseAsync(stream, statusCode, statusText, "text/plain; charset=utf-8", text, cancellationToken);
        }

        private static async Task WriteResponseAsync(NetworkStream stream, int statusCode, string statusText, string contentType, string content, CancellationToken cancellationToken)
        {
            byte[] bodyBytes = Encoding.UTF8.GetBytes(content);
            string headers =
                $"HTTP/1.1 {statusCode} {statusText}\r\n" +
                $"Content-Type: {contentType}\r\n" +
                "Access-Control-Allow-Origin: *\r\n" +
                "Access-Control-Allow-Methods: GET, POST, OPTIONS\r\n" +
                "Access-Control-Allow-Headers: Content-Type\r\n" +
                $"Content-Length: {bodyBytes.Length}\r\n" +
                "Cache-Control: no-store\r\n" +
                "Connection: close\r\n" +
                "\r\n";

            byte[] headerBytes = Encoding.ASCII.GetBytes(headers);
            await stream.WriteAsync(headerBytes, cancellationToken);
            await stream.WriteAsync(bodyBytes, cancellationToken);
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
