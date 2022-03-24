﻿using Jint;
using System.Text.Json;
using System.Net;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Types;
using File = System.IO.File;

// 统一控制台输入输出代码页（UTF8），防止乱码
Console.InputEncoding = Encoding.UTF8;
Console.OutputEncoding = Encoding.UTF8;

// ES引擎字典
Dictionary<string, KeyValuePair<Engine, PluginInfo>> engines = new();

// 监听方法字典
Dictionary<string, List<Action<object>>> listenerFunc = new();

// 共享方法字典
Dictionary<string, object> exportFunc = new();

// 通用ID
long id = 0;

// 配置文件
Config config = new()
{
    wsaddr = "127.0.0.1:8800",
    endpoint = "/mc",
    encrypt = "aes_cbc_pkcs7padding",
    wspasswd = "passwd",
    token = "YOUR_ACCESS_TOKEN_HERE",
    proxyaddr = "",
    language = "zh_Hans",
    debugmode = false
};
if (!File.Exists("config.json"))
{
    File.WriteAllText("config.json", JsonSerializer.Serialize(config, new JsonSerializerOptions
    {
        WriteIndented = true,
    }));
}
try
{
    config = JsonSerializer.Deserialize<Config>(File.ReadAllText("config.json"));
}
catch (Exception ex)
{
    Logger.Trace($"无法读取配置文件，已启用默认配置：{ex}", Logger.LogLevel.ERROR);
}

// 解析AES密钥&向量
StringBuilder sb = new();
byte[] d = MD5.Create().ComputeHash(Encoding.UTF8.GetBytes(config.wspasswd));
foreach (byte b in d)
{
    sb.Append($"{b:X2}");
}
string key_iv = $"{sb}";
byte[] key = Encoding.UTF8.GetBytes(key_iv[..16]);
byte[] iv = Encoding.UTF8.GetBytes(key_iv[16..]);
Aes aes = Aes.Create();
aes.Key = key;

// 语言包
Dictionary<string, string> language = new()
{
    ["twe.websocket.connected"] = "已连接到ws://%wsaddr%%endpoint%",
    ["twe.websocket.connectionfailed"] = "连接ws://%wsaddr%%endpoint%失败，将在5秒后重试",
    ["twe.websocket.receivefailed"] = "接收WS包失败",
    ["twe.telegram.connected"] = "已连接到Telegram服务器",
    ["twe.telegram.connectionfailed"] = "连接到Telegram服务器失败，将在5秒后重试",
    ["twe.plugin.loaded"] = "%name%已加载",
    ["twe.plugin.loadfailed"] = "%name%加载失败",
    ["twe.plugin.loadfinish"] = "已加载%count%个插件",
    ["twe.plugin.unloaded"] = "%name%已卸载",
    ["twe.plugin.unloadfailed"] = "%name%卸载失败",
    ["twe.plugin.apierror"] = "%name%抛出了异常"
};
if (!Directory.Exists("language"))
{
    Directory.CreateDirectory("language");
}
if (!File.Exists($"language\\zh_Hans.json"))
{
    File.WriteAllText($"language\\zh_Hans.json", JsonSerializer.Serialize(language, new JsonSerializerOptions
    {
        WriteIndented = true,
    }));
}
try
{
    language = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText($"language\\{config.language}.json"));
}
catch (Exception ex)
{
    Logger.Trace($"无法读取语言文件，已启用中文：{ex}", Logger.LogLevel.ERROR);
}

if (!Directory.Exists("plugins"))
{
    Directory.CreateDirectory("plugins");
}

// WebSocket连接
ClientWebSocket ws;
while (true)
{
    try
    {
        ws = new();
        ws.ConnectAsync(new Uri($"ws://{config.wsaddr}{config.endpoint}"), default).Wait();
        Logger.Trace(language["twe.websocket.connected"].Replace("%wsaddr%", config.wsaddr).Replace("%endpoint%", config.endpoint));
        break;
    }
    catch (Exception ex)
    {
        Logger.Trace($"{language["twe.websocket.connectionfailed"].Replace("%wsaddr%", config.wsaddr).Replace("%endpoint%", config.endpoint)}：：{ex}", Logger.LogLevel.WARN);
        Thread.Sleep(5000);
    }
}

