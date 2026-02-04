using Godot;
using System;
using System.Collections.Generic;

public partial class NetworkManager : Node
{
    private ENetMultiplayerPeer _peer;
    private int _port = 8181;

    [Signal] public delegate void ConnectionStatusChangedEventHandler(bool connected, bool isHost);
    [Signal] public delegate void ConfigurationSyncedEventHandler(string path, string jar, string maxRam, string minRam, string javaPath, string extraFlags);
    [Signal] public delegate void RemotePropertiesReceivedEventHandler(Godot.Collections.Dictionary props);
    [Signal] public delegate void SystemStatsReceivedEventHandler(float cpu, float ram, float serverRam);

    public override void _Ready()
    {
        Multiplayer.PeerConnected += OnPeerConnected;
    }

    private void OnPeerConnected(long id)
    {
        if (Multiplayer.IsServer())
        {
            // When a client connects, we should ideally sync all running servers' status
            // For now, the client will request status as needed or we can broadcast on change.
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
    public void RequestStartServer(string profileName, string path, string jar, string maxRam, string minRam, string javaPath, string extraFlags)
    {
        if (Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").StartServer(profileName, path, jar, maxRam, minRam, javaPath, extraFlags);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    public void RequestStopServer(string profileName)
    {
        if (Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").StopServer(profileName);
        }
    }

    // --- Standard Monitoring ---

    private void OnLocalLogReceived(string profileName, string message, bool isError)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveRemoteLog, profileName, message, isError);
        }
    }

    private void OnLocalStatusChanged(string profileName, string newStatus)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveRemoteStatus, profileName, newStatus);
        }
    }

    private void OnLocalPlayerJoined(string profileName, string playerName)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveRemotePlayerJoined, profileName, playerName);
        }
    }

    private void OnLocalPlayerLeft(string profileName, string playerName)
    {
        if (Multiplayer.IsServer())
        {
            Rpc(MethodName.ReceiveRemotePlayerLeft, profileName, playerName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    public void SendRemoteCommand(string profileName, string command)
    {
        if (Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").SendCommand(profileName, command);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemoteLog(string profileName, string message, bool isError)
    {
        if (!Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").EmitSignal(ServerManager.SignalName.LogReceived, profileName, message, isError);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemoteStatus(string profileName, string newStatus)
    {
        if (!Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").EmitSignal(ServerManager.SignalName.StatusChanged, profileName, newStatus);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemotePlayerJoined(string profileName, string playerName)
    {
        if (!Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").EmitSignal(ServerManager.SignalName.PlayerJoined, profileName, playerName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemotePlayerLeft(string profileName, string playerName)
    {
        if (!Multiplayer.IsServer())
        {
            GetNode<ServerManager>("/root/ServerManager").EmitSignal(ServerManager.SignalName.PlayerLeft, profileName, playerName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    public void RequestRemoteProperties(string path)
    {
        if (Multiplayer.IsServer())
        {
            var dict = ConfigManager.LoadProperties(path);
            var gDict = new Godot.Collections.Dictionary();
            foreach (var kvp in dict) gDict[kvp.Key] = kvp.Value;

            RpcId(Multiplayer.GetRemoteSenderId(), MethodName.ReceiveRemoteProperties, gDict);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false)]
    public void ReceiveRemoteProperties(Godot.Collections.Dictionary props)
    {
        EmitSignal(SignalName.RemotePropertiesReceived, props);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = false)]
    public void SaveRemoteProperties(string path, Godot.Collections.Dictionary props)
    {
        if (Multiplayer.IsServer())
        {
            var dict = new Dictionary<string, string>();
            foreach (var key in props.Keys) dict[key.ToString()] = props[key].ToString();

            ConfigManager.SaveProperties(path, dict);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = false, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
    public void ReceiveRemoteSystemStats(float cpu, float ram, float serverRam)
    {
        if (!Multiplayer.IsServer())
        {
            EmitSignal(SignalName.SystemStatsReceived, cpu, ram, serverRam);
        }
    }
}
