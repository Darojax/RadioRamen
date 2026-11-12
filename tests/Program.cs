using RadioRamen;
using System.Diagnostics;
using System.Security.Cryptography;
using Mono.Cecil;

string module = args.Length > 0 ? Path.GetFullPath(args[0]) : throw new Exception("Pass the RadioRamen module path.");
string game = args.Length > 1 ? Path.GetFullPath(args[1]) : throw new Exception("Pass the supported game installation as the second argument.");
int checks = 0;
void Check(bool value, string message) { if (!value) throw new Exception("FAILED: " + message); checks++; Console.WriteLine("PASS " + message); }
void Reject(Action action, string message) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, message); }
var stations = Station.Parse(File.ReadAllText(Path.Combine(module, "config/Stations.ini")));
Check(stations.Count >= 3 && stations.Select(s => s.Id).Distinct().Count() == stations.Count, "station catalogue has unique identities");
Check(stations.Select(s => s.Stream).Distinct(StringComparer.OrdinalIgnoreCase).Count() == stations.Count, "station catalogue contains no duplicate stream URLs");
Check(stations.Select(s => s.Region).Distinct().Count() >= 3, "catalogue covers regions beyond Tokyo");
Check(stations.Select(s => s.Category).Distinct().Count() >= 2, "station categories are available");
Check(stations.Single(s => s.Id == "fm-kahoku").Stream.EndsWith("/;"), "semicolons in stream URLs are preserved");
Check(stations[0].Matches("All categories", "All Japan", "seta"), "search matches station names without case sensitivity");
Check(!stations[0].Matches("News", "All Japan", ""), "category filter excludes other formats");
Check(!stations[0].Matches("All categories", "Okinawa", ""), "region filter excludes other regions");
foreach (string uri in new[] { "file:///C:/audio.mp3", "javascript:alert(1)", "ftp://example.com/live", "https://user:password@example.com/live" })
    Reject(() => Station.Parse("[test]\nName=Test\nStream=" + uri), "non-public or non-HTTP station URL rejected");
Reject(() => Station.Parse("[test]\nName=Test\nStream=https://example.com\rInjected:header"), "control characters cannot inject native request headers");
Reject(() => Ini.Read("[x]\na=1\na=2"), "duplicate INI keys are rejected");
Reject(() => Ini.Read("[x]\na=1\n[x]\na=2"), "duplicate INI sections are rejected");
var settings = RadioSettings.Parse(File.ReadAllText(Path.Combine(module, "config/RadioRamen.ini")));
Check(settings.Enabled && settings.Volume == 50 && !settings.Muted && settings.Repeat && !settings.Shuffle, "public controls have the intended defaults");
Check(!settings.ShortRange && !RadioSettings.Parse("[Radio]\nEnabled=true").ShortRange, "new and older INIs retain normal audible distance until Nearby is selected");
Check(RadioSettings.Parse("[Radio]\nShortRange=true").ShortRange, "nearby audible distance can be selected in the INI");
Reject(() => RadioSettings.Parse("[Radio]\nShortRange=maybe"), "invalid audible distance toggles are rejected");
Check(RadioRange.Nearby.First() == (0f, 1f) && RadioRange.Nearby.Last() == (RadioRange.NearbyMaximum, 0f), "Nearby starts at full selected volume and reaches zero at its outer boundary");
Check(RadioRange.Nearby.Zip(RadioRange.Nearby.Skip(1)).All(pair => pair.First.Distance < pair.Second.Distance && pair.First.Gain >= pair.Second.Gain)
    && RadioRange.Nearby.All(point => point.Gain >= 0 && point.Gain <= 1), "Nearby distance points are ordered, monotonically quieter and cannot amplify the source");
Check(RadioRange.Nearby.Where(p => p.Distance <= RadioRange.NearbyMinimum).All(p => p.Gain == 1)
    && RadioRange.Nearby.Where(p => p.Distance >= 6).All(p => p.Gain <= 0.10f)
    && RadioRange.Nearby.Where(p => p.Distance >= 8).All(p => p.Gain <= 0.02f), "Nearby preserves close listening and remains quiet farther from the radio");