// Telegram连接
TelegramBotClient botClient;
while (true)
{
    try
    {
        botClient = new(config.token, string.IsNullOrWhiteSpace(config.proxyaddr) ? null : new HttpClient(new HttpClientHandler
        {
            Proxy = new WebProxy(config.proxyaddr, true)
        }));
        botClient.TestApiAsync().Wait();
        Logger.Trace(language["twe.telegram.connected"]);
        break;
    }
    catch (Exception ex)
    {
        Logger.Trace($"{language["twe.telegram.connectionfailed"]}：{ex}", Logger.LogLevel.WARN);
        Thread.Sleep(5000);
    }
}

// Telegram监听
botClient.StartReceiving((botClient1, update, cancellationToken) =>
{
    if (listenerFunc.ContainsKey($"tg.{update.Type}"))
    {
        foreach (Action<Update> func in listenerFunc[$"tg.{update.Type}"])
        {
            func(update);
        }
    }
}, (botClient2, exception, cancellationToken) =>
{
    Logger.Trace($"{language["twe.telegram.connectionfailed"]}：{exception}", Logger.LogLevel.ERROR);
});

Logger.Trace(language["twe.plugin.loadfinish"].Replace("%count%", $"{LoadPlugins()}"));

// WebSocket监听
Task.Run(() =>
{
    while (true)
    {
        byte[] buffer = new byte[8192];
        try
        {
            ws.ReceiveAsync(buffer, default).Wait();
            string packStr = Encoding.UTF8.GetString(buffer).Replace("\0", "");
            if (config.debugmode)
            {
                Logger.Trace(packStr, Logger.LogLevel.DEBUG);
            }
            Pack pack = JsonSerializer.Deserialize<Pack>(packStr);
            string dataStr = Encoding.UTF8.GetString(aes.DecryptCbc(Convert.FromBase64String(pack.@params.raw), iv));
            if (config.debugmode)
            {
                Logger.Trace(dataStr, Logger.LogLevel.DEBUG);
            }
            Data data = JsonSerializer.Deserialize<Data>(dataStr);
            switch (data.cause)
            {
                case "decodefailed":
                    throw new CryptographicException($"{data.@params["msg"]}");
                case "invalidrequest":
                    throw new InvalidDataException($"{data.@params["msg"]}");
                default:
                    if (listenerFunc.ContainsKey($"ws.{data.cause}"))
                    {
                        foreach (Action<Dictionary<string, object>> func in listenerFunc[$"ws.{data.cause}"])
                        {
                            func(data.@params);
                        }
                    }
                    break;
            }
        }
        catch (Exception ex)
        {
            Logger.Trace($"{language["twe.websocket.receivefailed"]}：{ex}", Logger.LogLevel.ERROR);
        }
    }
});

// 控制台命令
while (true)
{
    string input = Console.ReadLine() ?? "";
    if (input.StartsWith("unload "))
    {
        try
        {
            engines.Remove(input[7..]);
            GC.SuppressFinalize(engines[input[7..]]);
            Logger.Trace(language["twe.plugin.unloaded"].Replace("%name%", $"{input[7..]}"));
        }
        catch (Exception ex)
        {
            Logger.Trace($"{language["twe.plugin.unloadfailed"].Replace("%name%", $"{input[7..]}")}：{ex}", Logger.LogLevel.WARN);
        }
    }
    else
    {
        switch (input)
        {
            case "reload":
                foreach (KeyValuePair<Engine, PluginInfo> engine in engines.Values)
                {
                    GC.SuppressFinalize(engine.Key);
                }
                engines.Clear();
                listenerFunc.Clear();
                exportFunc.Clear();
                Logger.Trace(language["twe.plugin.loadfinish"].Replace("%count%", $"{LoadPlugins()}"));
                break;
            case "list":
                Logger.Trace($"插件列表 [{engines.Count}]");
                foreach (KeyValuePair<string, KeyValuePair<Engine, PluginInfo>> engine in engines)
                {
                    Logger.Trace($"- {engine.Key} [{engine.Value.Value.version[0]}.{engine.Value.Value.version[1]}.{engine.Value.Value.version[2]}] ({engine.Value.Value.finename})");
                    Logger.Trace($"  {engine.Value.Value.introduction}");
                }
                break;
        }
    }
}

