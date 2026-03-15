namespace ScreenConnect.WebApp.Models;

public class ProcessSessionRequest
{
    public string SessionId { get; set; } = string.Empty;
    public string ScreenConnectBaseUrl { get; set; } = string.Empty;
    public string? SessionCode { get; set; } = string.Empty;
    public string OperatingSystem { get; set; } = "windows"; // Default to Windows
}

public class ResolveSessionCodeRequest
{
    public string SessionCode { get; set; } = string.Empty;
    public string ScreenConnectBaseUrl { get; set; } = string.Empty;
}

public class ResolveSessionCodeResponse
{
    public string SessionId { get; set; } = string.Empty;
    public string SessionCode { get; set; } = string.Empty;
}

public class ErrorResponse
{
    public string Error { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

public class ClientLaunchParameters
{
    public string h { get; set; } = string.Empty; // host
    public int p { get; set; } = 443; // port
    public string s { get; set; } = string.Empty; // session ID
    public string k { get; set; } = string.Empty; // key
    public string n { get; set; } = string.Empty; // parameter n
    public string r { get; set; } = string.Empty; // parameter r
    public string e { get; set; } = string.Empty; // parameter e
    public string i { get; set; } = string.Empty; // session name
    public string a { get; set; } = string.Empty; // parameter a
    public string l { get; set; } = string.Empty; // parameter l
}

public class ScreenConnectApiRequest
{
    public GuestSessionInfo GuestSessionInfo { get; set; } = new();
}

public class GuestSessionInfo
{
    public string[] SessionCodes { get; set; } = Array.Empty<string>();
    public string[] SessionIDs { get; set; } = Array.Empty<string>();
}

public class ScreenConnectApiResponse
{
    public long Version { get; set; }
    public string ProductVersion { get; set; } = string.Empty;
    public ResponseInfoMap ResponseInfoMap { get; set; } = new();
}

public class ResponseInfoMap
{
    public GuestSessionInfoResponse GuestSessionInfo { get; set; } = new();
}

public class GuestSessionInfoResponse
{
    public bool DoNonPublicCodeSessionsExist { get; set; }
    public SessionInfo[] Sessions { get; set; } = Array.Empty<SessionInfo>();
}

public class SessionInfo
{
    public string SessionID { get; set; } = string.Empty;
    public int SessionType { get; set; }
    public string Host { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public object[] ActiveConnections { get; set; } = Array.Empty<object>();
} 