Reject(() => RadioSettings.Parse("[Radio]\nVolume=101"), "out-of-range volume is rejected");
Reject(() => RadioSettings.Parse("[Radio]\nMuted=maybe"), "invalid mute flag is rejected");
Check(RadioSettings.Parse("[Radio]\nSound=FM").Sound == SoundProfile.FM && RadioSettings.Parse("[Radio]\nSound=AM").Sound == SoundProfile.AM, "FM and AM sound profiles can be selected");
Reject(() => RadioSettings.Parse("[Radio]\nSound=5"), "undefined sound profiles are rejected");
Check(PlaybackRules.PauseLocal(true, false, false), "powering off local music pauses its session");
Check(PlaybackRules.PauseLocal(true, true, true), "pausing the game pauses local music");
Check(!PlaybackRules.PauseLocal(true, true, false), "powered local music resumes when the game resumes");
Check(PlaybackRules.PauseLocal(true, false, true) && PlaybackRules.PauseLocal(true, false, false), "resuming the game does not turn a powered-off MP3 back on");
Check(!PlaybackRules.PauseLocal(false, true, true) && PlaybackRules.Mute(false, true, true, false), "live radio keeps running silently during game pause");
Check(!PlaybackRules.Mute(false, true, false, false) && PlaybackRules.Mute(false, true, false, true), "live radio unmutes on game resume while respecting manual mute");
Check(PlaybackRules.Tune(false, true, false, false, false, false, false), "a new live station can play the tuning effect while connecting");
Check(!PlaybackRules.Tune(true, true, false, false, false, false, false), "MP3 loading does not play radio tuning noise");
Check(!PlaybackRules.Tune(false, false, false, false, false, false, false)
    && !PlaybackRules.Tune(false, true, true, false, false, false, false)
    && !PlaybackRules.Tune(false, true, false, true, false, false, false), "tuning noise respects power, game pause and manual mute");
Check(!PlaybackRules.Tune(false, true, false, false, true, false, false)
    && !PlaybackRules.Tune(false, true, false, false, false, true, false)
    && !PlaybackRules.Tune(false, true, false, false, false, false, true), "tuning noise ends on playback, failure or completion");
var tuning = TuningSignal.Create();
double tuningRms = Math.Sqrt(tuning.Average(x => (double)x * x));
Check(tuning.Length == TuningSignal.Frequency * TuningSignal.Seconds && tuning.All(x => float.IsFinite(x) && Math.Abs(x) < 0.5f), "generated tuning audio is finite and has bounded peaks");
Check(tuningRms > 0.03 && tuningRms * TuningSignal.Volume < 0.02, "tuning audio remains quiet relative to the radio level");
Check(tuning[0] == 0 && tuning[^1] == 0 && tuning.Take(250).Max(x => Math.Abs(x)) < 0.5f, "tuning loop edges fade to zero");
AcousticsChecks.Run(module, Check);
string originalPrompt = "Press <sprite name=LeftMouse> to change channel\nPress <sprite name=E> to turn on or off";
Check(RadioPromptText.Operate(originalPrompt) == "Press <sprite name=LeftMouse> to operate\nPress <sprite name=E> to turn on or off", "radio prompt keeps native input icons and power wording while changing operate text");
Check(RadioPromptText.Operate("Press LMB to Change the Channel") == "Press LMB to operate"
    && RadioPromptText.Operate("Press LMB to clean") == "Press LMB to clean", "prompt replacement handles both channel phrasings without changing unrelated wording");