// 加载ES插件
int LoadPlugins()
{
    foreach (FileInfo file in new DirectoryInfo("plugins").GetFiles("*.js"))
    {
        Engine es = new();
        string pluginName = file.Name;
        PluginInfo info = new()
        {
            introduction = "",
            finename = file.Name,
            version = new List<int>
            {
                1,
                0,
                0
            }
        };
        es.SetValue("twe", new Dictionary<string, object>
        {
            ["registerPlugin"] = (string name, string introduction, List<int> version) => {
                pluginName = name;
                info.introduction = introduction;
                if (version.Count != 3)
                {
                    Logger.Trace("version 不太对哦");
                }
                info.version = version;
            },
            ["listen"] = (string type, Action<object> func) =>
            {
                if (!listenerFunc.ContainsKey(type))
                {
                    listenerFunc[type] = new();
                }
                listenerFunc[type].Add(func);
            },
            ["log"] = (object message) =>
            {
                Logger.Trace($"{message}");
            },
            ["export"] = (object func, string name) =>
            {
                try
                {
                    exportFunc.Add(name, func);
                }
                catch (Exception ex)
                {
                    Logger.Trace($"{language["twe.plugin.apierror"].Replace("%name%", $"{file.Name}")}：{ex.Message}", Logger.LogLevel.WARN);
                }
            },
            ["import"] = (string name) =>
            {
                try
                {
                    es.SetValue(name, exportFunc[name]);
                }
                catch (Exception ex)
                {
                    Logger.Trace($"{language["twe.plugin.apierror"].Replace("%name%", $"{file.Name}")}：{ex.Message}", Logger.LogLevel.WARN);
                }
            },
            ["listPlugins"] = () =>
            {
                return engines.Keys;
            },
            ["eval"] = (string code) =>
            {
                es.Execute(code);
            }// WIP

        });
        es.SetValue("tg", new Dictionary<string, object>
        {
            ["sendMessage"] = (long chatid, string msg, int? type) =>
            {
                botClient.SendTextMessageAsync(chatid, msg, (Telegram.Bot.Types.Enums.ParseMode?)type);
            },// WIP
        });
        es.SetValue("ws", new Dictionary<string, object>
        {
            ["sendPack"] = (string type, string action, Dictionary<string, object> @params) =>
            {
                sendPack(new SendData
                {
                    type = type,
                    action = action,
                    @params = @params
                });
            },// WIP
        });
        es.SetValue("mc", new Dictionary<string, object>    // 为MC准备的方便API
        {
            ["runcmd"] = (string cmd) =>
            {
                sendPack(new SendData
                {
                    type = "pack",
                    action = "runcmdrequest",
                    @params = new Dictionary<string, object>
                    {
                        ["cmd"] = cmd,
                        ["id"] = id++
                    }
                });
                return id;
            },// WIP
        });
        try
        {
            es.Execute(File.ReadAllText(file.FullName));
            engines.Add(pluginName, new KeyValuePair<Engine, PluginInfo>(es, info));
            Logger.Trace(language["twe.plugin.loaded"].Replace("%name%", $"{file.Name}"));
        }
        catch (Exception ex)
        {
            Logger.Trace($"{language["twe.plugin.loadfailed"].Replace("%name%", $"{file.Name}")}：{ex}", Logger.LogLevel.WARN);
            GC.SuppressFinalize(es);
        }
    }
    return engines.Count;
}

// 发WS包
void sendPack(SendData input)
{
    ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new Pack
    {
        type = "encrypted",
        @params = new Pack.ParamsData
        {
            mode = config.encrypt,
            raw = Convert.ToBase64String(aes.EncryptCbc(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input)), iv)),
        }
    })), WebSocketMessageType.Text, WebSocketMessageFlags.EndOfMessage, default).AsTask().Wait();
}
