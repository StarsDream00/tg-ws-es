public struct Data
{
    public string type { get; set; }
    public string cause { get; set; }
    public Dictionary<string, object> @params { get; set; }
}

public struct SendData
{
    public string type { get; set; }
    public string action { get; set; }
    public Dictionary<string, object> @params { get; set; }
}