var clock = new PlaybackClock(); clock.Resume(100); clock.Pause(112.5);
Check(clock.Elapsed(200) == 12.5, "MP3 playhead stays fixed while paused");
clock.Resume(250);
Check(clock.Elapsed(257.25) == 19.75, "MP3 playhead resumes from the same position");
clock.Pause(258); clock.Pause(300);
Check(clock.Elapsed(400) == 20.5, "overlapping pause reasons do not advance the playhead");
clock.Reset(125.25); clock.Resume(500); clock.Pause(503.5);
Check(clock.Elapsed(700) == 128.75, "a restored MP3 playhead includes its saved offset and freezes during pause");
var buffer = new PcmBuffer(5);
Check(buffer.Write(new float[] { 1, 2, 3, 4 }, 4) == 4, "PCM samples enter the bounded buffer");
var first = new float[3]; buffer.Read(first);
Check(first.SequenceEqual(new float[] { 1, 2, 3 }), "PCM samples retain their order");
buffer.Write(new float[] { 5, 6, 7, 8, 9 }, 5);
var wrapped = new float[7]; int read = buffer.Read(wrapped);
Check(read == 5 && wrapped.SequenceEqual(new float[] { 4, 5, 6, 7, 8, 0, 0 }), "buffer wraparound stays ordered, bounded and fills underruns with silence");
Check(buffer.Available == 0 && buffer.Free == 5, "PCM consumption releases all capacity");
var concurrent = new PcmBuffer(1024);
var writer = Task.Run(() => { for (int n = 1; n <= 10000;) { if (concurrent.Write(new float[] { n }, 1) == 1) n++; else Thread.Yield(); } });
int expected = 1; bool ordered = true;
var sample = new float[1];
while (expected <= 10000) { if (concurrent.Read(sample) == 1) { ordered &= sample[0] == expected; expected++; } else Thread.Yield(); }
writer.Wait(); Check(ordered, "concurrent audio producer and consumer preserve 10000 samples");
var random = new Random(123);
Check(MusicLibrary.Next(0, 0, false, true, random) == -1, "empty music library is safe");
Check(MusicLibrary.Next(2, 3, false, false, random) == -1, "playlist can end without repeating");
Check(MusicLibrary.Next(2, 3, false, true, random) == 0, "repeat wraps to the first track");
Check(Enumerable.Range(0, 30).All(_ => MusicLibrary.Next(1, 3, true, true, random) != 1), "shuffle avoids immediately repeating the selected track");
Check(MusicLibrary.Next(-1, 3, false, true, random) == 0, "Next without a selected track starts the playlist");
var order = new MusicOrder(); order.Select(1);
int shuffleA = order.Next(1, 3, true, false, random), shuffleB = order.Next(shuffleA, 3, true, false, random);
Check(shuffleA != 1 && shuffleB != 1 && shuffleA != shuffleB && order.Next(shuffleB, 3, true, false, random) == -1, "shuffle plays each remaining track once and respects repeat off");
Check(order.Next(shuffleB, 3, true, true, random) != shuffleB, "a repeated shuffle cycle avoids an immediate duplicate");
string fixture = Path.Combine(module, "artifacts/fixture.mp3");
string unicodeFile = Path.GetFullPath(Path.Combine(module, "artifacts/\u6771\u4eac\u30e9\u30b8\u30aa.mp3"));
File.Copy(fixture, unicodeFile, true);
string settingsFile = Path.Combine(module, "artifacts/settings-check.ini");
File.Copy(Path.Combine(module, "config/RadioRamen.ini"), settingsFile, true);
settings.Volume = 37; settings.Muted = true; settings.LastStation = "fm-kahoku"; settings.Sound = SoundProfile.AM; settings.ShortRange = true; settings.Save(settingsFile);
var remembered = RadioSettings.Parse(File.ReadAllText(settingsFile));
Check(remembered.Volume == 37 && remembered.Muted && remembered.LastStation == "fm-kahoku", "volume, mute and selected station persist without changing the public defaults");
Check(remembered.Sound == SoundProfile.AM, "the selected vintage sound profile is remembered");
Check(remembered.ShortRange, "Nearby audible distance is remembered alongside the other radio controls");
Check(remembered.LastSource == SavedSource.Radio && remembered.LastTrackPosition == 0, "older settings without a saved MP3 retain the radio default");
settings.LastSource = SavedSource.Music; settings.LastTrack = Path.GetFileName(unicodeFile); settings.LastTrackPosition = 1.25; settings.Save(settingsFile);
var savedPlayback = RadioSettings.Parse(File.ReadAllText(settingsFile));
Check(savedPlayback.LastSource == SavedSource.Music && savedPlayback.LastTrack == Path.GetFileName(unicodeFile) && savedPlayback.LastTrackPosition == 1.25,
    "the last MP3 filename and fractional playhead survive saving and loading");
savedPlayback.LastSource = SavedSource.Radio; savedPlayback.Save(settingsFile);
var afterRadio = RadioSettings.Parse(File.ReadAllText(settingsFile));
var resumeTracks = new[] { Path.Combine(module, "first.mp3"), unicodeFile, Path.Combine(module, "last.mp3") };
Check(afterRadio.LastSource == SavedSource.Radio && MusicLibrary.FindRememberedTrack(resumeTracks, afterRadio.LastTrack) == 1
    && afterRadio.LastTrackPosition == 1.25, "switching to live radio retains the previous MP3 and its resume point instead of selecting the first file");
Check(MusicLibrary.FindRememberedTrack(new[] { unicodeFile, resumeTracks[2], resumeTracks[0] }, afterRadio.LastTrack.ToUpperInvariant()) == 0,
    "remembered music is found by filename rather than an old list index or filename casing");
Check(MusicLibrary.FindRememberedTrack(resumeTracks, "removed.mp3") == -1 && MusicLibrary.FindRememberedTrack(Array.Empty<string>(), afterRadio.LastTrack) == -1,
    "a removed remembered MP3 and an empty library are reported without selecting a different file");
foreach (string position in new[] { "NaN", "Infinity", "-1", "1,25" })
    Reject(() => RadioSettings.Parse("[Playback]\nSource=Music\nPosition=" + position), "invalid persisted MP3 position rejected: " + position);
foreach (string track in new[] { "../outside.mp3", "C:\\outside.mp3", "folder\\track.mp3" })
    Reject(() => RadioSettings.Parse("[Playback]\nTrack=" + track), "a saved MP3 cannot escape the scanned Music folder");
