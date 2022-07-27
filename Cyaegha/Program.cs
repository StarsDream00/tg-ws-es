using Jint;
using Jint.Runtime;
using NLog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using File = System.IO.File;

// 统一控制台代码页为UTF8，防止乱码
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;
Console.ResetColor();

// ES引擎字典
Dictionary<string, KeyValuePair<Engine, PluginInfo>> engines = new();

// 监听方法字典
Dictionary<string, List<KeyValuePair<string, Action<long, Dictionary<string, object>>>>> wsListenerFunc = new();  // WS用
Dictionary<string, List<KeyValuePair<string, Action<Update>>>> tgListenerFunc = new();  // TG用

// 共享方法字典
Dictionary<string, object> exportFunc = new();

// 日志
Logger logger = LogManager.GetCurrentClassLogger();

// 配置文件
Config config = new() {
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
if (!File.Exists("config.json")) {
    File.WriteAllText("config.json", JsonSerializer.Serialize(config, new JsonSerializerOptions {
        WriteIndented = true
    }));
}
try {
    config = JsonSerializer.Deserialize<Config>(File.ReadAllText("config.json"));
} catch (Exception ex) {
    logger.Error($"无法读取配置文件，已启用默认配置：{(config.DebugMode ? ex : ex.Message)}");
}

// 语言包
Dictionary<string, string> language = new() {
    ["cyaegha.websocket.connected"] = "已连接到ws://%wsaddr%%endpoint%",
    ["cyaegha.websocket.connectionfailed"] = "连接ws://%wsaddr%%endpoint%失败，将在5秒后重连",
    ["cyaegha.websocket.connectionretry"] = "连接ws://%wsaddr%%endpoint%断开，将在5秒后重连",
    ["cyaegha.websocket.receivefailed"] = "监听Websocket失败",
    ["cyaegha.telegram.connected"] = "已连接到Telegram服务器",
    ["cyaegha.telegram.connectionfailed"] = "连接到Telegram服务器失败，将在5秒后重连",
    ["cyaegha.telegram.receivefailed"] = "监听Telegram失败",
    ["cyaegha.plugin.loaded"] = "%name%已加载",
    ["cyaegha.plugin.loadfailed"] = "%name%加载失败",
    ["cyaegha.plugin.loadfinish"] = "已加载%count%个插件",
    ["cyaegha.plugin.unloaded"] = "%name%已卸载",
    ["cyaegha.plugin.unloadfailed"] = "%name%卸载失败",
    ["cyaegha.plugin.apierror"] = "%name%抛出了异常",
    ["cyaegha.plugin.listenerror"] = "%name%监听（插件%plugin%）抛出了异常",
    ["cyaegha.plugin.list"] = "插件列表",
    ["cyaegha.js.error"] = "%message%（位于%location%）",
    ["cyaegha.command.doesntexist"] = "不存在的命令"
};
if (!Directory.Exists("language")) {
    _ = Directory.CreateDirectory("language");
}
if (!File.Exists($"language\\zh_Hans.json")) {
    File.WriteAllText($"language\\zh_Hans.json", JsonSerializer.Serialize(language, new JsonSerializerOptions {
        WriteIndented = true
    }));
}
try {
    language = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText($"language\\{config.Language}.json")) ?? language;
} catch (Exception ex) {
    logger.Error($"无法读取语言文件，已启用中文：{(config.DebugMode ? ex : ex.Message)}");
}

if (!Directory.Exists("plugins")) {
    _ = Directory.CreateDirectory("plugins");
}

// WebSocket连接
ClientWebSocket ws;
while (true) {
    try {
        WebsocketConnect();
        break;
    } catch (Exception ex) {
        logger.Warn($"{language["cyaegha.websocket.connectionfailed"].Replace("%wsaddr%", config.ListenAddr).Replace("%endpoint%", config.Endpoint)}：{(config.DebugMode ? ex : ex.Message)}");
        Thread.Sleep(5000);
    }
}

