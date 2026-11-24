using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace RiotAutoLogin.Models;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Account>))]
[JsonSerializable(typeof(HotkeySettings))]
internal partial class StartupJson : JsonSerializerContext { }
