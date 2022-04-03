public struct Config
{
    public string ListenAddr { get; set; }
    public string Endpoint { get; set; }
    public string Token { get; set; }
    public bool UsingTLS { get; set; }
    public string CertFile { get; set; }
    public string KeyFile { get; set; }
    public string BotToken { get; set; }
    public string ProxyAddr { get; set; }
    public string Language { get; set; }
    public bool DebugMode { get; set; }
}