// Telegram连接
TelegramBotClient botClient;
while (true) {
    try {
        botClient = new(config.BotToken, string.IsNullOrWhiteSpace(config.ProxyAddr) ? null : new HttpClient(new HttpClientHandler {
            Proxy = new WebProxy(config.ProxyAddr, true)
        }));
        botClient.TestApiAsync().Wait();
        logger.Info(language["cyaegha.telegram.connected"]);
        break;
    } catch (Exception ex) {
        logger.Warn($"{language["cyaegha.telegram.connectionfailed"]}：{(config.DebugMode ? ex : ex.Message)}");
        Thread.Sleep(5000);
    }
}

LoadPlugins();

// Telegram监听
botClient.StartReceiving((botClient1, update, cancellationToken) => {
    if (tgListenerFunc.ContainsKey($"{update.Type}")) {
        foreach (KeyValuePair<string, Action<Update>> func in tgListenerFunc[$"{update.Type}"]) {
            try {
                func.Value(update);
            } catch (Exception ex) {
                logger.Error($"{language["cyaegha.plugin.listenerror"].Replace("%name%", $"tg.{update.Type}").Replace("%plugin%", func.Key)}：{(ex.GetType() == typeof(JavaScriptException) ? language["cyaegha.js.error"].Replace("%message%", config.DebugMode ? $"{ex}" : ex.Message).Replace("%location%", $"{((JavaScriptException)ex).Location}") : (config.DebugMode ? ex : ex.Message))}");
            }
        }
    }
}, (botClient2, exception, cancellationToken) => {
    logger.Error($"{language["cyaegha.telegram.receivefailed"]}：{(config.DebugMode ? exception : exception.Message)}");
});

// WebSocket监听
Task.Run(() => {
    while (true) {
        byte[] buffer = new byte[8192];
        try {
            ws.ReceiveAsync(buffer, default).Wait();
            string packStr = Encoding.UTF8.GetString(buffer).Replace("\0", default);
            if (config.DebugMode) {
                logger.Debug(packStr);
            }
            PacketBase<Dictionary<string, object>> data = JsonSerializer.Deserialize<PacketBase<Dictionary<string, object>>>(packStr);
            if (wsListenerFunc.ContainsKey(data.Action)) {
                foreach (KeyValuePair<string, Action<long, Dictionary<string, object>>> func in wsListenerFunc[data.Action]) {
                    try {
                        func.Value(data.PacketId, data.Params);
                    } catch (Exception ex) {
                        logger.Error($"{language["cyaegha.plugin.listenerror"].Replace("%name%", $"ws.{data.Action}").Replace("%plugin%", func.Key)}：{(ex.GetType() == typeof(JavaScriptException) ? language["cyaegha.js.error"].Replace("%message%", config.DebugMode ? $"{ex}" : ex.Message).Replace("%location%", $"{((JavaScriptException)ex).Location}") : (config.DebugMode ? ex : ex.Message))}");
                    }
                }
            }
        } catch (Exception ex) {
            if (ws.State is WebSocketState.Open) {
                logger.Error($"{language["cyaegha.websocket.receivefailed"]}：{(config.DebugMode ? ex : ex.Message)}");
                continue;
            }
            UnloadPlugins();
            while (true) {
                try {
                    WebsocketConnect();
                    LoadPlugins();
                    break;
                } catch (Exception ex2) {
                    logger.Warn($"{language["cyaegha.websocket.connectionretry"].Replace("%wsaddr%", config.ListenAddr).Replace("%endpoint%", config.Endpoint)}：{(config.DebugMode ? ex2 : ex2.Message)}");
                    Thread.Sleep(5000);
                }
            }
        }
    }
});

