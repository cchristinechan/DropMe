namespace DropMe.Services.Session;

public enum SessionState {
    Idle,
    Connecting,
    Connected,
    Closed,
    Error
}