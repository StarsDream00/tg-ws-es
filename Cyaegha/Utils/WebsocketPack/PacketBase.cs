public struct PacketBase<T> {
    public string Action { get; set; }
    public long PacketId { get; set; }
    public T Params { get; set; }
}
public struct ResponseWithMessage {
    public string Message { get; set; }
}