// 控制台命令
while (true) {
    string input = Console.ReadLine() ?? default;
    if (input.StartsWith("unload ")) {
        try {
            GC.SuppressFinalize(engines[input[7..]]);
            _ = engines.Remove(input[7..]);
            logger.Info(language["cyaegha.plugin.unloaded"].Replace("%name%", input[7..]));
        } catch (Exception ex) {
            logger.Warn($"{language["cyaegha.plugin.unloadfailed"].Replace("%name%", input[7..])}：{(config.DebugMode ? ex : ex.Message)}");
        }
    } else {
        switch (input) {
            case "reload":
                UnloadPlugins();
                LoadPlugins();
                break;
            case "list":
                logger.Info($"{language["cyaegha.plugin.list"]} [{engines.Count}]");
                foreach (KeyValuePair<string, KeyValuePair<Engine, PluginInfo>> engine in engines) {
                    logger.Info($"- {engine.Key} [{engine.Value.Value.version[0]}.{engine.Value.Value.version[1]}.{engine.Value.Value.version[2]}] （{engine.Value.Value.finename}）");
                    logger.Info($"  {engine.Value.Value.introduction}");
                }
                break;
            case "stop":
                UnloadPlugins();
                _ = ws.CloseAsync(default, default, default);
                Environment.Exit(1);
                break;
            default:
                logger.Warn($"{language["cyaegha.command.doesntexist"]}：{input.Split(" ")[0]}");
                break;
        }
    }
}

// 加载插件
void LoadPlugins() {
    foreach (FileInfo file in new DirectoryInfo("plugins").GetFiles()) {
        if (file.Extension is not ".js") {
            continue;
        }
        Engine es = new();
        string pluginName = file.Name;
        PluginInfo info = new() {
            introduction = default,
            finename = file.Name,
            version = new() { 1, 0, 0 }
        };
        // 注册API
        _ = es.SetValue("cyaegha", new Dictionary<string, object> {
            ["registerPlugin"] = (string name, string introduction, int[] version) => {
                pluginName = name;
                info.introduction = introduction;
                info.version = new(version);
            },
            ["logger"] = LogManager.GetCurrentClassLogger(),
            ["export"] = (object func, string name) => {
                try {
                    exportFunc.Add(name, func);
                } catch (Exception ex) {
                    logger.Warn($"{language["cyaegha.plugin.apierror"].Replace("%name%", file.Name)}：{(config.DebugMode ? ex : ex.Message)}");
                }
            },
            ["import"] = (string name) => {
                try {
                    _ = es.SetValue(name, exportFunc[name]);
                } catch (Exception ex) {
                    logger.Warn($"{language["cyaegha.plugin.apierror"].Replace("%name%", file.Name)}：{(config.DebugMode ? ex : ex.Message)}");
                }
            },
            ["listPlugins"] = () => {
                return engines.Keys;
            },
            ["eval"] = (string code) => {
                _ = es.Execute(code);
            }// WIP
        });
        _ = es.SetValue("tg", new Dictionary<string, object> {
            ["sendMessage"] = (long chatid, string msg, int? type) => {
                while (true) {
                    try {
                        botClient.SendTextMessageAsync(chatid, msg, (Telegram.Bot.Types.Enums.ParseMode?)type).Wait();
                        break;
                    } catch (AggregateException ex) {
                        bool br = false;
                        foreach (Exception t in ex.InnerExceptions) {
                            if (t.GetType() == typeof(ApiRequestException)) {
                                br = true;
                            }
                            logger.Warn($"{language["cyaegha.plugin.apierror"].Replace("%name%", file.Name)}：{(config.DebugMode ? t : t.Message)}");
                        }
                        if (br) {
                            break;
                        }
                    } catch (Exception ex) {
                        logger.Warn($"{language["cyaegha.plugin.apierror"].Replace("%name%", file.Name)}：{(config.DebugMode ? ex : ex.Message)}");
                        if (ex.GetType() == typeof(ApiRequestException)) {
                            break;
                        }
                    }
                }
            },
            ["bot"] = botClient.GetMeAsync().Result,
            ["listen"] = (string type, Action<Update> func) => {
                if (!tgListenerFunc.ContainsKey(type)) {
                    tgListenerFunc[type] = new();
                }
                tgListenerFunc[type].Add(new KeyValuePair<string, Action<Update>>(pluginName, func));
            }// WIP
        });
        _ = es.SetValue("ws", new Dictionary<string, object> {
            ["sendPack"] = (string action, Dictionary<string, object> @params) => {
                long id = new Random().NextInt64();
                sendPack(new PacketBase<Dictionary<string, object>> {
                    Action = action,
                    PacketId = id,
                    Params = @params
                });
                return id;
            },
            ["listen"] = (string type, Action<long, Dictionary<string, object>> func) => {
                if (!wsListenerFunc.ContainsKey(type)) {
                    wsListenerFunc[type] = new();
                }
                wsListenerFunc[type].Add(new KeyValuePair<string, Action<long, Dictionary<string, object>>>(pluginName, func));
            }// WIP
        });
        _ = es.SetValue("mc", new Dictionary<string, object>    // 为MC准备的方便API
        {
            ["runcmd"] = (string cmd) => {
                long id = new Random().NextInt64();
                sendPack(new PacketBase<RuncmdRequest> {
                    Action = "RuncmdRequest",
                    PacketId = id,
                    Params = new RuncmdRequest {
                        Command = cmd,
                    }
                });
                return id;
            },
            ["broadcast"] = (string message, int? type) => {
                long id = new Random().NextInt64();
                sendPack(new PacketBase<BroadcastRequest> {
                    Action = "BroadcastRequest",
                    PacketId = id,
                    Params = new BroadcastRequest {
                        Message = message,
                        MessageType = type ?? 0
                    }
                });
                return id;
            }
        });
        try {
            _ = es.Execute(File.ReadAllText(file.FullName));
            engines.Add(pluginName, new KeyValuePair<Engine, PluginInfo>(es, info));
            logger.Info(language["cyaegha.plugin.loaded"].Replace("%name%", pluginName));
        } catch (Exception ex) {
            logger.Warn($"{language["cyaegha.plugin.loadfailed"].Replace("%name%", pluginName)}：{(ex.GetType() == typeof(JavaScriptException) ? language["cyaegha.js.error"].Replace("%message%", config.DebugMode ? $"{ex}" : ex.Message).Replace("%location%", $"{((JavaScriptException)ex).Location}") : (config.DebugMode ? ex : ex.Message))}");
            GC.SuppressFinalize(es);
        }
    }
    logger.Info(language["cyaegha.plugin.loadfinish"].Replace("%count%", $"{engines.Count}"));
}

