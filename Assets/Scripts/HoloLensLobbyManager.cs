﻿using Prototype.NetworkLobby;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Networking.Match;
using UnityEngine.Networking.Types;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System;
using System.Linq;

public class HoloLensLobbyManager : NetworkLobbyManager
{
    static short MsgKicked = MsgType.Highest + 1;

    static public HoloLensLobbyManager s_Singleton;

    [Header("Unity UI Lobby")]
    [Tooltip("Time in second between all players ready & match start")]
    public float prematchCountdown = 5.0f;

    //Client numPlayers from NetworkManager is always 0, so we count (throught connect/destroy in LobbyPlayer) the number
    //of players, so that even client know how many player there is.
    [HideInInspector]
    public int _playerNumber = 0;

    //used to disconnect a client properly when exiting the matchmaker
    [HideInInspector]
    public bool _isMatchmaking = false;

    protected bool _disconnectServer = false;

    protected ulong _currentMatchID;

    protected LobbyHook _lobbyHooks;

    public GameObject countdownView;

    void Start()
    {
        s_Singleton = this;
        _lobbyHooks = GetComponent<Prototype.NetworkLobby.LobbyHook>();
        DontDestroyOnLoad(gameObject);
    }

    public override void OnLobbyClientSceneChanged(NetworkConnection conn)
    {
        if (SceneManager.GetSceneAt(0).name != lobbyScene)
        {
            gameObject.SetActive(false);

            // Also find any HoloLensLobbyPlayer objects and disable them..
            var lobbyPlayers = FindObjectsOfType<HoloLensLobbyPlayer>();
            foreach (var lobbyPlayer in lobbyPlayers)
            {
                lobbyPlayer.gameObject.SetActive(false);
            }
        }
    }

    
        // ----------------- Server management
    private void SwitchToMainVIew()
    {
        // When we have started ourself as a host we need to transition to the players view..
        var go = transform.Find("PlayersView").gameObject;
        go.SetActive(false);

        var po = transform.Find("MainView").gameObject;
        po.SetActive(true);
    }

    private void SwitchToPlayerVIew()
    {
        // When we have started ourself as a host we need to transition to the players view..
        var go = transform.Find("MainView").gameObject;
        go.SetActive(false);

        var po = transform.Find("PlayersView").gameObject;
        po.SetActive(true);
    }

    public void AddLocalPlayer()
    {
        TryToAddPlayer();
    }

    public void RemovePlayer(HoloLensLobbyPlayer player)
    {
        player.RemovePlayer();
    }

    public void StopHostClbk()
    {
        if (_isMatchmaking)
        {
            matchMaker.DestroyMatch((NetworkID)_currentMatchID, 0, OnDestroyMatch);
            _disconnectServer = true;
        }
        else
        {
            StopHost();
        }
    }

    public void StopClientClbk()
    {
        StopClient();

        if (_isMatchmaking)
        {
            StopMatchMaker();
        }
    }

    public void StopServerClbk()
    {
        StopServer();
    }

    class KickMsg : MessageBase { }
    public void KickPlayer(NetworkConnection conn)
    {
        conn.Send(MsgKicked, new KickMsg());
    }

    public void KickedMessageHandler(NetworkMessage netMsg)
    {
        netMsg.conn.Disconnect();
    }

    //public override void OnServerAddPlayer(NetworkConnection conn, short playerControllerId)
    //{
    //    base.OnServerAddPlayer(conn, playerControllerId);
    //}
    //===================

    public override void OnStartHost()
    {
        base.OnStartHost();
        SwitchToPlayerVIew();
    }

    public override void OnLobbyClientEnter()
    {
        base.OnLobbyClientEnter();
        SwitchToPlayerVIew();
    }

    public override void OnMatchCreate(bool success, string extendedInfo, MatchInfo matchInfo)
    {
        base.OnMatchCreate(success, extendedInfo, matchInfo);
        _currentMatchID = (System.UInt64)matchInfo.networkId;
    }

    public override void OnDestroyMatch(bool success, string extendedInfo)
    {
        base.OnDestroyMatch(success, extendedInfo);
        if (_disconnectServer)
        {
            StopMatchMaker();
            StopHost();
        }
    }

    //allow to handle the (+) button to add/remove player
    public void OnPlayersNumberModified(int count)
    {
        _playerNumber += count;

        int localPlayerCount = 0;
        foreach (PlayerController p in ClientScene.localPlayers)
            localPlayerCount += (p == null || p.playerControllerId == -1) ? 0 : 1;
    }

