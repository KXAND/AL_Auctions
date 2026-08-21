using System;
using System.Collections;
using System.Collections.Generic;
using AuctionGame;
using UnityEngine;
using UnityEngine.SceneManagement;
using yaSingleton;

public enum AuctionLaunchMode
{
    Offline,
    Online
}

[CreateAssetMenu(fileName = "GameManager", menuName = "Singletons/GameManager")]
public class GameManager : Singleton<GameManager>
{

    [NonSerialized] private AuctionLaunchMode? _pendingAuctionLaunch;
    [SerializeField] private ItemCatalog _itemCatalog;
    [SerializeField] private ClueCatalog clueCatalog;
    [NonSerialized] private AuctionManager _auctionManager;
    [NonSerialized] private Authority _localAuthority;
    [NonSerialized] private HumanController _humanController;
    [NonSerialized] private readonly List<AIController> _aiControllers = new List<AIController>();
    [NonSerialized] private readonly HashSet<string> _appliedSettlements = new HashSet<string>();
    [NonSerialized] private int _localAssets;
    [NonSerialized] private bool _localAssetsAssigned;
    [NonSerialized] private string _localPlayerIdentity;

    public AuctionManager CurrentAuctionManager => _auctionManager;
    public HumanController CurrentHumanController => _humanController;
    public string CurrentAuctionPlayerIdentity =>
        _auctionManager != null
            ? _auctionManager.GetPlayerIdentity(_humanController)
            : null;

    public string HumanAssetOwnerId =>
        _auctionManager != null && _auctionManager.Mode == AuctionManagerMode.Local
            ? "local-player"
            : _auctionManager != null
                ? _auctionManager.ConnectedPlayerIdentity
                : null;
    public event Action<AuctionManager> CurrentAuctionManagerChanged;

    public void PlayAuctionOnline() => StartAuction(AuctionLaunchMode.Online);
    public void PlayAuctionOffline() => StartAuction(AuctionLaunchMode.Offline);

    public void ResetAuction()
    {
        if (_auctionManager != null && _auctionManager.Mode == AuctionManagerMode.Local)
        {
            EnterLocalGame(true);
        }
    }

    public void ReconnectAuction()
    {
        if (_auctionManager != null)
        {
            _auctionManager.Reconnect();
        }
    }

    public void RestartAuction()
    {
        if (_auctionManager == null)
        {
            return;
        }

        if (_auctionManager.Mode == AuctionManagerMode.Local)
        {
            EnterLocalGame(false);
        }
        else
        {
            EnterOnlineGame();
        }
    }

    public void ExitAuction()
    {
        ExitGame();
    }

    protected override void Initialize()
    {
        base.Initialize();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    protected override void Deinitialize()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        DisposeAuctionObjects();
        base.Deinitialize();
    }

    private void StartAuction(AuctionLaunchMode mode)
    {
        _pendingAuctionLaunch = mode;
        SceneLoader.Instance.LoadScene(UnityConstants.Scenes.AuctionDemo, true, false);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode loadMode)
    {
        if (scene.buildIndex != UnityConstants.Scenes.AuctionDemo || !_pendingAuctionLaunch.HasValue)
        {
            return;
        }
        AuctionLaunchMode mode = _pendingAuctionLaunch.Value;
        _pendingAuctionLaunch = null;
        if (mode == AuctionLaunchMode.Offline)
        {
            EnterLocalGame(false);
        }
        else
        {
            EnterOnlineGame();
        }
    }

