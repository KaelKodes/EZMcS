using Godot;
using System;

public partial class NetworkManager : Node
{
    private ENetMultiplayerPeer _peer;
    private int _port = 8181;

    [Signal] public delegate void ConnectionStatusChangedEventHandler(bool connected, bool isHost);
    [Signal] public delegate void ConfigurationSyncedEventHandler(string path, string jar, string maxRam, string minRam, string javaPath, string extraFlags);

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
    }

    private void OnPeerConnected(long id)
    {
        if (Multiplayer.IsServer())
        {
            // When a client connects, send them the current server status and configuration
            ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
            RpcId(id, MethodName.ReceiveRemoteStatus, sm.IsRunning ? (sm.IsRunning ? "Running" : "Stopped") : "Stopped");

            // Sync current player list
            foreach (var player in sm.OnlinePlayers)
            {
                RpcId(id, MethodName.ReceiveRemotePlayerJoined, player);
            }
        }
    }

    public void CreateHost(int port = 8181)
    {
        Disconnect();

        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateServer(port);
        if (err != Error.Ok)
        {
            GD.PrintErr("Failed to create host: " + err);
            return;
        }
        Multiplayer.MultiplayerPeer = _peer;
        EmitSignal(SignalName.ConnectionStatusChanged, true, true);

        ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
        sm.LogReceived -= OnLocalLogReceived;
        sm.StatusChanged -= OnLocalStatusChanged;
        sm.PlayerJoined -= OnLocalPlayerJoined;
        sm.PlayerLeft -= OnLocalPlayerLeft;

        sm.LogReceived += OnLocalLogReceived;
        sm.StatusChanged += OnLocalStatusChanged;
        sm.PlayerJoined += OnLocalPlayerJoined;
        sm.PlayerLeft += OnLocalPlayerLeft;

        GD.Print("Management Host created on port " + port);
    }

    public void ConnectToHost(string address, int port = 8181)
    {
        Disconnect();

        _peer = new ENetMultiplayerPeer();
        Error err = _peer.CreateClient(address, port);
        if (err != Error.Ok)
        {
            GD.PrintErr("Failed to connect to host: " + err);
            return;
        }
        Multiplayer.MultiplayerPeer = _peer;
        EmitSignal(SignalName.ConnectionStatusChanged, true, false);
        GD.Print("Connecting to " + address + ":" + port);
    }

    public void Disconnect()
    {
        if (Multiplayer.MultiplayerPeer != null)
        {
            if (_peer != null) _peer.Close();
            Multiplayer.MultiplayerPeer = null;
        }
        _peer = null;

        ServerManager sm = GetNode<ServerManager>("/root/ServerManager");
        sm.LogReceived -= OnLocalLogReceived;
        sm.StatusChanged -= OnLocalStatusChanged;
        sm.PlayerJoined -= OnLocalPlayerJoined;
        sm.PlayerLeft -= OnLocalPlayerLeft;

        EmitSignal(SignalName.ConnectionStatusChanged, false, false);
        GD.Print("Network management disconnected.");
    }

    // --- Configuration Sync ---

    public void SyncConfigurationToAll(string path, string jar, string maxRam, string minRam, string javaPath, string extraFlags)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveConfiguration, path, jar, maxRam, minRam, javaPath, extraFlags);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveConfiguration(string path, string jar, string maxRam, string minRam, string javaPath, string extraFlags)
    {
        EmitSignal(SignalName.ConfigurationSynced, path, jar, maxRam, minRam, javaPath, extraFlags);
    }

    // --- Remote Lifecycle Requests ---

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    public void RequestStartServer(string path, string jar, string maxRam, string minRam, string javaPath, string extraFlags)
    {
        if (Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").StartServer(path, jar, maxRam, minRam, javaPath, extraFlags);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    public void RequestStopServer()
    {
        if (Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").StopServer();
        }
    }

    // --- Standard Monitoring ---

    private void OnLocalLogReceived(string message, bool isError)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveRemoteLog, message, isError);
        }
    }

    private void OnLocalStatusChanged(string newStatus)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveRemoteStatus, newStatus);
        }
    }

    private void OnLocalPlayerJoined(string playerName)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveRemotePlayerJoined, playerName);
        }
    }

    private void OnLocalPlayerLeft(string playerName)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveRemotePlayerLeft, playerName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    public void SendRemoteCommand(string command)
    {
        if (Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").SendCommand(command);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemoteLog(string message, bool isError)
    {
        if (!Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").EmitSignal(ServerManager.SignalName.LogReceived, message, isError);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemoteStatus(string newStatus)
    {
        if (!Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").EmitSignal(ServerManager.SignalName.StatusChanged, newStatus);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemotePlayerJoined(string playerName)
    {
        if (!Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").EmitSignal(ServerManager.SignalName.PlayerJoined, playerName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemotePlayerLeft(string playerName)
    {
        if (!Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").EmitSignal(ServerManager.SignalName.PlayerLeft, playerName);
        }
    }
}
