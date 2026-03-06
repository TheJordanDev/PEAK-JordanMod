using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ExitGames.Client.Photon;
using Photon.Pun;
using Photon.Realtime;
using UnityEngine;

namespace JordanMod.Modules;

public class ModuleManager
{
    private readonly Dictionary<string, Module> _modules = [];
    private event Action? OnInitialize;
    private event Action? OnUpdate;
    private event Action? OnFixedUpdate;
    private event Action? OnDestroy;

    public void Initialize()
    {
        AddNetworkEventListener();
        LoadModules();
        Plugin.Log.LogInfo($"ModuleManager initialized with {_modules.Count} modules");
    }

    public List<Module> GetAllModules()
    {
        return [.. _modules.Values];
    }

    private void AddNetworkEventListener()
    {
        GameObject listenerObject = new("PhotonNetworkEventListener");
        UnityEngine.Object.DontDestroyOnLoad(listenerObject);
        listenerObject.AddComponent<PhotonNetworkEventListener>();
    }

    private void LoadModules()
    {
        // Auto-discover all types with the [Module] attribute
        var moduleTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.IsSubclassOf(typeof(Module)))
            .Select(t => new { Type = t, Attribute = t.GetCustomAttribute<ModuleAttribute>() })
            .Where(x => x.Attribute != null)
            .ToList();

        Plugin.Log.LogInfo($"Discovered {moduleTypes.Count} module(s) with [Module] attribute");

        foreach (var moduleInfo in moduleTypes)
        {
            try
            {
                Module module = (Module)Activator.CreateInstance(moduleInfo.Type)!;
                
                if (!moduleInfo.Attribute!.Enabled)
                    continue;

                _modules[module.ModuleName] = module;

                if (HasOverriddenMethod(moduleInfo.Type, nameof(Module.Initialize)))
                    OnInitialize += module.Initialize;

                if (HasOverriddenMethod(moduleInfo.Type, nameof(Module.Update)))
                    OnUpdate += module.Update;

                if (HasOverriddenMethod(moduleInfo.Type, nameof(Module.FixedUpdate)))
                    OnFixedUpdate += module.FixedUpdate;

                if (HasOverriddenMethod(moduleInfo.Type, nameof(Module.Destroy)))
                    OnDestroy += module.Destroy;

                Plugin.Log.LogInfo($"Loaded module: {module.ModuleName}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to load module {moduleInfo.Type.Name}: {e}");
            }
        }

        OnInitialize?.Invoke();
    }

    private static bool HasOverriddenMethod(Type type, string methodName)
    {
        var method = type.GetMethod(methodName);
        return method != null && method.DeclaringType != typeof(Module);
    }

    public void Update()
    {
        OnUpdate?.Invoke();
    }

    public void FixedUpdate()
    {
        OnFixedUpdate?.Invoke();
    }

    public void Destroy()
    {
        OnDestroy?.Invoke();
        
        _modules.Clear();
        OnInitialize = null;
        OnUpdate = null;
        OnFixedUpdate = null;
        OnDestroy = null;
    }

    public Module? GetModule(string moduleName)
    {
        _modules.TryGetValue(moduleName, out Module? module);
        return module;
    }

}

public abstract class Module
{
    public abstract string ModuleName { get; }

    public virtual void Initialize()
    {
        Plugin.Log.LogInfo($"Module '{ModuleName}' initialized");
    }

    public virtual void Update()
    {
    }

    public virtual void FixedUpdate()
    {
    }
    
    public virtual Type[] GetPatches()
    {
        return [];
    }

    public virtual void Destroy()
    {
        Plugin.Log.LogInfo($"Module '{ModuleName}' destroyed");
    }

}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public class ModuleAttribute : Attribute
{
    public bool Enabled { get; set; } = true;
}

class PhotonNetworkEventListener : MonoBehaviourPunCallbacks
{
	public static PhotonNetworkEventListener? Instance { get; private set; }

