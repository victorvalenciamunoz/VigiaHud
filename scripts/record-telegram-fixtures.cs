// Records real Bot API answers as test fixtures (DECISIONS 016), with every personal value replaced.
//
//   dotnet run scripts/record-telegram-fixtures.cs -- <path to a test JPEG>
//
// Before running: the bot token in the user secrets with id "vigiahud", outside the repo:
//
//   dotnet user-secrets set "Telegram:Token" (Read-Host 'Token') --id vigiahud
//
// and three messages sent to the bot from the owner's Telegram: "/start", "/foto" and a sticker (something that is
// not text). The script sends the owner one text and one photo, writes the fixtures to
// tests/VigiaHud.Core.Tests/Fixtures/Telegram, and saves the owner's chat id as "Telegram:ChatId" in the same user
// secrets for later steps. It never prints the token or the chat id.

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

const long FakeOwner = 111_222_333;
const long FakeBot = 1_234_567_890;
const string FakeBotUser = "VigiaHudPruebaBot";
var outDir = Path.Combine("tests", "VigiaHud.Core.Tests", "Fixtures", "Telegram");

// The user secrets store of "dotnet user-secrets --id vigiahud": a flat JSON file in the user's profile.
var secretsPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "UserSecrets", "vigiahud", "secrets.json");
var secrets = File.Exists(secretsPath) ? JsonNode.Parse(await File.ReadAllTextAsync(secretsPath))!.AsObject() : [];
var token = secrets["Telegram:Token"]?.GetValue<string>();
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Falta el token: dotnet user-secrets set \"Telegram:Token\" (Read-Host 'Token') --id vigiahud");
    return 1;
}

if (args.Length < 1 || !File.Exists(args[0]))
{
    Console.Error.WriteLine("Uso: dotnet run scripts/record-telegram-fixtures.cs -- <foto de prueba .jpg>");
    return 1;
}

if (!Directory.Exists("tests"))
{
    Console.Error.WriteLine("Ejecútalo desde la raíz del repositorio.");
    return 1;
}

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

async Task<string> Post(string method, HttpContent content, string botToken)
{
    using var response = await http.PostAsync($"https://api.telegram.org/bot{botToken}/{method}", content);
    return await response.Content.ReadAsStringAsync();
}

StringContent Json(JsonObject body) => new(body.ToJsonString(), Encoding.UTF8, "application/json");

JsonObject Poll(long? offset, int timeout)
{
    var body = new JsonObject { ["timeout"] = timeout, ["allowed_updates"] = new JsonArray("message") };
    if (offset is { } value)
    {
        body["offset"] = value;
    }

    return body;
}

// Who is who, to know what to hide.
var me = JsonNode.Parse(await Post("getMe", Json([]), token))!;
if (me["ok"]?.GetValue<bool>() != true)
{
    Console.Error.WriteLine($"getMe falló: {me["description"]}");
    return 1;
}

var botId = me["result"]!["id"]!.GetValue<long>();
var botUser = me["result"]!["username"]!.GetValue<string>();

var updatesText = await Post("getUpdates", Json(Poll(null, 0)), token);
var updates = JsonNode.Parse(updatesText)!["result"]!.AsArray();
var messages = updates.Select(u => u!["message"]).Where(m => m is not null).ToList();
var owner = messages.FirstOrDefault(m => m!["chat"]!["type"]!.GetValue<string>() == "private");
if (owner is null || !messages.Any(m => m!["text"] is not null) || !messages.Any(m => m!["text"] is null))
{
    var texts = messages.Count(m => m!["text"] is not null);
    Console.Error.WriteLine(
        $"El bot tiene {texts} mensajes de texto y {messages.Count - texts} que no son texto. " +
        "Hacen falta /start, /foto y una pegatina (un emoji suelto llega como texto). Mándalos y repite.");
    return 1;
}

var ownerId = owner["chat"]!["id"]!.GetValue<long>();
var personal = new HashSet<string>(StringComparer.Ordinal) { token, ownerId.ToString(), botId.ToString(), botUser };
foreach (var m in messages)
{
    foreach (var key in new[] { "first_name", "last_name", "username" })
    {
        if (m!["from"]?[key]?.GetValue<string>() is { Length: > 2 } value)
        {
            personal.Add(value);
        }
    }
}

