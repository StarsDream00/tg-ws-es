public struct PacketBase
{
    public string Action { get; set; }
    public long PacketId { get; set; }
    public object Params { get; set; }
}
public struct ResponseWithMessage
{
    public string Message { get; set; }
}