    internal void SetCountdown(int countdown)
    {
        if (countdownView == null)
            return;
        var countDown = countdownView.GetComponent<ICountdown>();
        if (countDown == null)
            return;
        countDown.SetCountdown(countdown);
    }

    // ----------------- Server callbacks ------------------

    //we want to disable the button JOIN if we don't have enough player
    //But OnLobbyClientConnect isn't called on hosting player. So we override the lobbyPlayer creation
    public override GameObject OnLobbyServerCreateLobbyPlayer(NetworkConnection conn, short playerControllerId)
    {
        GameObject obj = Instantiate(lobbyPlayerPrefab.gameObject) as GameObject;

        HoloLensLobbyPlayer newPlayer = obj.GetComponent<HoloLensLobbyPlayer>();

        PlayerList.instance.AddPlayer(newPlayer);

        return obj;
    }

    public override void OnLobbyServerPlayerRemoved(NetworkConnection conn, short playerControllerId)
    {
    }

    public override void OnLobbyServerDisconnect(NetworkConnection conn)
    {
    }

    public override bool OnLobbyServerSceneLoadedForPlayer(GameObject lobbyPlayer, GameObject gamePlayer)
    {
        //This hook allows you to apply state data from the lobby-player to the game-player
        //just subclass "LobbyHook" and add it to the lobby object.
        if (_lobbyHooks)
            _lobbyHooks.OnLobbyServerSceneLoadedForPlayer(this, lobbyPlayer, gamePlayer);

        return true;
    }

    public override void OnLobbyServerSceneChanged(string sceneName)
    {
        base.OnLobbyServerSceneChanged(sceneName);
    }

    // --- Countdown management

    public override void OnLobbyServerPlayersReady()
    {
        Debug.Log("OnLobbyServerPlayersReady called");
        bool allready = true;
        for (int i = 0; i < lobbySlots.Length; ++i)
        {
            if (lobbySlots[i] != null)
                allready &= lobbySlots[i].readyToBegin;
        }

        if (allready)
        {
            Debug.Log("Start server countdown");

            StartCoroutine(ServerCountdownCoroutine());
        }
    }

    public IEnumerator ServerCountdownCoroutine()
    {
        float remainingTime = prematchCountdown;
        int floorTime = Mathf.FloorToInt(remainingTime);

        while (remainingTime > 0)
        {
            yield return null;

            remainingTime -= Time.deltaTime;
            int newFloorTime = Mathf.FloorToInt(remainingTime);

            if (newFloorTime != floorTime)
            {//to avoid flooding the network of message, we only send a notice to client when the number of plain seconds change.
                floorTime = newFloorTime;

                for (int i = 0; i < lobbySlots.Length; ++i)
                {
                    if (lobbySlots[i] != null)
                    {//there is maxPlayer slots, so some could be == null, need to test it before accessing!
                        (lobbySlots[i] as HoloLensLobbyPlayer).RpcUpdateCountdown(floorTime);
                    }
                }
            }
        }

        for (int i = 0; i < lobbySlots.Length; ++i)
        {
            if (lobbySlots[i] != null)
            {
                (lobbySlots[i] as HoloLensLobbyPlayer).RpcUpdateCountdown(0);
            }
        }

        ServerChangeScene(playScene);
    }

    public override void ServerChangeScene(string sceneName)
    {
        if (sceneName == this.lobbyScene)
        {
            foreach (var lobbyPlayer in lobbySlots)
            {
                if (lobbyPlayer == null)
                    continue;

                // find the game-player object for this connection, and destroy it
                var uv = lobbyPlayer.GetComponent<NetworkIdentity>();

                PlayerController playerController;
                playerController = (PlayerController)uv.connectionToClient.playerControllers.Where(pc => pc.playerControllerId == uv.playerControllerId).Single();
                if (playerController != null)
                {
                    NetworkServer.Destroy(playerController.gameObject);
                }

                if (NetworkServer.active)
                {
                    // re-add the lobby object
                    lobbyPlayer.GetComponent<NetworkLobbyPlayer>().readyToBegin = false;
                    NetworkServer.ReplacePlayerForConnection(uv.connectionToClient, lobbyPlayer.gameObject, uv.playerControllerId);
                }
            }
        }
        base.ServerChangeScene(sceneName);
    }

    // ----------------- Client callbacks ------------------

    public override void OnClientConnect(NetworkConnection conn)
    {
        base.OnClientConnect(conn);

        conn.RegisterHandler(MsgKicked, KickedMessageHandler);
    }


    public override void OnClientDisconnect(NetworkConnection conn)
    {
        base.OnClientDisconnect(conn);
    }

    public override void OnClientError(NetworkConnection conn, int errorCode)
    {
    }
}