Reject(() => RadioSettings.Parse("[Playback]\nSource=Unknown"), "unknown persisted playback sources are rejected");
Check(File.ReadAllText(settingsFile).StartsWith("; RadioRamen"), "settings persistence retains comments");
File.WriteAllText(settingsFile, "; Custom settings\n[Radio]\nEnabled=true\n"); settings.Save(settingsFile);
remembered = RadioSettings.Parse(File.ReadAllText(settingsFile));
Check(remembered.Volume == 37 && remembered.Muted && remembered.Repeat && remembered.ShortRange, "controls are remembered even when optional INI keys or sections were omitted");
Check(MusicLibrary.Scan(Path.Combine(module, "artifacts")).Contains(unicodeFile), "music scanning supports Japanese filenames");
var userLibrary = UserStationLibrary.Parse("");
Check(userLibrary.Stations.Count == 0 && userLibrary.Favourites.Count == 0, "an empty player library adds no stations or favourites");
string userPath = Path.Combine(module, "artifacts/user-library-" + Guid.NewGuid().ToString("N") + ".ini");
Check(UserStationLibrary.Load(userPath).Stations.Count == 0 && !File.Exists(userPath), "loading a missing user library does not create or overwrite a file");
var custom = userLibrary.Upsert(null, "\u6771\u4eac Jazz", "https://radio.example/live/;?x=1&y=2", "Jazz", "Online", stations);
Check(custom.IsCustom && custom.Id.StartsWith("custom-", StringComparison.Ordinal) && custom.Frequency == "Internet radio", "custom feeds receive independent stable identities and an honest type label");
userLibrary.SetFavourite(stations[0].Id, true); userLibrary.SetFavourite(custom.Id, true); userLibrary.Save(userPath);
byte[] firstUserSave = File.ReadAllBytes(userPath);
var userReloaded = UserStationLibrary.Load(userPath);
Check(userReloaded.Stations.Single().Id == custom.Id && userReloaded.Stations.Single().Name == custom.Name && userReloaded.Stations.Single().Stream == custom.Stream,
    "custom names, identities, Unicode and semicolon stream URLs survive saving and loading");
Check(userReloaded.Favourites.SetEquals(new[] { stations[0].Id, custom.Id }), "bundled and custom station favourites both survive loading");
var updatedCustom = userReloaded.Upsert(custom.Id, "Renamed jazz", "https://radio.example/updated", "Easy listening", "Norway", stations);
Check(updatedCustom.Id == custom.Id && userReloaded.Favourites.Contains(custom.Id) && userReloaded.Stations.Count == 1, "editing a custom feed preserves its favourite and identity");
userReloaded.Save(userPath);
Check(File.ReadAllBytes(userPath + ".bak").SequenceEqual(firstUserSave), "updating the user library keeps the previous save as an exact backup");
Check(UserStationLibrary.Load(userPath).Stations.Single().Name == "Renamed jazz", "an atomic replacement contains the edited station");
var sameFeed = userReloaded.Upsert(null, "Other name for same feed", updatedCustom.Stream, "", "", stations);
Check(sameFeed.Id != updatedCustom.Id && sameFeed.Category == "My stations" && sameFeed.Region == "Online", "feeds sharing a URL retain separate identities and optional fields get useful defaults");
Check(UserStationLibrary.Merge(stations, userReloaded).Count == stations.Count + 2, "custom feeds extend the catalogue without replacing bundled entries");
Check(updatedCustom.Matches("All categories", "All regions", "renamed"), "the all-regions filter includes custom international feeds");
userReloaded.Remove(custom.Id);
Check(!userReloaded.Favourites.Contains(custom.Id) && userReloaded.Favourites.Contains(stations[0].Id) && userReloaded.Stations.Single().Id == sameFeed.Id,
    "removing one custom feed clears only its own favourite and retains other stations");
userReloaded.SetFavourite(stations[0].Id, false);
Check(!userReloaded.Favourites.Contains(stations[0].Id), "a bundled station can be unfavourited without removing its catalogue entry");
Reject(() => userReloaded.Remove(stations[0].Id), "bundled stations cannot be deleted through the custom library");
Reject(() => userReloaded.Upsert("custom-missing", "Missing", "https://radio.example/live", "", "", stations), "editing a removed custom feed does not silently recreate it");
foreach (string bad in new[] { "file:///C:/private.mp3", "javascript:alert(1)", "https://name:secret@example.com/live", "https://example.com/live\nInjected=1" })
    Reject(() => userReloaded.Upsert(null, "Bad stream", bad, "", "", stations), "unsafe or multiline custom stream inputs are refused");
