using Jint;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Telegram.Bot;

// 统一控制台代码页为UTF8，防止乱码
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// ES引擎字典
Dictionary<string, KeyValuePair<Engine, PluginInfo>> engines = new();

// 监听方法字典
Dictionary<string, List<KeyValuePair<string, Action<long, Dictionary<string, object>>>>> listenerFunc = new();  // WS用
Dictionary<string, List<KeyValuePair<string, Action<object>>>> tgListenerFunc = new();  // TG用

// 共享方法字典
Dictionary<string, object> exportFunc = new();

// 配置文件
Config config = new()
{
    ListenAddr = "127.0.0.1:8080",
    Endpoint = "/ws",
    Token = "",
    /*UsingTLS = false,
    CertFile = "cert.pem",
    KeyFile = "key.pem",*/
    BotToken = "",
    ProxyAddr = "",
    Language = "zh_Hans",
    DebugMode = false
};
if (!File.Exists("config.json"))
{
    File.WriteAllText("config.json", JsonSerializer.Serialize(config, new JsonSerializerOptions
    {
        WriteIndented = true
    }));
}
try
{
    config = JsonSerializer.Deserialize<Config>(File.ReadAllText("config.json"));
}
catch (Exception ex)
{
    Logger.Trace($"无法读取配置文件，已启用默认配置：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.ERROR);
}

// 语言包
Dictionary<string, string> language = new()
{
    ["twe.websocket.connected"] = "已连接到ws://%wsaddr%%endpoint%",
    ["twe.websocket.connectionfailed"] = "连接ws://%wsaddr%%endpoint%失败，将在5秒后重连",
    ["twe.websocket.connectionretry"] = "连接ws://%wsaddr%%endpoint%断开，将在5秒后重连",
    ["twe.websocket.receivefailed"] = "监听Websocket失败",
    ["twe.telegram.connected"] = "已连接到Telegram服务器",
    ["twe.telegram.connectionfailed"] = "连接到Telegram服务器失败，将在5秒后重连",
    ["twe.telegram.receivefailed"] = "监听Telegram失败",
    ["twe.plugin.loaded"] = "%name%已加载",
    ["twe.plugin.loadfailed"] = "%name%加载失败",
    ["twe.plugin.loadfinish"] = "已加载%count%个插件",
    ["twe.plugin.unloaded"] = "%name%已卸载",
    ["twe.plugin.unloadfailed"] = "%name%卸载失败",
    ["twe.plugin.apierror"] = "%name%抛出了异常",
    ["twe.plugin.listenerror"] = "%name%监听（插件%plugin%）抛出了异常",
    ["twe.plugin.list"] = "插件列表",
    ["twe.command.doesntexist"] = "不存在的命令"
};
if (!Directory.Exists("language"))
{
    _ = Directory.CreateDirectory("language");
}
if (!File.Exists($"language\\zh_Hans.json"))
{
    File.WriteAllText($"language\\zh_Hans.json", JsonSerializer.Serialize(language, new JsonSerializerOptions
    {
        WriteIndented = true
    }));
}
try
{
    language = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText($"language\\{config.Language}.json")) ?? language;
}
catch (Exception ex)
{
    Logger.Trace($"无法读取语言文件，已启用中文：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.ERROR);
}

if (!Directory.Exists("plugins"))
{
    _ = Directory.CreateDirectory("plugins");
}

// WebSocket连接
ClientWebSocket ws;
while (true)
{
    try
    {
        WebsocketConnect();
        break;
    }
    catch (Exception ex)
    {
        Logger.Trace($"{language["twe.websocket.connectionfailed"].Replace("%wsaddr%", config.ListenAddr).Replace("%endpoint%", config.Endpoint)}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.WARN);
        Thread.Sleep(5000);
    }
}

// Telegram连接
TelegramBotClient botClient;
while (true)
{
    try
    {
        botClient = new(config.BotToken, string.IsNullOrWhiteSpace(config.ProxyAddr) ? null : new HttpClient(new HttpClientHandler
        {
            Proxy = new WebProxy(config.ProxyAddr, true)
        }));
        botClient.TestApiAsync().Wait();
        Logger.Trace(language["twe.telegram.connected"]);
        break;
    }
    catch (Exception ex)
    {
        Logger.Trace($"{language["twe.telegram.connectionfailed"]}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.WARN);
        Thread.Sleep(5000);
    }
}

LoadPlugins();

// Telegram监听
botClient.StartReceiving((botClient1, update, cancellationToken) =>
{
    if (listenerFunc.ContainsKey($"{update.Type}"))
    {
        foreach (KeyValuePair<string, Action<object>> func in tgListenerFunc[$"tg.{update.Type}"])
        {
            try
            {
                func.Value(update);
            }
            catch (Exception ex)
            {
                Logger.Trace($"{language["twe.plugin.listenerror"].Replace("%name%", $"tg.{update.Type}").Replace("%plugin%", func.Key)}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.ERROR);
            }
        }
    }
}, (botClient2, exception, cancellationToken) =>
{
    Logger.Trace($"{language["twe.telegram.receivefailed"]}：{(config.DebugMode ? exception : exception.Message)}", Logger.LogLevel.ERROR);
});

// WebSocket监听
Task.Run(() =>
{
    while (true)
    {
        byte[] buffer = new byte[8192];
        try
        {
            ws.ReceiveAsync(buffer, default).Wait();
            string packStr = Encoding.UTF8.GetString(buffer).Replace("\0", string.Empty);
            if (config.DebugMode)
            {
                Logger.Trace(packStr, Logger.LogLevel.DEBUG);
            }
            PacketBase<Dictionary<string, object>> data = JsonSerializer.Deserialize<PacketBase<Dictionary<string, object>>>(packStr);
            if (listenerFunc.ContainsKey(data.Action))
            {
                foreach (KeyValuePair<string, Action<long, Dictionary<string, object>>> func in listenerFunc[$"ws.{data.Action}"])
                {
                    try
                    {
                        func.Value(data.PacketId, data.Params);
                    }
                    catch (Exception ex)
                    {
                        Logger.Trace($"{language["twe.plugin.listenerror"].Replace("%name%", $"ws.{data.Action}").Replace("%plugin%", func.Key)}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.ERROR);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            if (ws.State == WebSocketState.Open)
            {
                Logger.Trace($"{language["twe.websocket.receivefailed"]}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.ERROR);
                return;
            }
            UnloadPlugins();
            while (true)
            {
                try
                {
                    WebsocketConnect();
                    LoadPlugins();
                    break;
                }
                catch (Exception ex2)
                {
                    Logger.Trace($"{language["twe.websocket.connectionretry"].Replace("%wsaddr%", config.ListenAddr).Replace("%endpoint%", config.Endpoint)}：{(config.DebugMode ? ex2 : ex2.Message)}", Logger.LogLevel.WARN);
                    Thread.Sleep(5000);
                }
            }
        }
    }
});

// 控制台命令
while (true)
{
    string input = Console.ReadLine() ?? string.Empty;
    if (input.StartsWith("unload "))
    {
        try
        {
            GC.SuppressFinalize(engines[input[7..]]);
            _ = engines.Remove(input[7..]);
            Logger.Trace(language["twe.plugin.unloaded"].Replace("%name%", $"{input[7..]}"));
        }
        catch (Exception ex)
        {
            Logger.Trace($"{language["twe.plugin.unloadfailed"].Replace("%name%", $"{input[7..]}")}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.WARN);
        }
    }
    else
    {
        switch (input)
        {
            case "reload":
                UnloadPlugins();
                LoadPlugins();
                break;
            case "list":
                Logger.Trace($"{language["twe.plugin.list"]} [{engines.Count}]");
                foreach (KeyValuePair<string, KeyValuePair<Engine, PluginInfo>> engine in engines)
                {
                    Logger.Trace($"- {engine.Key} [{engine.Value.Value.version[0]}.{engine.Value.Value.version[1]}.{engine.Value.Value.version[2]}] （{engine.Value.Value.finename}）");
                    Logger.Trace($"  {engine.Value.Value.introduction}");
                }
                break;
            case "stop":
                UnloadPlugins();
                ws.CloseAsync(WebSocketCloseStatus.Empty, null, default);
                Environment.Exit(1);
                break;
            default:
                Logger.Trace($"{language["twe.command.doesntexist"]}：{input.Split(" ")[0]}", Logger.LogLevel.WARN);
                break;
        }
    }
}

// 加载插件
void LoadPlugins()
{
    foreach (FileInfo file in new DirectoryInfo("plugins").GetFiles())
    {
        if (file.Extension is not ".es" and not ".js")
        {
            continue;
        }
        Engine es = new();
        string pluginName = file.Name;
        PluginInfo info = new()
        {
            introduction = string.Empty,
            finename = file.Name,
            version = new[] { 1, 0, 0 }
        };
        // 注册API
        _ = es.SetValue("twe", new Dictionary<string, object>
        {
            ["registerPlugin"] = (string name, string introduction, int[] version) =>
            {
                pluginName = name;
                info.introduction = introduction;
                info.version = version;
            },
            ["log"] = (object message, int? level, int? type, string? path) =>
            {
                Logger.Trace(message, level == null ? Logger.LogLevel.INFO : (Logger.LogLevel)level, type == null ? Logger.LogType.OnlyConsole : (Logger.LogType)type, path);
            },
            ["export"] = (object func, string name) =>
            {
                try
                {
                    exportFunc.Add(name, func);
                }
                catch (Exception ex)
                {
                    Logger.Trace($"{language["twe.plugin.apierror"].Replace("%name%", $"{file.Name}")}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.WARN);
                }
            },
            ["import"] = (string name) =>
            {
                try
                {
                    _ = es.SetValue(name, exportFunc[name]);
                }
                catch (Exception ex)
                {
                    Logger.Trace($"{language["twe.plugin.apierror"].Replace("%name%", $"{file.Name}")}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.WARN);
                }
            },
            ["listPlugins"] = () =>
            {
                return engines.Keys;
            },
            ["eval"] = (string code) =>
            {
                _ = es.Execute(code);
            }// WIP
        });
        _ = es.SetValue("tg", new Dictionary<string, object>
        {
            ["sendMessage"] = (long chatid, string msg, int? type) =>
            {
                while (true)
                {
                    try
                    {
                        botClient.SendTextMessageAsync(chatid, msg, (Telegram.Bot.Types.Enums.ParseMode?)type).Wait();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Logger.Trace($"{language["twe.plugin.apierror"].Replace("%name%", $"{file.Name}")}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.WARN);
                    }
                }
            },
            ["bot"] = botClient.GetMeAsync().Result,
            ["listen"] = (string type, Action<object> func) =>
            {
                if (!tgListenerFunc.ContainsKey(type))
                {
                    tgListenerFunc[type] = new();
                }
                tgListenerFunc[type].Add(new KeyValuePair<string, Action<object>>(pluginName, func));
            }// WIP
        });
        _ = es.SetValue("ws", new Dictionary<string, object>
        {
            ["sendPack"] = (string action, Dictionary<string, object> @params) =>
            {
                long id = new Random().NextInt64();
                sendPack(new PacketBase<object>
                {
                    Action = action,
                    PacketId = id,
                    Params = @params
                });
                return id;
            },
            ["listen"] = (string type, Action<long, Dictionary<string, object>> func) =>
            {
                if (!listenerFunc.ContainsKey(type))
                {
                    listenerFunc[type] = new();
                }
                listenerFunc[type].Add(new KeyValuePair<string, Action<long, Dictionary<string, object>>>(pluginName, func));
            }// WIP
        });
        _ = es.SetValue("mc", new Dictionary<string, object>    // 为MC准备的方便API
        {
            ["runcmd"] = (string cmd) =>
            {
                long id = new Random().NextInt64();
                sendPack(new PacketBase<RuncmdRequest>
                {
                    Action = "RuncmdRequest",
                    PacketId = id,
                    Params = new RuncmdRequest
                    {
                        Command = cmd,
                    }
                });
                return id;
            },
            ["broadcast"] = (string message, int? type) =>
            {
                long id = new Random().NextInt64();
                sendPack(new PacketBase<BroadcastRequest>
                {
                    Action = "BroadcastRequest",
                    PacketId = id,
                    Params = new BroadcastRequest
                    {
                        Message = message,
                        MessageType = type ?? 0
                    }
                });
                return id;
            }
        });
        try
        {
            _ = es.Execute(File.ReadAllText(file.FullName));
            engines.Add(pluginName, new KeyValuePair<Engine, PluginInfo>(es, info));
            Logger.Trace(language["twe.plugin.loaded"].Replace("%name%", $"{pluginName}"));
        }
        catch (Exception ex)
        {
            Logger.Trace($"{language["twe.plugin.loadfailed"].Replace("%name%", $"{pluginName}")}：{(config.DebugMode ? ex : ex.Message)}", Logger.LogLevel.WARN);
            GC.SuppressFinalize(es);
        }
    }
    Logger.Trace(language["twe.plugin.loadfinish"].Replace("%count%", $"{engines.Count}"));
}

// 发WS包
void sendPack(object input)
{
    byte[] pack = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input));
    ws.SendAsync(pack, WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, default).AsTask().Wait();
}

void WebsocketConnect()
{
    ws = new();
    ws.ConnectAsync(new Uri($"ws://{config.ListenAddr}{config.Endpoint}"), default).Wait();
    long id = new Random().NextInt64();
    ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new PacketBase<LoginRequest>
    {
        Action = "LoginRequest",
        PacketId = id,
        Params = new LoginRequest
        {
            Password = config.Token
        }
    })), WebSocketMessageType.Text, true, default).Wait();
    byte[] buffer = new byte[8192];
    ws.ReceiveAsync(buffer, default).Wait();
    string packStr = Encoding.UTF8.GetString(buffer).Replace("\0", string.Empty);
    if (config.DebugMode)
    {
        Logger.Trace(packStr, Logger.LogLevel.DEBUG);
    }
    PacketBase<LoginResponse> data = JsonSerializer.Deserialize<PacketBase<LoginResponse>>(packStr);
    if (data.Action == "LoginResponse" && data.PacketId == id && data.Params.Success && data.Params.Message == string.Empty)
    {
        Logger.Trace(language["twe.websocket.connected"].Replace("%wsaddr%", config.ListenAddr).Replace("%endpoint%", config.Endpoint));
    }
    else
    {
        throw new Exception(data.Params.Message);
    }
}

void UnloadPlugins()
{
    foreach (KeyValuePair<string, KeyValuePair<Engine, PluginInfo>> engine in engines)
    {
        GC.SuppressFinalize(engine.Value.Key);
        Logger.Trace(language["twe.plugin.unloaded"].Replace("%name%", $"{engine.Key}"));
    }
    engines.Clear();
    listenerFunc.Clear();
    exportFunc.Clear();
}