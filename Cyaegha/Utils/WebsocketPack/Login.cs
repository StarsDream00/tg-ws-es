public struct LoginRequest {
    public string Password { get; set; }
}
public struct LoginResponse {
    public bool Success { get; set; }
    public string Message { get; set; }
}
