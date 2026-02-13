namespace DropMe.Services.Session;

public enum SessionMessageType : byte {
    Hello = 1,
    HelloAck = 2,
    Ping = 3,
    Pong = 4,

    FileOffer = 10,
    FileAccept = 11,
    FileReject = 12,
    FileChunk = 13,
    FileDone = 14,
    FileAck = 15,

    Error = 250,
    Bye = 255
}