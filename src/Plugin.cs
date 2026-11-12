using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using BepInEx;
using HarmonyLib;
using Interactable_Infos.Common;
using Interactable_Infos.Sealed_SO_Classes.Environment;
using Managers___Handlers;
using RSServiceLocator.Scripts;
using Runtime_Classes.Environments;
using Runtime_Classes.Common;
using Sonity;
using UnityEngine;
using UnityEngine.UIElements;

namespace RadioRamen;

[BepInPlugin(Id, "RadioRamen", Version)]
[BepInProcess("Ramen Shop Simulator.exe")]
[BepInDependency(StartupBadge.RelaxedId, BepInDependency.DependencyFlags.SoftDependency)]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Id = "com.darojax.ramenshop.radio", Version = "0.1.11";
    internal static Plugin? Instance;
    internal RadioSettings Settings = new();
    internal readonly RadioBrowseState Browsing = new();
    internal List<Station> Stations = new();
    internal List<string> Tracks = new();
    internal string Folder = "", Status = "Choose a station or an MP3 track.";
    internal string MusicFolder => Path.Combine(Folder, "Music");
    private string UserLibraryPath => Path.Combine(Folder, UserStationLibrary.FileName);
    private UserStationLibrary _userStations = new();
    private string _stationId = "";
    internal string CurrentStationId
    {
        get { var playing = Playing; return playing?.Local == false && Stations.Any(s => s.Id == _stationId && s.Stream == playing.Source) ? _stationId : ""; }
    }
    internal bool IsFavourite(string id) => _userStations.Favourites.Contains(id);
    internal bool IsOpen => _panel != null;
    internal Playback? Playing => _decoder?.Current;
    internal string PlaybackMarker
    {
        get
        {
            var playback = Playing;
            if (playback == null) return "";
            if (playback.Failure.Length > 0) return "Unavailable";
            if (_sourcePaused) return "Paused";
            if (!_startedClip || playback.Format != _format || playback.Status == "Reconnecting...") return "Connecting";
            return !_source || _source.volume <= 0 ? "Muted" : "Playing";
        }
    }
    internal double MusicPosition => _clock.Elapsed(AudioSettings.dspTime);
    private Decoder? _decoder;
    private Harmony? _harmony;
    private RadioPanel? _panel;
    private RuntimeEnvironment_Radio? _radio;
    private SoundEvent? _originalSong;
    private AudioSource? _source;
    private AudioSource? _tuningSource;
    private AudioClip? _tuningClip;
    private AnimationCurve? _nearbyCurve;
    private AudioClip? _clip;
    private AudioFormat? _format;
    private GameObject? _speaker;
    private AudioLowPassFilter? _lowPass;
    private AudioHighPassFilter? _highPass;
    private AudioDistortionFilter? _distortion;
    private Playback? _playing;
    private int _track = -1, _openFrame = -1, _backFrame = -1;
    private readonly System.Random _random = new();
    private readonly MusicOrder _order = new();
    private State _oldState, _oldBeforeMenu;
    private bool _wasMovement, _wasBlur, _ready, _settingsDirty, _sourcePaused, _startedClip, _lastWasLocal;
    private readonly PlaybackClock _clock = new();
    private float _saveAt, _checkpointAt;
    private string _assemblyHash = "", _startupFailure = "", _lastPlaybackFailure = "", _lastTransport = "";
    private string _panelDetail = "";
    private bool _operatePromptApplied;
    private StartupBadge? _startupBadge;
    private string _compatibility = "Not checked";
    private static readonly FieldInfo Song = AccessTools.Field(typeof(RuntimeEnvironment_Radio), "CurrentSong");
    private static readonly MethodInfo HoverBody = AccessTools.PropertyGetter(typeof(UIService), "mouseoverBodyLabel");

    private void Awake()
    {
        Instance = this;
        try
        {
            Folder = Path.GetDirectoryName(Info.Location)!;
            foreach (string file in new[] { "RadioRamen.ini", "Stations.ini" })
                if (!File.Exists(Path.Combine(Folder, file)))
                {
                    using var stream = GetType().Assembly.GetManifestResourceStream("RadioRamen." + file);
                    using var reader = new StreamReader(stream ?? throw new InvalidDataException("Missing default " + file));
                    File.WriteAllText(Path.Combine(Folder, file), reader.ReadToEnd(), new System.Text.UTF8Encoding(false));
                }
            Settings = RadioSettings.Parse(File.ReadAllText(Path.Combine(Folder, "RadioRamen.ini")));
            _lastWasLocal = Settings.LastSource == SavedSource.Music;
            Browsing.Music = _lastWasLocal;
            if (!Settings.Enabled) { Logger.LogInfo("RadioRamen is disabled in its INI."); return; }
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll")))
                _assemblyHash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
            _compatibility = GameCompatibility.Inspect(Path.Combine(Paths.ManagedPath, "Assembly-CSharp.dll"), Info.Location).Summary;
            Directory.CreateDirectory(MusicFolder);
            RefreshLibraries();
            _decoder = new Decoder(Path.Combine(Paths.BepInExRootPath, "RadioRamen", "native"));
            _harmony = new Harmony(Id); _harmony.PatchAll(typeof(Plugin).Assembly);
            int hooks = _harmony.GetPatchedMethods().Count();
            if (hooks != GameCompatibility.ExpectedHooks) throw new InvalidOperationException("Expected all " + GameCompatibility.ExpectedHooks + " radio hooks; installed " + hooks + ".");
            _ready = true;
            Logger.LogInfo("RadioRamen " + Version + " ready: " + Stations.Count + " stations; " + _compatibility + ". E controls power; left click opens the player.");
        }
        catch (Exception ex)
        {
            _ready = false; _harmony?.UnpatchSelf(); _decoder?.Dispose(); _decoder = null;
            if (_compatibility == "Not checked") _compatibility = "Failed";
            _startupFailure = ex.GetType().Name + ": " + ex.Message;
            Logger.LogError("RadioRamen startup failed: " + ex);
        }
        finally { WriteStartupStatus(); }
    }

    private void OnGUI()
    {
        if (!_ready) return;
        (_startupBadge ??= new StartupBadge(Version)).Draw();
    }

    private void WriteStartupStatus()
    {
        try
        {
            string text = "RadioRamen " + Version + "\nLoaded UTC: " + DateTime.UtcNow.ToString("O")
                + "\nAssembly SHA-256: " + _assemblyHash + "\nActive: " + _ready + "; hooks: " + (_harmony?.GetPatchedMethods().Count() ?? 0)
                + "\nCompatibility: " + _compatibility
                + "\nStations: " + Stations.Count + "; custom: " + _userStations.Stations.Count + "; favourites: " + _userStations.Favourites.Count + "; local tracks: " + Tracks.Count + "\nMenu open: " + IsOpen
                + "\nTransport: " + (Playing?.Transport ?? "None") + "; MP3 position: " + MusicPosition.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "\nPlayback detail: " + (Playing?.Diagnostic ?? "")
                + "\nPanel: " + _panelDetail
                + "\nRadio hover wording: " + (_operatePromptApplied ? "operate applied" : "waiting for radio hover")
                + "\nAudible distance: " + (Settings.ShortRange ? "Nearby (fade to zero at " + RadioRange.NearbyMaximum + " game units)" : "Normal")
                + "\nStatus: " + (_startupFailure.Length > 0 ? _startupFailure : Settings.Enabled ? Status : "Disabled in INI") + "\n";
            File.WriteAllText(Path.Combine(Paths.ConfigPath, Id + ".status.txt"), text.Substring(0, Math.Min(text.Length, 4096)), new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) { Logger.LogWarning("Could not write radio startup status: " + ex.Message); }
    }

    internal void RefreshLibraries()
    {
        // Replace the catalogue only after a complete successful parse.
        var bundled = Station.Parse(File.ReadAllText(Path.Combine(Folder, "Stations.ini")));
        var user = UserStationLibrary.Load(UserLibraryPath);
        var stations = UserStationLibrary.Merge(bundled, user);
        var tracks = MusicLibrary.Scan(MusicFolder);
        string? currentTrack = Playing?.Local == true ? Playing.Source
            : tracks.FirstOrDefault(t => Path.GetFileName(t).Equals(Settings.LastTrack, StringComparison.OrdinalIgnoreCase));
        Stations = stations; Tracks = tracks; _userStations = user;
        _track = currentTrack == null ? -1 : Tracks.IndexOf(currentTrack); _order.Select(_track);
    }

    internal void SetFavourite(string id, bool favourite)
    {
        var bundled = Station.Parse(File.ReadAllText(Path.Combine(Folder, "Stations.ini")));
        var user = UserStationLibrary.Load(UserLibraryPath);
        var merged = UserStationLibrary.Merge(bundled, user);
        if (!merged.Any(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("This station was removed. Refresh the list.");
        user.SetFavourite(id, favourite); user.Save(UserLibraryPath);
        _userStations = user; Stations = merged; WriteStartupStatus();
    }

    internal Station SaveCustomStation(string? id, string name, string stream, string category, string region, bool favourite)
    {
        var bundled = Station.Parse(File.ReadAllText(Path.Combine(Folder, "Stations.ini")));
        var user = UserStationLibrary.Load(UserLibraryPath);
        var station = user.Upsert(id, name, stream, category, region, bundled);
        user.SetFavourite(station.Id, favourite);
        var merged = UserStationLibrary.Merge(bundled, user);
        user.Save(UserLibraryPath); _userStations = user; Stations = merged;
        if (Playing?.Local == false && _stationId == station.Id && Playing.Source == station.Stream) Playing.Title = station.Name;
        WriteStartupStatus(); return station;
    }

    internal void RemoveCustomStation(string id)
    {
        var bundled = Station.Parse(File.ReadAllText(Path.Combine(Folder, "Stations.ini")));
        var user = UserStationLibrary.Load(UserLibraryPath); user.Remove(id);
        var merged = UserStationLibrary.Merge(bundled, user);
        user.Save(UserLibraryPath); _userStations = user; Stations = merged;
        if (_stationId == id) Stop();
        if (Settings.LastStation == id) { Settings.LastStation = Stations.FirstOrDefault()?.Id ?? ""; Remember(); }
        WriteStartupStatus();
    }

    internal void Back()
    {
        if (_backFrame == Time.frameCount) return;
        _backFrame = Time.frameCount;
        if (_panel?.CloseTransient() == true) return;
        ClosePanel();
    }

    internal void NotePanel(string detail) { _panelDetail = detail; WriteStartupStatus(); }

    internal void ShowRadioPrompt(RuntimeInteractable origin)
    {
        if (!_ready || !(origin is RuntimeEnvironment_Radio) || PlayerState.GetCurrentPlayerState == State.creation) return;
        try
        {
            var label = HoverBody.Invoke(null, null) as Label;
            if (label == null || string.IsNullOrEmpty(origin.PopupDescriptionKey)) return;
            var localization = RSServices.Current.Get<LocalizationService>();
            string original = localization.GetLocalizedString(origin.PopupDescriptionKey);
            string replacement = RadioPromptText.Operate(original);
            if (replacement != original)
            {
                localization.SetNonLocalizedUIInfo(label, replacement);
                if (!_operatePromptApplied) { _operatePromptApplied = true; WriteStartupStatus(); }
            }
        }
        catch (Exception ex) { Logger.LogWarning("Could not update radio interaction wording: " + ex.Message); }
    }

    internal bool Open(EnvironmentInfo_Radio info, Transform? target = null)
    {
        if (!_ready) return false;
        if (IsOpen || _openFrame == Time.frameCount) return true;
        var radio = ResolveRadio(info, target);
        if (!radio || PlayerState.GetCurrentPlayerState != State.none) return true;
        _openFrame = Time.frameCount;
        try
        {
            AttachRadio(radio);
            if (radio.isOn && Playing == null) PlaySelected(_lastWasLocal);
            _oldState = PlayerState.GetCurrentPlayerState;
            _oldBeforeMenu = PauseMenuManager.Instance.stateBeforeMenu;
            _wasBlur = AccessTools.Field(typeof(UIService), "_nonUIToolkitBGBlur").GetValue(RSServices.Current!.Get<UIService>()) is UnityEngine.Object blur && blur;
            _wasMovement = PlayerInputReader.Instance.InputForMovement;
            PlayerState.SetState(State.menu);
            PlayerInputReader.Instance.InputForMovement = false;
            RSServices.Current.Get<UIService>().ShowNonUIToolkitBackgroundBlur = true;
            _panel = new RadioPanel(this);
            WriteStartupStatus();
            Logger.LogInfo("RadioRamen menu opened: " + Stations.Count + " stations, " + Tracks.Count + " local tracks.");
            return true;
        }
        catch (Exception ex) { Logger.LogError("Could not open RadioRamen: " + ex); ClosePanel(); return true; }
    }

    internal void ClosePanel()
    {
        var panel = _panel; _panel = null; panel?.Dispose();
        if (PlayerState.GetCurrentPlayerState == State.menu && _openFrame >= 0)
        {
            PlayerState.SetState(_oldState);
            if (PlayerInputReader.Instance) PlayerInputReader.Instance.InputForMovement = _wasMovement;
            if (PauseMenuManager.Instance) PauseMenuManager.Instance.stateBeforeMenu = _oldBeforeMenu;
            RSServices.Current.Get<UIService>().ShowNonUIToolkitBackgroundBlur = _wasBlur;
        }
        _openFrame = -1;
        CapturePlayback();
        SaveSettings();
        if (_ready) WriteStartupStatus();
    }

    internal void Choose(Station station)
    {
        if (!_radio || _decoder == null) return;
        _track = -1; Settings.LastStation = station.Id; Remember();
        StartPlayback(station.Stream, station.Name, false, station.Id);
    }

    internal void ChooseTrack(string path)
    {
        if (!_radio || _decoder == null || !Tracks.Contains(path)) return;
        _track = Tracks.IndexOf(path); _order.Select(_track);
        if (Playing?.Local == true && Playing.Source == path && Playing.Failure.Length == 0)
        { if (!_radio.isOn) _radio.isOn = true; return; }
        double resume = Path.GetFileName(path).Equals(Settings.LastTrack, StringComparison.OrdinalIgnoreCase) ? Settings.LastTrackPosition : 0;
        StartPlayback(path, Path.GetFileNameWithoutExtension(path), true, resumeSeconds: resume);
    }

    private void StartPlayback(string source, string title, bool local, string stationId = "", double resumeSeconds = 0)
    {
        CapturePlayback();
        DestroyClip(); _lastWasLocal = local; _lastPlaybackFailure = ""; _lastTransport = "";
        _stationId = stationId;
        _playing = _decoder!.Play(source, title, local, resumeSeconds);
        Settings.LastSource = local ? SavedSource.Music : SavedSource.Radio;
        if (local) { Settings.LastTrack = Path.GetFileName(source); Settings.LastTrackPosition = resumeSeconds; }
        Remember(); _checkpointAt = Time.realtimeSinceStartup + 10f;
        if (_radio && !_radio.isOn) _radio.isOn = true;
        Status = "Connecting...";
    }

    internal void PlaySelected(bool music)
    {
        if (music)
        {
            int remembered = MusicLibrary.FindRememberedTrack(Tracks, Settings.LastTrack);
            if (remembered < 0 && Settings.LastTrack.Length > 0)
                Status = "The last MP3 is missing from the Music folder. Choose another track.";
            else if (Tracks.Count > 0) ChooseTrack(Tracks[remembered >= 0 ? remembered : _track >= 0 && _track < Tracks.Count ? _track : 0]);
            else Status = "Add MP3 files to the Music folder, then press Refresh.";
        }
        else { var selected = Stations.FirstOrDefault(s => s.Id.Equals(Settings.LastStation, StringComparison.OrdinalIgnoreCase)) ?? Stations.FirstOrDefault(); if (selected != null) Choose(selected); }
    }

    internal void NextTrack()
    {
        if (!_radio || _decoder == null) return;
        int next = _order.Next(_track, Tracks.Count, Settings.Shuffle, Settings.Repeat, _random);
        if (next >= 0) { _track = next; StartPlayback(Tracks[next], Path.GetFileNameWithoutExtension(Tracks[next]), true); } else Stop();
    }

    internal void Stop()
    {
        CapturePlayback(); SaveSettings();
        _decoder?.Stop(); _playing = null; _stationId = ""; DestroyClip();
        if (_radio && _radio.isOn) _radio.isOn = false;
        Status = "Stopped";
    }

    private void Update()
    {
        if (!_ready) return;
        if (_settingsDirty && Time.unscaledTime >= _saveAt) SaveSettings();
        if (_playing != null && !_radio) { ClosePanel(); Detach(); return; }
        if (IsOpen && (PlayerState.GetCurrentPlayerState != State.menu || !_radio)) ClosePanel();
        var playback = _decoder?.Current;
        if (playback != null && _source)
        {
            bool powered = _radio && _radio.isOn;
            bool gamePaused = RSServices.Current?.Get<TimeService>()?.gamePaused == true;
            bool pause = PlaybackRules.PauseLocal(playback.Local, powered, gamePaused);
            bool mute = PlaybackRules.Mute(playback.Local, powered, gamePaused, Settings.Muted);
            _source.volume = (mute ? 0f : Settings.Volume / 100f) * Mathf.Clamp01(PlayerPrefs.GetInt("volume", 50) / 100f);
            var format = playback.Format;
            if (format != null && format != _format && (format.Buffer.Available >= format.Frequency * format.Channels * 2 || (playback.Finished && format.Buffer.Available > 0)))
            {
                DestroyClip(); _format = format;
                _clock.Reset(playback.Local ? format.StartSeconds : 0);
                // Capture this format, so an old callback cannot read the next station's buffer.
                _clip = AudioClip.Create("RadioRamen", format.Frequency * 2, format.Channels, format.Frequency, true, data => format.Buffer.Read(data));
                _source.clip = _clip;
            }
            SetSourcePaused(pause);
            if (format != null && format == _format && !_startedClip && !pause && (format.Buffer.Available >= format.Frequency * format.Channels || (playback.Finished && format.Buffer.Available > 0)))
            { _source.Play(); _startedClip = true; _clock.Resume(AudioSettings.dspTime); }
            bool started = _startedClip && format == _format && playback.Status != "Reconnecting...";
            UpdateTuning(PlaybackRules.Tune(playback.Local, powered, gamePaused, Settings.Muted, started, playback.Failure.Length > 0, playback.Finished), mute);
            Status = playback.Failure.Length > 0 ? playback.Failure : playback.Status;
            if (!playback.Local && !started && playback.Failure.Length == 0 && !playback.Finished) Status = "Tuning station...";
            if (pause && playback.Failure.Length == 0) Status = gamePaused ? "Paused with the game" : "Paused - E or Play to resume";
            else if (!playback.Local && gamePaused && playback.Failure.Length == 0) Status = "Muted while the game is paused";
            if (playback.Failure != _lastPlaybackFailure || playback.Transport != _lastTransport)
            { _lastPlaybackFailure = playback.Failure; _lastTransport = playback.Transport; if (playback.Failure.Length > 0) Logger.LogWarning("Radio playback: " + playback.Diagnostic); WriteStartupStatus(); }
            if (playback.Local && playback.Finished && format != null && format.Buffer.Available == 0 && !pause && _startedClip
                && MusicPosition >= format.StartSeconds + Interlocked.Read(ref playback.DecodedSamples) / (double)(format.Frequency * format.Channels))
            {
                if (playback.Failure.Length == 0) NextTrack(); else { Stop(); Status = playback.Failure; }
            }
        }
        if (Time.realtimeSinceStartup >= _checkpointAt)
        {
            _checkpointAt = Time.realtimeSinceStartup + 10f;
            if (Playing?.Local == true && _startedClip && Math.Abs(Settings.LastTrackPosition - MusicPosition) >= 0.5)
            { CapturePlayback(); SaveSettings(); }
        }
        _panel?.UpdateStatus();
    }

    private static RuntimeEnvironment_Radio? ResolveRadio(EnvironmentInfo_Radio info, Transform? target)
    {
        var radio = RSServices.Current?.Get<InteractionService>()?.MouseOverObject as RuntimeEnvironment_Radio;
        if (!radio && target) radio = target.GetComponentInParent<RuntimeEnvironment_Radio>();
        return radio ? radio : info.RuntimeObject as RuntimeEnvironment_Radio;
    }

    private void AttachRadio(RuntimeEnvironment_Radio radio)
    {
        if (_radio == radio) return;
        Detach(); _radio = radio;
        _originalSong = Song.GetValue(radio) as SoundEvent; _originalSong?.Stop(radio.transform);
        _speaker = new GameObject("RadioRamen speaker"); _speaker.transform.SetParent(radio.transform, false);
        _source = _speaker.AddComponent<AudioSource>();
        _source.playOnAwake = false; _source.loop = true; _source.ignoreListenerPause = true;
        _source.spatialBlend = 1f; _source.dopplerLevel = 0; _source.pitch = 1;
        var tuner = new GameObject("RadioRamen tuning"); tuner.transform.SetParent(_speaker.transform, false);
        _tuningSource = tuner.AddComponent<AudioSource>(); _tuningSource.playOnAwake = false; _tuningSource.loop = true;
        _tuningSource.ignoreListenerPause = true; _tuningSource.spatialBlend = 1;
        _tuningSource.dopplerLevel = 0; _tuningSource.volume = 0;
        float[] tuning = TuningSignal.Create();
        _tuningClip = AudioClip.Create("RadioRamen tuning", tuning.Length, 1, TuningSignal.Frequency, false); _tuningClip.SetData(tuning, 0); _tuningSource.clip = _tuningClip;
        _lowPass = _speaker.AddComponent<AudioLowPassFilter>(); _highPass = _speaker.AddComponent<AudioHighPassFilter>(); _distortion = _speaker.AddComponent<AudioDistortionFilter>();
        ApplySound(); ApplyRange();
    }

    internal bool TogglePower(EnvironmentInfo_Radio info, Transform? target)
    {
        if (!_ready) return false;
        var radio = ResolveRadio(info, target);
        if (!radio || PlayerState.GetCurrentPlayerState != State.none) return true;
        try
        {
            AttachRadio(radio);
            if (radio.isOn) PowerOff();
            else if (Playing?.Local == true && Playing.Failure.Length == 0) radio.isOn = true;
            else PlaySelected(_lastWasLocal);
            WriteStartupStatus();
        }
        catch (Exception ex) { Logger.LogError("Radio power control: " + ex); }
        return true;
    }

    internal bool OwnsRadio(RuntimeEnvironment_Radio radio) => _radio == radio;

    internal void PowerOff()
    {
        if (Playing?.Local == true)
        { if (_radio && _radio.isOn) _radio.isOn = false; SetSourcePaused(true); CapturePlayback(); SaveSettings(); Status = "Paused - E or Play to resume"; }
        else Stop();
    }

    private void SetSourcePaused(bool paused)
    {
        if (!_source || paused == _sourcePaused) return;
        _sourcePaused = paused;
        if (paused) { _source.Pause(); _clock.Pause(AudioSettings.dspTime); }
        else { _source.UnPause(); if (_startedClip) _clock.Resume(AudioSettings.dspTime); }
    }

    private void UpdateTuning(bool wanted, bool silent)
    {
        if (!_tuningSource) return;
        float target = wanted ? Settings.Volume / 100f * Mathf.Clamp01(PlayerPrefs.GetInt("volume", 50) / 100f) * TuningSignal.Volume : 0;
        if (wanted && !_tuningSource.isPlaying) _tuningSource.Play();
        _tuningSource.volume = silent ? 0 : Mathf.MoveTowards(_tuningSource.volume, target, Time.unscaledDeltaTime * 2);
        if (!wanted && _tuningSource.volume <= 0) _tuningSource.Stop();
    }

    internal void SetShortRange(bool nearby) { Settings.ShortRange = nearby; ApplyRange(); Remember(); WriteStartupStatus(); }
    private void ApplyRange()
    {
        foreach (var source in new[] { _source, _tuningSource })
        {
            if (!source) continue;
            source.minDistance = Settings.ShortRange ? RadioRange.NearbyMinimum : RadioRange.NormalMinimum;
            source.maxDistance = Settings.ShortRange ? RadioRange.NearbyMaximum : RadioRange.NormalMaximum;
            if (Settings.ShortRange)
            {
                if (_nearbyCurve == null)
                {
                    var points = RadioRange.Nearby;
                    var keys = new Keyframe[points.Length];
                    for (int i = 0; i < points.Length; i++)
                    {
                        float incoming = i == 0 ? 0 : (points[i].Gain - points[i - 1].Gain) / (points[i].Distance - points[i - 1].Distance) * RadioRange.NearbyMaximum;
                        float outgoing = i + 1 == points.Length ? 0 : (points[i + 1].Gain - points[i].Gain) / (points[i + 1].Distance - points[i].Distance) * RadioRange.NearbyMaximum;
                        keys[i] = new Keyframe(points[i].Distance / RadioRange.NearbyMaximum, points[i].Gain, incoming, outgoing);
                    }
                    _nearbyCurve = new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
                }
                source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, _nearbyCurve);
                source.rolloffMode = AudioRolloffMode.Custom;
            }
            else source.rolloffMode = AudioRolloffMode.Logarithmic;
        }
    }

    internal void SetSound(SoundProfile sound) { Settings.Sound = sound; ApplySound(); Remember(); }
    private void ApplySound()
    {
        if (!_lowPass || !_highPass || !_distortion) return;
        bool active = Settings.Sound != SoundProfile.Clean;
        _lowPass.enabled = _highPass.enabled = _distortion.enabled = active;
        _lowPass.cutoffFrequency = Settings.Sound == SoundProfile.AM ? 3200 : 7000;
        _highPass.cutoffFrequency = Settings.Sound == SoundProfile.AM ? 300 : 120;
        _distortion.distortionLevel = Settings.Sound == SoundProfile.AM ? 0.08f : 0.025f;
    }

    internal void Remember() { _settingsDirty = true; _saveAt = Time.unscaledTime + 0.5f; }
    private void CapturePlayback()
    {
        var playback = Playing;
        if (playback == null) return;
        Settings.LastSource = playback.Local ? SavedSource.Music : SavedSource.Radio;
        if (playback.Local)
        {
            Settings.LastTrack = Path.GetFileName(playback.Source);
            Settings.LastTrackPosition = _startedClip ? MusicPosition : playback.Format?.StartSeconds ?? playback.ResumeSeconds;
        }
        _settingsDirty = true;
    }

    private void SaveSettings()
    {
        if (!_settingsDirty || Folder.Length == 0) return;
        CapturePlayback();
        try { Settings.Save(Path.Combine(Folder, "RadioRamen.ini")); }
        catch (Exception ex) { Logger.LogWarning("Could not remember radio controls: " + ex.Message); }
        _settingsDirty = false;
    }

    private void DestroyClip()
    {
        if (_tuningSource) { _tuningSource.Stop(); _tuningSource.volume = 0; }
        if (_source) { _source.Stop(); _source.clip = null; }
        if (_clip) Destroy(_clip);
        _clip = null; _format = null;
        _sourcePaused = false; _startedClip = false; _clock.Reset();
    }

    internal void Detach()
    {
        Stop();
        if (_speaker) Destroy(_speaker);
        if (_tuningClip) Destroy(_tuningClip);
        _tuningSource = null; _tuningClip = null;
        _speaker = null; _lowPass = null; _highPass = null; _distortion = null;
        _source = null; _radio = null; _originalSong = null;
    }

    internal void RadioDisabled(RuntimeEnvironment_Radio radio) { if (_radio == radio) { ClosePanel(); Detach(); } }
    private void OnApplicationQuit() { CapturePlayback(); SaveSettings(); }
    private void OnDestroy()
    {
        _ready = false;
        try { ClosePanel(); } catch (Exception ex) { Logger.LogWarning("Radio menu cleanup: " + ex.Message); }
        try { Detach(); } catch (Exception ex) { Logger.LogWarning("Radio audio cleanup: " + ex.Message); }
        finally { _harmony?.UnpatchSelf(); _decoder?.Dispose(); _decoder = null; Instance = null; }
    }
}

[HarmonyPatch(typeof(EnvironmentInfo_Radio), nameof(EnvironmentInfo_Radio.InteractEnvironment), new[] { typeof(Transform) })]
internal static class RadioPowerInteraction
{
    private static bool Prefix(EnvironmentInfo_Radio __instance, Transform __0) => !(Plugin.Instance?.TogglePower(__instance, __0) ?? false);
}
[HarmonyPatch(typeof(EnvironmentInfo_Radio), nameof(EnvironmentInfo_Radio.Interact), new[] { typeof(MainInteractableInfo), typeof(MainInteractableInfo) })]
internal static class RadioChannelInteraction
{
    private static bool Prefix(EnvironmentInfo_Radio __instance) => !(Plugin.Instance?.Open(__instance) ?? false);
}
[HarmonyPatch(typeof(RuntimeEnvironment_Radio), nameof(RuntimeEnvironment_Radio.isOn), MethodType.Setter)]
internal static class NativePowerState
{
    private static bool Prefix(RuntimeEnvironment_Radio __instance, bool __0, ref bool ____isOn)
    {
        if (Plugin.Instance?.OwnsRadio(__instance) != true) return true;
        // Keep the native lamp/button and saved power state, while leaving the saved vanilla song reference intact.
        if (____isOn != __0) { ____isOn = __0; __instance.ToggleRadio(); }
        return false;
    }
}
[HarmonyPatch(typeof(PauseMenuManager), nameof(PauseMenuManager.TogglePauseMenu), new Type[] { })]
internal static class CloseOnEscape
{
    private static bool Prefix() { if (Plugin.Instance?.IsOpen != true) return true; Plugin.Instance.Back(); return false; }
}
[HarmonyPatch(typeof(RuntimeEnvironment_Radio), "OnDisable", new Type[] { })]
internal static class StopDisabledRadio
{
    private static void Postfix(RuntimeEnvironment_Radio __instance) => Plugin.Instance?.RadioDisabled(__instance);
}

[HarmonyPatch(typeof(UIService), nameof(UIService.ShowMouseoverUI), new[] { typeof(RuntimeInteractable), typeof(string) })]
internal static class RadioHoverPrompt
{
    private static void Postfix(RuntimeInteractable __0) => Plugin.Instance?.ShowRadioPrompt(__0);
}