Reject(() => userReloaded.Upsert(null, "Injected\n[Favourites]\nfake=true", "https://radio.example/live", "", "", stations), "custom names cannot inject INI sections");
Reject(() => userReloaded.Upsert(null, "Name", "https://radio.example/live", "Jazz\u2028Other", "", stations), "custom metadata rejects Unicode line separators");
Reject(() => userReloaded.SetFavourite("bad\nentry", true), "favourite identities cannot inject INI content");
Reject(() => UserStationLibrary.Parse("[Favourites]\nfm-setagaya=maybe"), "malformed favourite flags are reported");
Reject(() => UserStationLibrary.Parse("[bundled-id]\nName=Wrong namespace\nStream=https://example.com/live"), "custom library sections cannot impersonate bundled identities");
userReloaded.Save(userPath); byte[] savedBeforeInvalid = File.ReadAllBytes(userPath);
sameFeed.Stream = "file:///C:/private.mp3";
Reject(() => userReloaded.Save(userPath), "the entire library is validated before any replacement");
Check(File.ReadAllBytes(userPath).SequenceEqual(savedBeforeInvalid), "a failed user library validation leaves the last save intact");
string gameAssembly = Path.Combine(game, "Ramen Shop Simulator_Data/Managed/Assembly-CSharp.dll");
string pluginAssembly = Path.Combine(module, "src/bin/Release/netstandard2.1/RadioRamen.dll");
var compatibility = GameCompatibility.Inspect(gameAssembly, pluginAssembly);
Check(compatibility.Hooks.Count == 6 && compatibility.GameApiReferences > 0, "the installed game passes all radio hook and API capability checks");
byte[] gameBytes = File.ReadAllBytes(gameAssembly), futureBytes = gameBytes.Concat(new byte[] { 82, 65, 68, 73, 79 }).ToArray();
using (var future = AssemblyDefinition.ReadAssembly(new MemoryStream(futureBytes)))
using (var plugin = AssemblyDefinition.ReadAssembly(pluginAssembly))
{
    future.Name.Version = new Version(99, 0, 0, 0); future.MainModule.Mvid = Guid.NewGuid();
    future.MainModule.Types.Add(new TypeDefinition("UnrelatedUpdate", "NewFeature", Mono.Cecil.TypeAttributes.Public | Mono.Cecil.TypeAttributes.Class, future.MainModule.TypeSystem.Object));
    var power = future.MainModule.GetType("Interactable_Infos.Sealed_SO_Classes.Environment.EnvironmentInfo_Radio").Methods.Single(m => m.Name == "InteractEnvironment");
    power.Parameters[0].Name = "renamedTarget";
    power.Body.GetILProcessor().InsertBefore(power.Body.Instructions[0], Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Nop));
    future.MainModule.GetType("RSServiceLocator.Scripts.UIService").Methods.Add(new MethodDefinition("ShowMouseoverUI", Mono.Cecil.MethodAttributes.Public, future.MainModule.TypeSystem.Void));
    Check(!SHA256.HashData(gameBytes).SequenceEqual(SHA256.HashData(futureBytes)) && GameCompatibility.Inspect(future, plugin).Hooks.Count == 6,
        "changed fingerprints, versions, unrelated code, parameter names, extra overloads and harmless instructions do not block compatible radio code");
}
const string radioType = "Runtime_Classes.Environments.RuntimeEnvironment_Radio", infoType = "Interactable_Infos.Sealed_SO_Classes.Environment.EnvironmentInfo_Radio";
MethodDefinition ChangedMethod(AssemblyDefinition a, string type, string name) => a.MainModule.GetType(type).Methods.Single(m => m.Name == name);
void RejectCompatibility(Action<AssemblyDefinition> change, string message, string detail)
{
    using var changed = AssemblyDefinition.ReadAssembly(gameAssembly);
    using var plugin = AssemblyDefinition.ReadAssembly(pluginAssembly);
    change(changed);
    string failure = "";
    try { GameCompatibility.Inspect(changed, plugin); } catch (InvalidDataException ex) { failure = ex.Message; }
    Check(failure.Contains(detail, StringComparison.Ordinal), message + (failure.Length > 0 ? " / " + failure : ""));
}
RejectCompatibility(a => a.MainModule.GetType(radioType).Name = "RemovedRadio",
    "a missing radio type is rejected before patching", "missing game type");
RejectCompatibility(a => a.MainModule.GetType(radioType).Fields.Single(f => f.Name == "_isOn").FieldType = a.MainModule.TypeSystem.Int32,
    "a changed native power field is rejected", "private field contract changed");
RejectCompatibility(a => a.MainModule.GetType(radioType).Fields.Single(f => f.Name == "_isOn").IsInitOnly = true,
    "a read-only native power field is rejected", "private field contract changed");
RejectCompatibility(a => a.MainModule.GetType(radioType).Fields.Remove(a.MainModule.GetType(radioType).Fields.Single(f => f.Name == "CurrentSong")),
    "a missing native song reference is rejected", "private field contract changed");
RejectCompatibility(a => ChangedMethod(a, infoType, "InteractEnvironment").ReturnType = a.MainModule.TypeSystem.Int32,
    "a changed radio interaction signature is rejected", "method contract is missing or ambiguous");
RejectCompatibility(a => {
    var original = ChangedMethod(a, "RSServiceLocator.Scripts.UIService", "ShowMouseoverUI");
    var duplicate = new MethodDefinition(original.Name, original.Attributes, original.ReturnType);
    foreach (var p in original.Parameters) duplicate.Parameters.Add(new ParameterDefinition(p.Name, p.Attributes, p.ParameterType));
    original.DeclaringType.Methods.Add(duplicate);
},
    "an ambiguous hover hook is rejected", "method contract is missing or ambiguous");
