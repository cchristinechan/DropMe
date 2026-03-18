namespace DropMe.Services.Session;

public enum SessionMessageType : byte {
    Ping = 3,
    Pong = 4,
    FileOffer = 10,
    FileAccept = 11,
    FileReject = 12,
    FileChunk = 13,
    FileDone = 14,
    FileAck = 15,
    SwitchConnectionRequest = 16,
    SwitchConnectionAccept = 17,
    SwitchConnectionReject = 18,
    Disconnect = 19,
}