    private void EnterLocalGame(bool resetAssets)
    {
        DisposeAuctionObjects();

        if (resetAssets || !_localAssetsAssigned)
        {
            _localAssets = GlobalSettings.InitialAssets;
            _localAssetsAssigned = true;
            _appliedSettlements.Clear();
            _localPlayerIdentity = $"local-player-{Guid.NewGuid():N}";
        }

        string matchId = Guid.NewGuid().ToString("N");
        MatchParticipant[] participants = new MatchParticipant[GlobalSettings.PlayerCount];
        participants[0] = new MatchParticipant(
            _localPlayerIdentity,
            _localAssets,
            false,
            "local-player");
        for (int index = 1; index < participants.Length; index++)
        {
            string identity = $"local-ai-{matchId}-{index}";
            participants[index] = new MatchParticipant(
                identity,
                GlobalSettings.InitialAssets,
                true,
                identity);
        }

        _localAuthority = new Authority(
            _itemCatalog,
            clueCatalog,
            new System.Random());
        _auctionManager = new GameObject("Local AuctionManager").AddComponent<AuctionManager>();

        CurrentAuctionManagerChanged?.Invoke(_auctionManager);
        _auctionManager.StartLocal(_localAuthority);
        _humanController = FindFirstObjectByType<HumanController>();
        if (_humanController == null)
        {
            _humanController = new GameObject("HumanController").AddComponent<HumanController>();
        }
        _humanController.Bind(_auctionManager);
        _auctionManager.RegisterController(_humanController, participants[0].PlayerIdentity);
        for (int index = 1; index < participants.Length; index++)
        {
            AIController ai = new AIController(_auctionManager, new System.Random());
            _aiControllers.Add(ai);
            _auctionManager.RegisterController(ai, participants[index].PlayerIdentity);
        }

        _localAuthority.SettlementCreated += OnLocalSettlementCreated;
        _localAuthority.PrepareMatch(matchId, participants);
        _localAuthority.StartMatch();
    }

    private void EnterOnlineGame()
    {
        DisposeAuctionObjects();

        _auctionManager = new GameObject("Online AuctionManager").AddComponent<AuctionManager>();
        CurrentAuctionManagerChanged?.Invoke(_auctionManager);

        _auctionManager.ConnectionChanged += OnOnlineConnectionChanged;
        _auctionManager.StartOnlineClient(Guid.NewGuid().ToString("N"));
    }

    private void OnOnlineConnectionChanged(string state)
    {
        if (state != "Connected" || _auctionManager == null)
        {
            return;
        }
        if (_humanController != null)
        {
            _auctionManager.UnregisterController(_humanController);
        }
        _humanController = FindFirstObjectByType<HumanController>();
        if (_humanController == null)
        {
            _humanController = new GameObject("HumanController").AddComponent<HumanController>();
        }
        _humanController.Bind(_auctionManager);
        _auctionManager.RegisterController(_humanController, _auctionManager.ConnectedPlayerIdentity);
    }

    private void OnLocalSettlementCreated(SettlementRecord settlement)
    {
        if (!_appliedSettlements.Add(settlement.SettlementId))
        {
            return;
        }
        if (settlement.FinalVisibleAssets.TryGetValue("local-player", out int assets))
        {
            _localAssets = assets;
        }
    }


    private void ExitGame()
    {
        _pendingAuctionLaunch = null;
        DisposeAuctionObjects();
        SceneLoader.Instance.LoadScene(UnityConstants.Scenes.MainMenu, true, false);
    }

    private void DisposeAuctionObjects()
    {
        if (_localAuthority != null)
        {
            _localAuthority.SettlementCreated -= OnLocalSettlementCreated;
        }
        AuctionManager disposedAuctionManager = _auctionManager;
        if (_auctionManager != null)
        {
            _auctionManager.ConnectionChanged -= OnOnlineConnectionChanged;
            _auctionManager.Disconnect();
            if (_auctionManager.gameObject != null)
            {
                UnityEngine.Object.Destroy(_auctionManager.gameObject);
            }
        }
        _auctionManager = null;
        if (disposedAuctionManager != null)
        {
            CurrentAuctionManagerChanged?.Invoke(null);
        }
        _localAuthority = null;
        _humanController = null;
        _aiControllers.Clear();
    }
}