RejectCompatibility(a => a.MainModule.GetType("RSServiceLocator.Scripts.NotebookService").Fields.Single(f => f.Name == "instructionsUIAsset").FieldType = a.MainModule.TypeSystem.String,
    "a changed notebook theme contract is rejected", "private field contract changed");
RejectCompatibility(a => a.MainModule.GetType("RSServiceLocator.Scripts.UIService").Fields.Single(f => f.Name == "_nonUIToolkitBGBlur").FieldType = a.MainModule.TypeSystem.Boolean,
    "a changed menu blur contract is rejected", "private field contract changed");
RejectCompatibility(a => ChangedMethod(a, "RSServiceLocator.Scripts.UIService", "get_mouseoverBodyLabel").IsStatic = false,
    "a changed native hover label getter is rejected", "method contract is missing or ambiguous");
RejectCompatibility(a => a.MainModule.GetType("Managers___Handlers.State").Fields.Single(f => f.Name == "menu").Constant = 32,
    "changed player state values are rejected", "player state value changed");
RejectCompatibility(a => ChangedMethod(a, "RSServiceLocator.Scripts.TimeService", "get_gamePaused").ReturnType = a.MainModule.TypeSystem.Int32,
    "a changed game pause API is rejected", "game API method changed");
RejectCompatibility(a => {
    var getter = ChangedMethod(a, radioType, "get_isOn"); var il = getter.Body.GetILProcessor();
    il.InsertBefore(getter.Body.Instructions.Last(), Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
    il.InsertBefore(getter.Body.Instructions.Last(), Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ceq));
}, "changed power getter semantics are rejected", "power getter no longer returns");
RejectCompatibility(a => {
    var visual = ChangedMethod(a, radioType, "ToggleRadio"); var il = visual.Body.GetILProcessor(); var end = visual.Body.Instructions.Last();
    il.InsertBefore(end, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldarg_0));
    il.InsertBefore(end, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Ldc_I4_0));
    il.InsertBefore(end, Mono.Cecil.Cil.Instruction.Create(Mono.Cecil.Cil.OpCodes.Call, ChangedMethod(a, radioType, "set_isOn")));
}, "visual callbacks that now change power are rejected", "visual update now changes power");
RejectCompatibility(a => {
    var setter = ChangedMethod(a, radioType, "OnDisable").Body.Instructions.First(i => i.Operand is MethodReference m && m.Name == "set_isOn");
    setter.Previous.OpCode = Mono.Cecil.Cil.OpCodes.Ldc_I4_1;
}, "changed removal power behavior is rejected", "removal no longer powers it off");
RejectCompatibility(a => {
    var toggle = ChangedMethod(a, infoType, "PlayRadio").Body.Instructions.First(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Ceq);
    toggle.Previous.OpCode = Mono.Cecil.Cil.OpCodes.Ldc_I4_1;
}, "changed native toggle behavior is rejected", "power no longer toggles");
using (var assembly = AssemblyDefinition.ReadAssembly(gameAssembly))
{
    var info = assembly.MainModule.GetType("Interactable_Infos.Sealed_SO_Classes.Environment.EnvironmentInfo_Radio");
    Check(info.Methods.Count(m => m.Name == "InteractEnvironment" && m.Parameters.Count == 1) == 1, "radio power interaction target has the reviewed signature");
    Check(info.Methods.Count(m => m.Name == "Interact" && m.Parameters.Count == 2) == 1, "radio channel interaction target has the reviewed signature");
    var radio = assembly.MainModule.GetType("Runtime_Classes.Environments.RuntimeEnvironment_Radio");
    Check(radio.Fields.Any(f => f.Name == "CurrentSong" && f.FieldType.FullName == "Sonity.SoundEvent"), "original radio song can be stopped and restored");
    Check(radio.Methods.Any(m => m.Name == "OnDisable"), "radio removal has a cleanup hook");
    Check(assembly.MainModule.GetType("Managers___Handlers.PauseMenuManager").Methods.Any(m => m.Name == "TogglePauseMenu"), "Escape uses the game's menu action");
    Check(assembly.MainModule.GetType("RSServiceLocator.Scripts.UIService").Fields.Any(f => f.Name == "_nonUIToolkitBGBlur"), "the native menu blur can be restored");
    Check(assembly.MainModule.GetType("RSServiceLocator.Scripts.UIService").Methods.Count(m => m.Name == "ShowMouseoverUI" && m.Parameters.Count == 2) == 1,
        "native hover UI has the reviewed prompt hook");
    var notebook = assembly.MainModule.GetType("RSServiceLocator.Scripts.NotebookService");
    Check(notebook.Fields.Any(f => f.Name == "instructionsUIAsset") && notebook.Fields.Any(f => f.Name == "m_panelSettings"), "notebook theme and panel assets are available without copied game art");
}
using (var assembly = AssemblyDefinition.ReadAssembly(Path.Combine(module, "src/bin/Release/netstandard2.1/RadioRamen.dll")))
{
    IEnumerable<TypeDefinition> Types(IEnumerable<TypeDefinition> types) => types.SelectMany(t => new[] {t}.Concat(Types(t.NestedTypes)));
    var types = Types(assembly.MainModule.Types).ToArray();
    Check(types.Count(t => t.CustomAttributes.Any(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch")) == 6, "only the six reviewed radio/menu/prompt hooks are declared");
    var instructions = types.SelectMany(t => t.Methods).Where(m => m.HasBody).SelectMany(m => m.Body.Instructions);
    Check(!instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Time" && m.Name == "set_timeScale"), "radio never changes global time speed");
    Check(!instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "System.Reflection.FieldInfo" && m.Name == "SetValue"), "radio leaves saved vanilla song definitions intact");
    Check(assembly.MainModule.Resources.Count == 2, "default player settings and station list are embedded");
    var acousticType = types.Single(t => t.FullName == "RadioRamen.RadioObstruction");
    Check(acousticType.Methods.Single(m => m.Name == "Update").Body.Instructions.Any(i => i.Operand is MethodReference m
        && m.DeclaringType.FullName == "RadioRamen.RadioRange" && m.Name == "Gain"), "runtime audio uses the checked volume-dependent attenuation model");
    var casts = acousticType.Methods.Single(m => m.Name == "Cast").Body.Instructions.Where(i => i.Operand is MethodReference m && m.Name == "RaycastNonAlloc").ToArray();
    Check(casts.Length == 2 && casts.All(i => i.Previous.OpCode == Mono.Cecil.Cil.OpCodes.Ldc_I4_1), "reusable raycast buffers ignore triggers, including the dense fallback");
    Check(acousticType.Methods.Single(m => m.Name == "Blocked").Body.Instructions.Count(i => i.Operand is MethodReference m && m.Name == "Cast") == 2,
        "wall routes check both directions for one-sided mesh faces");
    var acousticCast = acousticType.Methods.Single(m => m.Name == "Cast").Body.Instructions;
    Check(new[] { "UnityEngine.CapsuleCollider", "UnityEngine.SphereCollider", "UnityEngine.CharacterController" }.All(name => acousticCast
        .Any(i => i.OpCode == Mono.Cecil.Cil.OpCodes.Isinst && i.Operand is TypeReference t && t.FullName == name)),
        "character-controller and rounded actor colliders cannot be mistaken for structural walls");
    Check(!instructions.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "UnityEngine.Physics" && m.Name.StartsWith("set_")), "radio obstruction cannot change global game physics query settings");
    var rightClick = types.SelectMany(t => t.Methods).Where(m => m.HasBody && m.Parameters.Any(p => p.ParameterType.FullName == "UnityEngine.UIElements.PointerDownEvent"))
        .SelectMany(m => m.Body.Instructions).ToArray();
    Check(rightClick.Any(i => i.Operand is MethodReference m && m.Name == "get_button")
        && rightClick.Any(i => i.Operand is MethodReference m && m.Name == "StopImmediatePropagation")
        && rightClick.Any(i => i.Operand is MethodReference m && m.DeclaringType.FullName == "RadioRamen.Plugin" && m.Name == "ClosePanel"),
        "right-click handler consumes the pointer event and closes Radio Controls");
    var awake = types.Single(t => t.FullName == "RadioRamen.Plugin").Methods.Single(m => m.Name == "Awake").Body.Instructions.ToArray();
    int preflight = Array.FindIndex(awake, i => i.Operand is MethodReference m && m.DeclaringType.FullName == "RadioRamen.GameCompatibility" && m.Name == "Inspect");
    int patching = Array.FindIndex(awake, i => i.Operand is MethodReference m && m.Name == "PatchAll");
    Check(preflight >= 0 && patching > preflight && !types.Any(t => t.Name == "GameBuilds"), "startup validates capabilities before any patching and contains no build allowlist");
}
var decoder = new Decoder(Path.Combine(module, "artifacts/native"), args.Contains("--managed-http"));
try
{
    var playback = decoder.Play(unicodeFile, "Test", true);
    var watch = Stopwatch.StartNew(); long samplesRead = 0; double energy = 0;
    var samples = new float[8192];
    while (watch.Elapsed < TimeSpan.FromSeconds(12))
    {
        var format = playback.Format;
        if (format != null)
        {
            int got = format.Buffer.Read(samples); samplesRead += got;
            for (int i = 0; i < got; i++) energy += samples[i] * samples[i];
            if (playback.Finished && format.Buffer.Available == 0) break;
        }
        Thread.Sleep(5);
    }
    Check(playback.Finished && playback.Failure.Length == 0, "native decoder completes a local MP3 with a Japanese filename");
    Check(playback.Format?.Frequency == 44100 && playback.Format?.Channels == 2 && samplesRead >= 44100 * 2 * 3 && energy > 1, "local MP3 produces the expected stereo PCM audio");
    var resumed = decoder.Play(unicodeFile, "Restored MP3", true, savedPlayback.LastTrackPosition);
    watch.Restart(); long remaining = 0; double resumedEnergy = 0;
    while (watch.Elapsed < TimeSpan.FromSeconds(12))
    {
        if (resumed.Format != null)
        {
            int got = resumed.Format.Buffer.Read(samples); remaining += got;
            for (int i = 0; i < got; i++) resumedEnergy += samples[i] * samples[i];
            if (resumed.Finished && resumed.Format.Buffer.Available == 0) break;
        }
        Thread.Sleep(5);
    }
    Check(resumed.Failure.Length == 0 && resumed.Finished && Math.Abs((resumed.Format?.StartSeconds ?? -1) - 1.25) < 1.0 / 44100,
        "the native MP3 decoder seeks to the restored fractional position");
    Check(Math.Abs(remaining - 44100 * 2 * 1.75) <= 2 && resumedEnergy > 1, "resuming decodes only the remaining audio, preserving the correct track-end timing");
    var completed = decoder.Play(unicodeFile, "Completed MP3", true, 100000);
    watch.Restart();
    while (completed.Format == null && !completed.Finished && watch.Elapsed < TimeSpan.FromSeconds(12)) Thread.Sleep(5);
    Check(completed.Format?.StartSeconds == 0 && completed.Failure.Length == 0, "a completed or shortened MP3 safely restarts at the beginning");
    for (int n = 0; n < 100; n++) decoder.Play("http://127.0.0.1:1/unavailable", "Cancelled", false);
    var latest = decoder.Play(unicodeFile, "Latest", true);
    watch.Restart();
    while (latest.Format == null && latest.Failure.Length == 0 && watch.Elapsed < TimeSpan.FromSeconds(12)) Thread.Sleep(10);
    Check(ReferenceEquals(decoder.Current, latest) && latest.Format != null, "100 rapid station changes coalesce to the most recent selection");
    decoder.Stop(); Check(latest.Cancelled && decoder.Current == null, "Stop cancels playback and clears the current selection");
    var hls = decoder.Play(Path.Combine(module, "artifacts/fixture.m3u8"), "HLS fixture", true);
    watch.Restart();
    while ((hls.Format == null || hls.Format.Buffer.Available == 0) && !hls.Finished && watch.Elapsed < TimeSpan.FromSeconds(12)) Thread.Sleep(10);
    Check(hls.Format != null && hls.Format.Buffer.Available > 0, "HLS support decodes MP3 segments / " + hls.Failure);
    decoder.Stop();
    if (args.Contains("--live"))
    {
        var results = new List<(Station Station, bool Passed, string Failure, long Samples, int Frequency, int Channels, long Elapsed)>();
        foreach (var station in stations)
        {
            var live = decoder.Play(station.Stream, station.Name, false); watch.Restart(); long received = 0;
            while (watch.Elapsed < TimeSpan.FromSeconds(35))
            {
                if (live.Format != null) received += live.Format.Buffer.Read(samples);
                if (live.Format != null && received >= (long)live.Format.Frequency * live.Format.Channels) break;
                if (live.Finished) break;
                Thread.Sleep(10);
            }
            bool passed = live.Format != null && received >= (long)live.Format.Frequency * live.Format.Channels;
            results.Add((station, passed, live.Failure, received, live.Format?.Frequency ?? 0, live.Format?.Channels ?? 0, watch.ElapsedMilliseconds));
            Console.WriteLine((passed ? "DECODE OK " : "DECODE FAILED ") + station.Name + " / " + live.Failure);
        }
        File.WriteAllText(Path.Combine(module, "artifacts/live-stream-checks.json"), System.Text.Json.JsonSerializer.Serialize(new {
            CheckedUtc = DateTime.UtcNow, AudioPlayed = false, ManagedHttpForced = args.Contains("--managed-http"),
            Stations = results.Select(r => new { r.Station.Id, r.Station.Name, r.Station.Stream, r.Passed, r.Failure,
                DecodedSamples = r.Samples, r.Frequency, r.Channels, ElapsedMilliseconds = r.Elapsed })
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        foreach (var result in results)
            Check(result.Passed, "live stream decodes at least one second: " + result.Station.Name + " / " + result.Failure);
    }
}
finally { decoder.Dispose(); Check(decoder.WaitForExit(10000), "decoder worker shuts down and releases its streams"); }
Console.WriteLine($"{checks} RadioRamen checks passed. In-game interface and spatial playback require live acceptance.");
