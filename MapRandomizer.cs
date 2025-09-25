// Author: Omniscye/Empress
using BepInEx;
using BepInEx.Configuration;
using ExitGames.Client.Photon;
using HarmonyLib;
using Photon.Pun;
using Photon.Realtime;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Empress.VoteNextMap
{
    public class VoteOption
    {
        public string Label;
        public List<string> LevelNames;
    }

    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class VoteNextMapPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "Empress.VoteNextMap";
        public const string PluginName = "Vote Next Map";
        public const string PluginVersion = "1.5.2";

        internal static VoteNextMapPlugin Instance;
        internal static Harmony Harmony;

        private const string AsciiBanner = @"
/*  ██████╗ ███╗   ███╗███╗   ██╗██╗                           */
/* ██╔═══██╗████╗ ████║████╗  ██║██║                           */
/* ██║   ██║██╔████╔██║██╔██╗ ██║██║                           */
/* ██║   ██║██║╚██╔╝██║██║╚██╗██║██║                           */
/* ╚██████╔╝██║ ╚═╝ ██║██║ ╚████║██║                           */
/*  ╚═════╝ ╚═╝     ╚═╝╚═╝  ╚═══╝╚═╝                           */
/*                                                             */
/* ███████╗███╗   ███╗██████╗ ██████╗ ███████╗███████╗███████╗ */
/* ██╔════╝████╗ ████║██╔══██╗██╔══██╗██╔════╝██╔════╝██╔════╝ */
/* █████╗  ██╔████╔██║██████╔╝██████╔╝█████╗  ███████╗███████╗ */
/* ██╔══╝  ██║╚██╔╝██║██╔═══╝ ██╔══██╗██╔══╝  ╚════██║╚════██║ */
/* ███████╗██║ ╚═╝ ██║██║     ██║  ██║███████╗███████║███████║ */
/* ╚══════╝╚═╝     ╚═╝╚═╝     ╚═╝  ╚═╝╚══════╝╚══════╝╚══════╝ */
";

        internal static ConfigEntry<string> Mode;
        internal static ConfigEntry<int> VoteDurationSeconds;
        internal static ConfigEntry<string> TiebreakRule;
        internal static ConfigEntry<bool> AllowAbstain;
        internal static ConfigEntry<bool> FailsafeAdvanceIfError;
        internal static ConfigEntry<int> MinPlayersToStartVote;
        internal static ConfigEntry<bool> AutoPopulateWhenEmpty;

        internal static ConfigEntry<float> RandomizerDuration;
        internal static ConfigEntry<float> RandomizerCyclesPerSec;
        internal static ConfigEntry<float> RandomizerEaseOut;
        internal static ConfigEntry<float> TickPitch;
        internal static ConfigEntry<bool> IncludeShopInRandomizer;
        internal static ConfigEntry<int> RandomizerRunLevelMin;
        internal static ConfigEntry<int> RandomizerRunLevelMax;

        internal static readonly List<VoteOption> Options = new();

        void Awake()
        {
            Instance = this;
            Harmony = new Harmony(PluginGuid);
            Logger.LogInfo(AsciiBanner);

            Mode = Config.Bind("General", "Mode", "Random", "Random | Vote");
            VoteDurationSeconds = Config.Bind("Vote", "VoteDurationSeconds", 15, "How long the vote stays open.");
            TiebreakRule = Config.Bind("Vote", "TiebreakRule", "Host", "Tiebreak rule: Host | Random");
            AllowAbstain = Config.Bind("Vote", "AllowAbstain", false, "If true, players can abstain.");
            FailsafeAdvanceIfError = Config.Bind("General", "FailsafeAdvanceIfError", true, "If feature cannot run, fall back to vanilla.");
            MinPlayersToStartVote = Config.Bind("General", "MinPlayersToStartVote", 1, "Minimum players required to open.");
            AutoPopulateWhenEmpty = Config.Bind("General", "AutoPopulateWhenEmpty", true, "If true and no options are configured, build one option per playable Level from RunManager.levels.");

            RandomizerDuration = Config.Bind("Randomizer", "Duration", 3.5f, "Total spin duration seconds.");
            RandomizerCyclesPerSec = Config.Bind("Randomizer", "CyclesPerSecond", 8f, "Approx cycles/sec at peak speed.");
            RandomizerEaseOut = Config.Bind("Randomizer", "EaseOutPortion", 0.33f, "Portion of time spent slowing down (0..1).");
            TickPitch = Config.Bind("Randomizer", "TickPitch", 1.0f, "Pitch multiplier for tick sound.");
            IncludeShopInRandomizer = Config.Bind("Randomizer", "IncludeShopInRandomizer", true, "Add Shop as an option if available.");
            RandomizerRunLevelMin = Config.Bind("Randomizer", "RunLevelMin", 1, "Minimum run level (inclusive).");
            RandomizerRunLevelMax = Config.Bind("Randomizer", "RunLevelMax", 20, "Maximum run level (inclusive).");

            LoadOptionBlock("Option1");
            LoadOptionBlock("Option2");
            LoadOptionBlock("Option3");
            LoadOptionBlock("Option4");
            LoadOptionBlock("Option5");

            Harmony.PatchAll(Assembly.GetExecutingAssembly());
            Logger.LogInfo($"{PluginName} {PluginVersion} loaded with {Options.Count} options (Mode={Mode.Value}, AutoPopulate={AutoPopulateWhenEmpty.Value}).");
        }

        private void LoadOptionBlock(string section)
        {
            var enabled = Config.Bind(section, "Enabled", false, "Enable this option.").Value;
            if (!enabled) return;

            var label = Config.Bind(section, "Label", section, "Label shown in the UI.").Value?.Trim();
            var levelsCsv = Config.Bind(section, "Levels", string.Empty, "Comma-separated Level names as in RunManager.levels list").Value ?? string.Empty;
            var names = levelsCsv.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                                 .Select(s => s.Trim())
                                 .Where(s => !string.IsNullOrEmpty(s))
                                 .Distinct(StringComparer.Ordinal)
                                 .ToList();
            if (string.IsNullOrEmpty(label) || names.Count == 0) return;

            Options.Add(new VoteOption { Label = label, LevelNames = names });
        }

        internal static bool EnsureOptions(out string error)
        {
            error = null;
            if (Options.Count == 0 && (AutoPopulateWhenEmpty?.Value ?? true))
            {
                TryAutoPopulateOptions();
            }
            if (Options.Count == 0)
            {
                error = "No options configured.";
                return false;
            }
            if (PhotonNetwork.InRoom && PhotonNetwork.PlayerList.Length < MinPlayersToStartVote.Value)
            {
                error = "Not enough players.";
                return false;
            }
            return true;
        }

        internal static void ForceVanillaAdvance(string reason)
        {
            Instance.Logger.LogWarning($"[VoteNextMap] Fallback to vanilla ChangeLevel: {reason}");
            try { RunManager.instance.ChangeLevel(_completedLevel: true, _levelFailed: false); }
            catch (Exception ex) { Instance.Logger.LogError($"Vanilla fallback failed: {ex}"); }
        }

        private static void TryAutoPopulateOptions()
        {
            try
            {
                var rm = RunManager.instance;
                if (!rm || rm.levels == null) return;

                Options.Clear();

                foreach (var lvl in rm.levels)
                {
                    if (!lvl) continue;
                    var n = lvl.name ?? string.Empty;
                    if (IsUtilityLevelName(n)) continue;
                    Options.Add(new VoteOption { Label = CleanLabel(n), LevelNames = new List<string> { n } });
                }

                if ((IncludeShopInRandomizer?.Value ?? true) && rm.levelShop)
                {
                    Options.Add(new VoteOption
                    {
                        Label = "Shop",
                        LevelNames = new List<string> { rm.levelShop.name }
                    });
                }

                Instance.Logger.LogInfo($"[AutoPopulate] Built {Options.Count} options from RunManager.levels (IncludeShop={IncludeShopInRandomizer.Value}).");
            }
            catch (Exception ex) { Instance.Logger.LogWarning($"[AutoPopulate] Failed: {ex}"); }
        }

        private static bool IsUtilityLevelName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string[] bad = { "Lobby", "Menu", "MainMenu", "Recording", "Splash" };
            return bad.Any(b => name.IndexOf(b, StringComparison.OrdinalIgnoreCase) >= 0);
        }
        private static string CleanLabel(string levelName) => levelName.Replace("Level - ", string.Empty).Trim();
    }

    [HarmonyPatch(typeof(TruckScreenText), nameof(TruckScreenText.GotoNextLevel))]
    public static class Patch_TruckScreen_GotoNextLevel
    {
        static bool Prefix()
        {
            if (!VoteNextMapPlugin.EnsureOptions(out var err))
            {
                if (VoteNextMapPlugin.FailsafeAdvanceIfError.Value)
                    VoteNextMapPlugin.ForceVanillaAdvance(err ?? "unknown");
                return false;
            }

            var vm = VoteManager.Ensure();
            if (!vm)
            {
                if (VoteNextMapPlugin.FailsafeAdvanceIfError.Value)
                    VoteNextMapPlugin.ForceVanillaAdvance("No VoteManager");
                return false;
            }

            if (!PhotonNetwork.IsMasterClient) return false;

            if (string.Equals(VoteNextMapPlugin.Mode.Value, "Random", StringComparison.OrdinalIgnoreCase))
            {
                vm.StartRandomizer(VoteNextMapPlugin.Options,
                                   VoteNextMapPlugin.RandomizerDuration.Value,
                                   VoteNextMapPlugin.RandomizerCyclesPerSec.Value,
                                   VoteNextMapPlugin.RandomizerEaseOut.Value,
                                   VoteNextMapPlugin.TickPitch.Value,
                                   VoteNextMapPlugin.RandomizerRunLevelMin.Value,
                                   VoteNextMapPlugin.RandomizerRunLevelMax.Value);
            }
            else
            {
                vm.StartNetworkedVote(VoteNextMapPlugin.Options,
                                      VoteNextMapPlugin.VoteDurationSeconds.Value,
                                      VoteNextMapPlugin.AllowAbstain.Value,
                                      VoteNextMapPlugin.TiebreakRule.Value);
            }
            return false;
        }
    }

    public class VoteManager : MonoBehaviourPun, IOnEventCallback
    {
        internal static VoteManager Instance;
        public bool IsVoteActive { get; private set; }
        public bool IsRandomizing { get; private set; }

        private readonly Dictionary<int, int> _votesByActor = new();
        private readonly Dictionary<int, int> _tally = new();
        private List<VoteOption> _currentOptions = new();
        private bool _allowAbstain;
        private string _tiebreakRule = "Host";
        private float _timeLeft;
        private int _localChoice = int.MinValue;

        private System.Random _rng;
        private float _randDuration;
        private float _randCyclesPerSec;
        private float _randEaseOut;
        private int _randWinnerIndex;
        private double _randStartTime;
        private int _lastTickIndex = -1;
        private float _tickPitch = 1f;
        private int _randRunLevel = 1;
        private bool _randLanding;
        private bool _randSentClose;
        private bool _finalBeepPlayed;
        private const float FLASH_DURATION = 0.85f;

        private AudioSource _audio;
        private AudioClip _tickClip;
        private AudioClip _finalClip;

        private bool _cursorForced;
        private CursorLockMode _priorLock;
        private bool _priorVisible;

        private const byte EV_OPEN_VOTE = 41;
        private const byte EV_VOTE = 42;
        private const byte EV_CLOSE_VOTE = 43;
        private const byte EV_OPEN_RAND = 44;
        private const byte EV_CLOSE_RAND = 45;

        public static VoteManager Ensure()
        {
            if (Instance) return Instance;
            var go = new GameObject("Empress_VoteManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            Instance = go.AddComponent<VoteManager>();
            return Instance;
        }

        void OnEnable() { PhotonNetwork.AddCallbackTarget(this); EnsureAudio(); }
        void OnDisable() { PhotonNetwork.RemoveCallbackTarget(this); }

        private void EnsureAudio()
        {
            if (_audio) return;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _audio.volume = 0.40f;
            _tickClip = MakeBeepClip(0.05f, 350f);
            _finalClip = MakeBeepClip(0.12f, 550f);
        }

        public void StartRandomizer(List<VoteOption> options, float duration, float cyclesPerSec, float easeOut, float tickPitch, int runLevelMin, int runLevelMax)
        {
            if (IsRandomizing || IsVoteActive) return;
            if (!PhotonNetwork.InRoom) { Debug.LogWarning("[VoteNextMap] Not in room; cannot start randomizer."); return; }

            PhotonNetwork.RaiseEvent(EV_OPEN_RAND, null, new RaiseEventOptions { CachingOption = EventCaching.RemoveFromRoomCache }, SendOptions.SendReliable);

            int min = Math.Min(runLevelMin, runLevelMax);
            int max = Math.Max(runLevelMin, runLevelMax);
            if (max < 1) { min = 1; max = 1; }

            int seed = unchecked(
                PhotonNetwork.ServerTimestamp ^
                Environment.TickCount ^
                (PhotonNetwork.LocalPlayer != null ? PhotonNetwork.LocalPlayer.ActorNumber : 0) ^
                Guid.NewGuid().GetHashCode()
            );

            var rng = new System.Random(seed);
            int winner = options.Count > 0 ? rng.Next(0, options.Count) : 0;
            int runLevel = rng.Next(min, max + 1);

            string[] labels = options.Select(o => o.Label).ToArray();
            string[] levelGroups = options.Select(o => string.Join("|", o.LevelNames ?? new List<string>())).ToArray();
            object[] content = { labels, levelGroups, duration, cyclesPerSec, easeOut, seed, winner, tickPitch, runLevel };
            var opts = new RaiseEventOptions { Receivers = ReceiverGroup.All, CachingOption = EventCaching.AddToRoomCache };
            PhotonNetwork.RaiseEvent(EV_OPEN_RAND, content, opts, SendOptions.SendReliable);
        }

        public void StartNetworkedVote(List<VoteOption> options, int durationSeconds, bool allowAbstain, string tiebreakRule)
        {
            if (IsVoteActive || IsRandomizing) return;
            if (!PhotonNetwork.InRoom) { Debug.LogWarning("[VoteNextMap] Not in room; cannot start vote."); return; }

            string[] labels = options.Select(o => o.Label).ToArray();
            string[] levelGroups = options.Select(o => string.Join("|", o.LevelNames ?? new List<string>())).ToArray();
            object[] content = { labels, levelGroups, durationSeconds, allowAbstain, tiebreakRule ?? "Host" };
            var opts = new RaiseEventOptions { Receivers = ReceiverGroup.All, CachingOption = EventCaching.AddToRoomCache };
            PhotonNetwork.RaiseEvent(EV_OPEN_VOTE, content, opts, SendOptions.SendReliable);
        }

        public void OnEvent(EventData photonEvent)
        {
            try
            {
                switch (photonEvent.Code)
                {
                    case EV_OPEN_VOTE:
                        {
                            var data = (object[])photonEvent.CustomData;
                            var labels = (string[])data[0];
                            var levelGroups = (string[])data[1];
                            int duration = Convert.ToInt32(data[2]);
                            bool allowAbstain = (bool)data[3];
                            string tiebreakRule = (string)data[4];
                            OpenLocalVote(labels, levelGroups, duration, allowAbstain, tiebreakRule);
                            break;
                        }
                    case EV_VOTE:
                        {
                            if (!PhotonNetwork.IsMasterClient) break;
                            var data = (object[])photonEvent.CustomData;
                            int actor = (int)data[0];
                            int choice = (int)data[1];
                            if (!IsVoteActive) break;
                            _votesByActor[actor] = choice;
                            RecountTally();
                            break;
                        }
                    case EV_CLOSE_VOTE:
                        {
                            var data = (object[])photonEvent.CustomData;
                            int winnerIdx = (int)data[0];
                            string winnerLabel = (string)data[1];
                            CloseLocalVote(winnerIdx, winnerLabel);
                            break;
                        }
                    case EV_OPEN_RAND:
                        {
                            var d = (object[])photonEvent.CustomData;
                            var labels = (string[])d[0];
                            var levelGroups = (string[])d[1];
                            float duration = Convert.ToSingle(d[2]);
                            float cyclesPerSec = Convert.ToSingle(d[3]);
                            float easeOut = Convert.ToSingle(d[4]);
                            int seed = (int)d[5];
                            int winner = (int)d[6];
                            float tickPitch = Convert.ToSingle(d[7]);
                            int runLevel = (int)d[8];
                            OpenLocalRandom(labels, levelGroups, duration, cyclesPerSec, easeOut, seed, winner, tickPitch, runLevel);
                            break;
                        }
                    case EV_CLOSE_RAND:
                        {
                            var data = (object[])photonEvent.CustomData;
                            int winnerIdx = (int)data[0];
                            string winnerLabel = (string)data[1];
                            CloseLocalRandom(winnerIdx, winnerLabel);
                            break;
                        }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VoteNextMap] OnEvent error: {ex}");
            }
        }

        private void OpenLocalVote(string[] optionLabels, string[] levelGroups, int durationSeconds, bool allowAbstain, string tiebreakRule)
        {
            if (IsVoteActive || IsRandomizing) return;
            IsVoteActive = true;
            _allowAbstain = allowAbstain;
            _tiebreakRule = tiebreakRule ?? "Host";
            _timeLeft = Mathf.Max(5, durationSeconds);

            _currentOptions = new List<VoteOption>();
            _tally.Clear();
            for (int i = 0; i < optionLabels.Length; i++)
            {
                var names = (i < levelGroups.Length && !string.IsNullOrEmpty(levelGroups[i])) ? levelGroups[i].Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList() : new List<string>();
                _currentOptions.Add(new VoteOption { Label = optionLabels[i], LevelNames = names });
                _tally[i] = 0;
            }

            _localChoice = int.MinValue;
            ForceCursor(true);
            if (PhotonNetwork.IsMasterClient) StartCoroutine(HostCountdownVote());
        }

        private void CloseLocalVote(int winnerIndex, string winnerLabel)
        {
            if (!IsVoteActive) return;
            IsVoteActive = false;
            ForceCursor(false);

            if (PhotonNetwork.IsMasterClient)
            {
                TryForceLevelFromWinner(winnerIndex, winnerLabel, null);
            }
        }

        private IEnumerator HostCountdownVote()
        {
            while (_timeLeft > 0f && IsVoteActive)
            {
                _timeLeft -= Time.deltaTime;
                yield return null;
            }
            if (!IsVoteActive) yield break;

            var result = ComputeWinner();
            object[] content = { result.winnerIndex, result.winnerLabel };
            PhotonNetwork.RaiseEvent(EV_CLOSE_VOTE, content, new RaiseEventOptions { Receivers = ReceiverGroup.All }, SendOptions.SendReliable);
            PhotonNetwork.RaiseEvent(EV_OPEN_VOTE, null, new RaiseEventOptions { CachingOption = EventCaching.RemoveFromRoomCache }, SendOptions.SendReliable);
        }

        private (int winnerIndex, string winnerLabel) ComputeWinner()
        {
            RecountTally();
            int maxVotes = -1;
            List<int> leaders = new();
            foreach (var kv in _tally)
            {
                if (kv.Value > maxVotes) { leaders.Clear(); leaders.Add(kv.Key); maxVotes = kv.Value; }
                else if (kv.Value == maxVotes) { leaders.Add(kv.Key); }
            }
            if (leaders.Count == 1) return (leaders[0], _currentOptions[leaders[0]].Label);

            if (string.Equals(_tiebreakRule, "Host", StringComparison.OrdinalIgnoreCase))
            {
                var hostActor = PhotonNetwork.MasterClient.ActorNumber;
                if (_votesByActor.TryGetValue(hostActor, out var hostChoice) && leaders.Contains(hostChoice))
                    return (hostChoice, _currentOptions[hostChoice].Label);
                var pick = leaders[0];
                return (pick, _currentOptions[pick].Label);
            }
            else
            {
                var pick = leaders[UnityEngine.Random.Range(0, leaders.Count)];
                return (pick, _currentOptions[pick].Label);
            }
        }

        private void RecountTally()
        {
            _tally.Clear();
            for (int i = 0; i < _currentOptions.Count; i++) _tally[i] = 0;
            foreach (var v in _votesByActor.Values)
            {
                if (v >= 0 && v < _currentOptions.Count) _tally[v]++;
            }
        }

        private void OpenLocalRandom(string[] optionLabels, string[] levelGroups, float duration, float cyclesPerSec, float easeOut, int seed, int winnerIndex, float tickPitch, int runLevel)
        {
            if (IsRandomizing || IsVoteActive) return;
            IsRandomizing = true;

            _currentOptions = new List<VoteOption>();
            for (int i = 0; i < optionLabels.Length; i++)
            {
                var names = (i < levelGroups.Length && !string.IsNullOrEmpty(levelGroups[i])) ? levelGroups[i].Split('|').Select(s => s.Trim()).Where(s => s.Length > 0).ToList() : new List<string>();
                _currentOptions.Add(new VoteOption { Label = optionLabels[i], LevelNames = names });
            }

            _rng = new System.Random(seed);
            _randDuration = Mathf.Max(1.5f, duration);
            _randCyclesPerSec = Mathf.Max(1f, cyclesPerSec);
            _randEaseOut = Mathf.Clamp01(easeOut);
            _randWinnerIndex = Mathf.Clamp(winnerIndex, 0, Mathf.Max(0, _currentOptions.Count - 1));
            _randRunLevel = Mathf.Max(1, runLevel);
            _randStartTime = Time.timeAsDouble;
            _lastTickIndex = -1;
            _tickPitch = Mathf.Clamp(tickPitch, 0.25f, 3f);
            _randLanding = false;
            _randSentClose = false;
            _finalBeepPlayed = false;
            EnsureAudio();
        }

        private void CloseLocalRandom(int winnerIndex, string winnerLabel)
        {
            if (!IsRandomizing) return;
            IsRandomizing = false;

            if (PhotonNetwork.IsMasterClient)
            {
                TryForceLevelFromWinner(winnerIndex, winnerLabel, _randRunLevel);
            }
        }

        private static void FillRect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private static Rect Inset(Rect r, float px) => new Rect(r.x + px, r.y + px, r.width - px * 2f, r.height - px * 2f);

        void OnGUI()
        {
            if (!IsVoteActive && !IsRandomizing) return;
            GUI.depth = -1000;

            var full = new Rect(0, 0, Screen.width, Screen.height);
            FillRect(full, new Color(0, 0, 0, 0.60f));

            float panelW = Mathf.Min(820f, Screen.width - 120f);
            float panelH = Mathf.Min(580f, Screen.height - 140f);
            var panel = new Rect((Screen.width - panelW) / 2f, (Screen.height - panelH) / 2f, panelW, panelH);

            FillRect(new Rect(panel.x + 8f, panel.y + 10f, panel.width, panel.height), new Color(0, 0, 0, 0.35f));
            FillRect(panel, new Color(0.06f, 0.07f, 0.10f, 0.95f));
            FillRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(1f, 1f, 1f, 0.05f));
            FillRect(new Rect(panel.x, panel.yMax - 2f, panel.width, 2f), new Color(0f, 0f, 0f, 0.35f));

            var header = new Rect(panel.x, panel.y, panel.width, 38f);
            FillRect(header, new Color(0.10f, 0.12f, 0.20f, 1f));
            FillRect(new Rect(header.x, header.y, header.width, 18f), new Color(0.14f, 0.16f, 0.28f, 0.85f));
            var titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 16 };
            GUI.Label(header, IsRandomizing ? "RANDOM SELECT" : "VOTE: Choose the next map type", titleStyle);

            if (IsVoteActive)
            {
                DrawVoteUI(panel);
            }
            else if (IsRandomizing)
            {
                DrawRandomizerUI(panel);
            }
        }

        private void DrawVoteUI(Rect panel)
        {
            string t = System.TimeSpan.FromSeconds(Mathf.Max(0f, _timeLeft)).ToString(@"mm\:ss");
            var timerRect = new Rect(panel.x, panel.y + 40f, panel.width, 22f);
            var timerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            GUI.Label(timerRect, $"Time left: {t}", timerStyle);

            float innerX = panel.x + 18f;
            float innerY = panel.y + 68f;
            float innerW = panel.width - 36f;
            var inner = new Rect(innerX, innerY, innerW, panel.height - 122f);
            int cols = 2;
            int total = _currentOptions.Count + (_allowAbstain ? 1 : 0);
            float cellW = (inner.width - (cols - 1) * 10f) / cols;
            float cellH = 48f;

            int drawIndex = 0;
            for (int i = 0; i < _currentOptions.Count; i++)
            {
                int r = drawIndex / cols;
                int c = drawIndex % cols;
                var rct = new Rect(inner.x + c * (cellW + 10f), inner.y + r * (cellH + 8f), cellW, cellH);
                int count = _tally.TryGetValue(i, out var cc) ? cc : 0;
                string baseLabel = _currentOptions[i].Label;
                string label = _localChoice == i ? $"> {baseLabel} — {count}" : $"{baseLabel} — {count}";
                if (GUI.Button(rct, label))
                {
                    _localChoice = i;
                    object[] content = { PhotonNetwork.LocalPlayer.ActorNumber, i };
                    PhotonNetwork.RaiseEvent(EV_VOTE, content, new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);
                }
                drawIndex++;
            }

            if (_allowAbstain)
            {
                int r = drawIndex / cols;
                int c = drawIndex % cols;
                var rct = new Rect(inner.x + c * (cellW + 10f), inner.y + r * (cellH + 8f), cellW, cellH);
                if (GUI.Button(rct, _localChoice == -1 ? "> Abstain" : "Abstain"))
                {
                    _localChoice = -1;
                    object[] content = { PhotonNetwork.LocalPlayer.ActorNumber, -1 };
                    PhotonNetwork.RaiseEvent(EV_VOTE, content, new RaiseEventOptions { Receivers = ReceiverGroup.MasterClient }, SendOptions.SendReliable);
                }
            }

            var closeRect = new Rect(panel.x + (panel.width - 200f) / 2f, panel.yMax - 44f, 200f, 30f);
            GUI.enabled = PhotonNetwork.IsMasterClient;
            if (GUI.Button(closeRect, PhotonNetwork.IsMasterClient ? "Host: Close Now" : "Waiting for Host…"))
            {
                if (PhotonNetwork.IsMasterClient)
                {
                    var res = ComputeWinner();
                    object[] content = { res.winnerIndex, res.winnerLabel };
                    PhotonNetwork.RaiseEvent(EV_CLOSE_VOTE, content, new RaiseEventOptions { Receivers = ReceiverGroup.All }, SendOptions.SendReliable);
                    PhotonNetwork.RaiseEvent(EV_OPEN_VOTE, null, new RaiseEventOptions { CachingOption = EventCaching.RemoveFromRoomCache }, SendOptions.SendReliable);
                }
            }
            GUI.enabled = true;
        }

        private void DrawRandomizerUI(Rect panel)
        {
            float innerX = panel.x + 18f;
            float innerY = panel.y + 56f;
            float innerW = panel.width - 36f;
            float innerH = panel.height - 110f;
            var inner = new Rect(innerX, innerY, innerW, innerH);

            int cols = Mathf.Clamp(_currentOptions.Count >= 8 ? 3 : 2, 1, 4);
            float cellW = (inner.width - (cols - 1) * 14f) / cols;
            float cellH = 76f;

            double t = Math.Max(0.0, Time.timeAsDouble - _randStartTime);
            double T = _randDuration;
            double mid = T * (1.0 - _randEaseOut);
            double idxF;
            if (t <= mid)
            {
                double prog = t / Math.Max(0.0001, mid);
                double speed = _randCyclesPerSec * (0.5 + 0.5 * prog);
                idxF = speed * t * _currentOptions.Count;
            }
            else
            {
                double prog = (t - mid) / Math.Max(0.0001, T - mid);
                prog = Math.Min(1.0, prog);
                double startIdx = _currentOptions.Count * _randCyclesPerSec * mid;
                double endIdx = startIdx + 2 * _currentOptions.Count + (_randWinnerIndex - (int)(startIdx % _currentOptions.Count) + _currentOptions.Count) % _currentOptions.Count;
                double eased = 1 - Math.Pow(1 - prog, 3);
                idxF = startIdx + (endIdx - startIdx) * eased;
            }
            int hi = _currentOptions.Count > 0 ? (int)Math.Abs(idxF) % _currentOptions.Count : 0;

            if (t < T && hi != _lastTickIndex)
            {
                _lastTickIndex = hi;
                if (_audio && _tickClip)
                {
                    _audio.pitch = _tickPitch * UnityEngine.Random.Range(0.95f, 1.05f);
                    _audio.PlayOneShot(_tickClip);
                }
            }

            if (t >= T && !_randLanding)
            {
                _randLanding = true;
                _lastTickIndex = _randWinnerIndex;
                if (!_finalBeepPlayed && _audio && _finalClip)
                {
                    _finalBeepPlayed = true;
                    _audio.pitch = 1f;
                    _audio.PlayOneShot(_finalClip);
                }
                if (PhotonNetwork.IsMasterClient && !_randSentClose)
                {
                    _randSentClose = true;
                    StartCoroutine(LandAndClose());
                }
            }

            for (int i = 0; i < _currentOptions.Count; i++)
            {
                int r = i / cols;
                int c = i % cols;

                var rct = new Rect(inner.x + c * (cellW + 14f), inner.y + r * (cellH + 12f), cellW, cellH);

                bool isWinner = (i == _randWinnerIndex);
                bool isLit = (i == hi) || (_randLanding && isWinner);

                if (_randLanding && isWinner)
                {
                    float pulse = 1f + 0.04f * Mathf.Sin(Time.time * 12f);
                    float dx = rct.width * (pulse - 1f) / 2f;
                    float dy = rct.height * (pulse - 1f) / 2f;
                    rct = new Rect(rct.x - dx, rct.y - dy, rct.width * pulse, rct.height * pulse);
                }

                FillRect(new Rect(rct.x + 4f, rct.y + 6f, rct.width, rct.height), new Color(0, 0, 0, 0.35f));
                FillRect(rct, isLit ? new Color(0.98f, 0.82f, 0.22f, 1f) : new Color(0.10f, 0.12f, 0.16f, 1f));
                FillRect(new Rect(rct.x, rct.y, rct.width, 2f), new Color(1f, 1f, 1f, isLit ? 0.18f : 0.08f));
                FillRect(new Rect(rct.x, rct.yMax - 2f, rct.width, 2f), new Color(0f, 0f, 0f, 0.35f));

                var inset = Inset(rct, 3f);
                FillRect(inset, isLit ? new Color(0.16f, 0.12f, 0.05f, 0.95f) : new Color(0.04f, 0.05f, 0.07f, 0.95f));

                FillRect(new Rect(inset.x, inset.y, inset.width, 16f), new Color(1f, 1f, 1f, isLit ? 0.06f : 0.03f));

                var labelStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperCenter,
                    fontStyle = isLit ? FontStyle.Bold : FontStyle.Normal,
                    fontSize = 15
                };
                GUI.Label(new Rect(inset.x, inset.y + 6f, inset.width, 22f), _currentOptions[i].Label, labelStyle);

                var pill = new Rect(inset.x + (inset.width - 56f) / 2f, inset.y + inset.height - 24f, 56f, 18f);
                FillRect(pill, new Color(0.20f, 0.42f, 0.26f, 0.95f));
                FillRect(new Rect(pill.x, pill.y, pill.width, 2f), new Color(1f, 1f, 1f, 0.10f));
                FillRect(new Rect(pill.x, pill.yMax - 2f, pill.width, 2f), new Color(0f, 0f, 0f, 0.25f));
                var subStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Bold };
                GUI.Label(pill, $"Lv {_randRunLevel}", subStyle);

                if (_randLanding && isWinner)
                {
                    float a = 0.35f + 0.35f * Mathf.PingPong(Time.time * 4f, 1f);
                    Color glow = new Color(1f, 1f, 1f, a);
                    var border1 = Inset(rct, -2f);
                    var border2 = Inset(rct, -4f);
                    DrawBorder(border1, glow, 2f);
                    DrawBorder(border2, new Color(1f, 0.95f, 0.6f, a * 0.6f), 2f);
                }
            }

            var foot = new Rect(panel.x, panel.yMax - 42f, panel.width, 24f);
            var footStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12, fontStyle = FontStyle.Italic };
            GUI.Label(foot, $"Run Level: {_randRunLevel}", footStyle);
        }

        private static void DrawBorder(Rect r, Color c, float thickness)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private IEnumerator LandAndClose()
        {
            yield return new WaitForSeconds(FLASH_DURATION);

            var label = _currentOptions[_randWinnerIndex].Label;
            object[] content = { _randWinnerIndex, label };
            PhotonNetwork.RaiseEvent(EV_CLOSE_RAND, content, new RaiseEventOptions { Receivers = ReceiverGroup.All }, SendOptions.SendReliable);
            PhotonNetwork.RaiseEvent(EV_OPEN_RAND, null, new RaiseEventOptions { CachingOption = EventCaching.RemoveFromRoomCache }, SendOptions.SendReliable);
        }

        private AudioClip MakeBeepClip(float seconds, float freq)
        {
            int rate = 44100;
            int samples = Mathf.CeilToInt(rate * seconds);
            var clip = AudioClip.Create("Empress_Tone", samples, 1, rate, false);
            float[] data = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)rate;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * t) * Mathf.Exp(-3.2f * t);
            }
            clip.SetData(data, 0);
            return clip;
        }

        private void ForceCursor(bool on)
        {
            try
            {
                if (on)
                {
                    if (!_cursorForced)
                    {
                        _priorLock = Cursor.lockState;
                        _priorVisible = Cursor.visible;
                        _cursorForced = true;
                    }
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
                else if (_cursorForced)
                {
                    Cursor.lockState = _priorLock;
                    Cursor.visible = _priorVisible;
                    _cursorForced = false;
                }
            }
            catch { }
        }

        private void TryForceLevelFromWinner(int winnerIndex, string winnerLabel, int? levelsCompletedOverride)
        {
            try
            {
                var rm = RunManager.instance;
                if (!rm)
                {
                    VoteNextMapPlugin.ForceVanillaAdvance("RunManager missing");
                    return;
                }

                Level chosen = null;
                if (winnerIndex >= 0 && winnerIndex < _currentOptions.Count)
                {
                    var names = _currentOptions[winnerIndex].LevelNames;
                    foreach (var n in names)
                    {
                        var match = rm.levels.FirstOrDefault(l => l && l.name == n);
                        if (match) { chosen = match; break; }
                    }
                    if (!chosen)
                    {
                        var pool = new List<Level> { rm.levelLobby, rm.levelLobbyMenu, rm.levelShop, rm.levelTutorial, rm.levelArena, rm.levelMainMenu, rm.levelRecording, rm.levelSplashScreen };
                        chosen = pool.FirstOrDefault(l => l && names.Contains(l.name));
                    }
                }

                if (!chosen)
                {
                    VoteNextMapPlugin.ForceVanillaAdvance("Winner option has no resolvable Level");
                    return;
                }

                int lvlCompleted = levelsCompletedOverride ?? rm.levelsCompleted;

                var pun = rm.runManagerPUN;
                if (pun && pun.photonView)
                {
                    pun.photonView.RPC("UpdateLevelRPC", RpcTarget.OthersBuffered, chosen.name, lvlCompleted, false);
                }
                rm.UpdateLevel(chosen.name, lvlCompleted, false);
                rm.RestartScene();
            }
            catch (Exception ex)
            {
                Debug.LogError($"[VoteNextMap] Force level failed: {ex}");
                VoteNextMapPlugin.ForceVanillaAdvance("Exception during force level");
            }
        }
    }
}