var lastUpdate = updates.Max(u => u!["update_id"]!.GetValue<long>());

var sendText = await Post("sendMessage", Json(new JsonObject
{
    ["chat_id"] = ownerId,
    ["text"] = "Prueba de VigíaHud: estoy grabando respuestas de Telegram para los tests. Puedes ignorar este mensaje.",
}), token);

using var photo = new MultipartFormDataContent
{
    { new StringContent(ownerId.ToString()), "chat_id" },
    { new StringContent("Foto de prueba.", Encoding.UTF8), "caption" },
};
var jpeg = new ByteArrayContent(await File.ReadAllBytesAsync(args[0]));
jpeg.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
photo.Add(jpeg, "photo", "foto.jpg");
var sendPhoto = await Post("sendPhoto", photo, token);

// Confirms what was read, and shows what "nothing new" looks like.
var emptyText = await Post("getUpdates", Json(Poll(lastUpdate + 1, 0)), token);

// Two polls at once: Telegram ends one of them with 409 Conflict.
var longPoll = Post("getUpdates", Json(Poll(lastUpdate + 1, 5)), token);
await Task.Delay(1000);
var shortPoll = await Post("getUpdates", Json(Poll(lastUpdate + 1, 0)), token);
var conflict = new[] { await longPoll, shortPoll }.FirstOrDefault(t => t.Contains("\"error_code\":409", StringComparison.Ordinal));

var unauthorized = await Post("getMe", Json([]), "123456789:AAinvalid-token-for-the-fixture");

string Clean(string raw)
{
    var node = JsonNode.Parse(raw)!;
    Walk(node);
    return node.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
}

void Walk(JsonNode? node)
{
    switch (node)
    {
        case JsonArray array:
            foreach (var item in array)
            {
                Walk(item);
            }

            break;
        case JsonObject obj:
            var isBot = obj["id"] is JsonValue id && id.TryGetValue<long>(out var value) && value == botId;
            foreach (var key in obj.Select(p => p.Key).ToList())
            {
                switch (key)
                {
                    case "id" when obj[key] is JsonValue v && v.TryGetValue<long>(out var n):
                        obj[key] = n == ownerId ? FakeOwner : n == botId ? FakeBot : n;
                        break;
                    case "first_name":
                        obj[key] = isBot ? "VigíaHud" : "Dueño";
                        break;
                    case "last_name":
                        obj.Remove(key);
                        break;
                    case "username":
                        if (isBot)
                        {
                            obj[key] = FakeBotUser;
                        }
                        else
                        {
                            obj.Remove(key);
                        }

                        break;
                    case "file_id":
                        obj[key] = "FILE_ID";
                        break;
                    case "file_unique_id":
                        obj[key] = "FILE_UNIQUE_ID";
                        break;
                    case "set_name":
                        obj[key] = "pegatinas";
                        break;
                    default:
                        Walk(obj[key]);
                        break;
                }
            }

            break;
    }
}

var fixtures = new Dictionary<string, string?>
{
    ["getUpdates-messages.json"] = updatesText,
    ["getUpdates-empty.json"] = emptyText,
    ["sendMessage-ok.json"] = sendText,
    ["sendPhoto-ok.json"] = sendPhoto,
    ["error-409-conflict.json"] = conflict,
    ["error-401-unauthorized.json"] = unauthorized,
};

Directory.CreateDirectory(outDir);
foreach (var (name, raw) in fixtures)
{
    if (raw is null)
    {
        Console.WriteLine($"  {name}: no se obtuvo (sin conflicto esta vez); repite si hace falta.");
        continue;
    }

    var clean = Clean(raw);
    var leak = personal.FirstOrDefault(p => clean.Contains(p, StringComparison.Ordinal));
    if (leak is not null)
    {
        Console.Error.WriteLine($"  {name}: queda un dato personal tras limpiar; no se guarda. Revisa el script.");
        return 1;
    }

    await File.WriteAllTextAsync(Path.Combine(outDir, name), clean + "\n");
    Console.WriteLine($"  {name}: guardado");
}

secrets["Telegram:ChatId"] = ownerId.ToString();
await File.WriteAllTextAsync(secretsPath, secrets.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Chat ID del dueño guardado en los secretos de usuario como Telegram:ChatId (no se muestra).");
return 0;