// 发WS包
void sendPack(object input) {
    string packStr = JsonSerializer.Serialize(input);
    logger.Debug(packStr);
    ws.SendAsync(Encoding.UTF8.GetBytes(packStr), WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, default).AsTask().Wait();
}

void WebsocketConnect() {
    ws = new();
    ws.ConnectAsync(new Uri($"ws://{config.ListenAddr}{config.Endpoint}"), default).Wait();
    long id = new Random().NextInt64();
    ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new PacketBase<LoginRequest> {
        Action = "LoginRequest",
        PacketId = id,
        Params = new LoginRequest {
            Password = config.Token
        }
    })), WebSocketMessageType.Text, true, default).Wait();
    byte[] buffer = new byte[8192];
    ws.ReceiveAsync(buffer, default).Wait();
    string packStr = Encoding.UTF8.GetString(buffer).Replace("\0", default);
    if (config.DebugMode) {
        logger.Debug(packStr);
    }
    PacketBase<LoginResponse> data = JsonSerializer.Deserialize<PacketBase<LoginResponse>>(packStr);
    if (data.Action is "LoginResponse" && data.PacketId == id && data.Params.Success && string.IsNullOrWhiteSpace(data.Params.Message)) {
        logger.Info(language["cyaegha.websocket.connected"].Replace("%wsaddr%", config.ListenAddr).Replace("%endpoint%", config.Endpoint));
    } else {
        throw new Exception(data.Params.Message);
    }
}

void UnloadPlugins() {
    foreach (KeyValuePair<string, KeyValuePair<Engine, PluginInfo>> engine in engines) {
        GC.SuppressFinalize(engine.Value.Key);
        logger.Info(language["cyaegha.plugin.unloaded"].Replace("%name%", engine.Key));
    }
    engines.Clear();
    wsListenerFunc.Clear();
    tgListenerFunc.Clear();
    exportFunc.Clear();
}