    public override void OnEnable()
    {
        if (Instance != null) return;
        Instance = this;
        base.OnEnable();
	}

    public event Action? Connected;
    public event Action? LeftRoom;
    public event Action<Photon.Realtime.Player>? MasterClientSwitched;
    public event Action<short, string>? CreateRoomFailed;
    public event Action<short, string>? JoinRoomFailed;
    public event Action? CreatedRoom;
    public event Action? JoinedLobby;
    public event Action? LeftLobby;
    public event Action<DisconnectCause>? Disconnected;
    public event Action<RegionHandler>? RegionListReceived;
    public event Action<List<RoomInfo>>? RoomListUpdate;
    public event Action? JoinedRoom;
    public event Action<Photon.Realtime.Player>? PlayerEnteredRoom;
    public event Action<Photon.Realtime.Player>? PlayerLeftRoom;
    public event Action<short, string>? JoinRandomFailed;
    public event Action? ConnectedToMaster;
    public event Action<ExitGames.Client.Photon.Hashtable>? RoomPropertiesUpdate;
    public event Action<Photon.Realtime.Player, ExitGames.Client.Photon.Hashtable>? PlayerPropertiesUpdate;
    public event Action<List<FriendInfo>>? FriendListUpdate;
    public event Action<Dictionary<string, object>>? CustomAuthenticationResponse;
    public event Action<string>? CustomAuthenticationFailed;
    public event Action<OperationResponse>? WebRpcResponse;
    public event Action<List<TypedLobbyInfo>>? LobbyStatisticsUpdate;
    public event Action<ErrorInfo>? ErrorInfoEvent;

    // Register methods
    public void RegisterOnConnected(Action handler) => Connected += handler;
    public void RegisterOnLeftRoom(Action handler) => LeftRoom += handler;
    public void RegisterOnMasterClientSwitched(Action<Photon.Realtime.Player> handler) => MasterClientSwitched += handler;
    public void RegisterOnCreateRoomFailed(Action<short, string> handler) => CreateRoomFailed += handler;
    public void RegisterOnJoinRoomFailed(Action<short, string> handler) => JoinRoomFailed += handler;
    public void RegisterOnCreatedRoom(Action handler) => CreatedRoom += handler;
    public void RegisterOnJoinedLobby(Action handler) => JoinedLobby += handler;
    public void RegisterOnLeftLobby(Action handler) => LeftLobby += handler;
    public void RegisterOnDisconnected(Action<DisconnectCause> handler) => Disconnected += handler;
    public void RegisterOnRegionListReceived(Action<RegionHandler> handler) => RegionListReceived += handler;
    public void RegisterOnRoomListUpdate(Action<List<RoomInfo>> handler) => RoomListUpdate += handler;
    public void RegisterOnJoinedRoom(Action handler) => JoinedRoom += handler;
    public void RegisterOnPlayerEnteredRoom(Action<Photon.Realtime.Player> handler) => PlayerEnteredRoom += handler;
    public void RegisterOnPlayerLeftRoom(Action<Photon.Realtime.Player> handler) => PlayerLeftRoom += handler;
    public void RegisterOnJoinRandomFailed(Action<short, string> handler) => JoinRandomFailed += handler;
    public void RegisterOnConnectedToMaster(Action handler) => ConnectedToMaster += handler;
    public void RegisterOnRoomPropertiesUpdate(Action<ExitGames.Client.Photon.Hashtable> handler) => RoomPropertiesUpdate += handler;
    public void RegisterOnPlayerPropertiesUpdate(Action<Photon.Realtime.Player, ExitGames.Client.Photon.Hashtable> handler) => PlayerPropertiesUpdate += handler;
    public void RegisterOnFriendListUpdate(Action<List<FriendInfo>> handler) => FriendListUpdate += handler;
    public void RegisterOnCustomAuthenticationResponse(Action<Dictionary<string, object>> handler) => CustomAuthenticationResponse += handler;
    public void RegisterOnCustomAuthenticationFailed(Action<string> handler) => CustomAuthenticationFailed += handler;
    public void RegisterOnWebRpcResponse(Action<OperationResponse> handler) => WebRpcResponse += handler;
    public void RegisterOnLobbyStatisticsUpdate(Action<List<TypedLobbyInfo>> handler) => LobbyStatisticsUpdate += handler;
    public void RegisterOnErrorInfo(Action<ErrorInfo> handler) => ErrorInfoEvent += handler;

