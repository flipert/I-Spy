#if UNITY_EDITOR
if (availableServers.Count == 0)
{
    Debug.Log("********** ADDING TEST SERVER IN EDITOR FOR DEBUGGING **********");
    // Get public IP using GetExternalIP; fallback to loopback if unavailable
    string externalIP = GetExternalIP();
    if(string.IsNullOrEmpty(externalIP))
    {
        externalIP = "127.0.0.1";
    }
    ServerInfo testServer = new ServerInfo(
        "TEST SERVER (Editor Only)",
        externalIP,
        7777,
        1,
        maxPlayersPerServer,
        false
    );
    availableServers.Add(testServer);
    foundServers = true;
    
    Debug.Log($"********** ADDED TEST SERVER TO VERIFY UI **********\n" +
              $"Name: {testServer.serverName}\n" +
              $"IP: {testServer.ipAddress}\n" +
              $"Port: {testServer.port}");
}
#endif 