    // Override and invoke actions
    public override void OnConnected()
    {
        Connected?.Invoke();
    }

    public override void OnLeftRoom()
    {
        LeftRoom?.Invoke();
    }

    public override void OnMasterClientSwitched(Photon.Realtime.Player newMasterClient)
    {
        MasterClientSwitched?.Invoke(newMasterClient);
    }

    public override void OnCreateRoomFailed(short returnCode, string message)
    {
        CreateRoomFailed?.Invoke(returnCode, message);
    }

    public override void OnJoinRoomFailed(short returnCode, string message)
    {
        JoinRoomFailed?.Invoke(returnCode, message);
    }

    public override void OnCreatedRoom()
    {
        CreatedRoom?.Invoke();
    }

    public override void OnJoinedLobby()
    {
        JoinedLobby?.Invoke();
    }

    public override void OnLeftLobby()
    {
        LeftLobby?.Invoke();
    }

    public override void OnDisconnected(DisconnectCause cause)
    {
        Disconnected?.Invoke(cause);
    }

    public override void OnRegionListReceived(RegionHandler regionHandler)
    {
        RegionListReceived?.Invoke(regionHandler);
    }

    public override void OnRoomListUpdate(List<RoomInfo> roomList)
    {
        RoomListUpdate?.Invoke(roomList);
    }

    public override void OnJoinedRoom()
    {
        JoinedRoom?.Invoke();
    }

    public override void OnPlayerEnteredRoom(Photon.Realtime.Player newPlayer)
    {
        PlayerEnteredRoom?.Invoke(newPlayer);
    }

    public override void OnPlayerLeftRoom(Photon.Realtime.Player otherPlayer)
    {
        PlayerLeftRoom?.Invoke(otherPlayer);
    }

    public override void OnJoinRandomFailed(short returnCode, string message)
    {
        JoinRandomFailed?.Invoke(returnCode, message);
    }

    public override void OnConnectedToMaster()
    {
        ConnectedToMaster?.Invoke();
    }

    public override void OnRoomPropertiesUpdate(ExitGames.Client.Photon.Hashtable propertiesThatChanged)
    {
        RoomPropertiesUpdate?.Invoke(propertiesThatChanged);
    }

    public override void OnPlayerPropertiesUpdate(Photon.Realtime.Player targetPlayer, ExitGames.Client.Photon.Hashtable changedProps)
    {
        PlayerPropertiesUpdate?.Invoke(targetPlayer, changedProps);
    }

    public override void OnFriendListUpdate(List<FriendInfo> friendList)
    {
        FriendListUpdate?.Invoke(friendList);
    }

    public override void OnCustomAuthenticationResponse(Dictionary<string, object> data)
    {
        CustomAuthenticationResponse?.Invoke(data);
    }

    public override void OnCustomAuthenticationFailed(string debugMessage)
    {
        CustomAuthenticationFailed?.Invoke(debugMessage);
    }

    public override void OnWebRpcResponse(OperationResponse response)
    {
        WebRpcResponse?.Invoke(response);
    }

    public override void OnLobbyStatisticsUpdate(List<TypedLobbyInfo> lobbyStatistics)
    {
        LobbyStatisticsUpdate?.Invoke(lobbyStatistics);
    }

    public override void OnErrorInfo(ErrorInfo errorInfo)
    {
        ErrorInfoEvent?.Invoke(errorInfo);
    }

}