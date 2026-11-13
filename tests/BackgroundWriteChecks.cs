using RadioRamen;
using Mono.Cecil;

internal static class BackgroundWriteChecks
{
    internal static void Run(string module,Action<bool,string> check)
    {
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var written=new List<string>();int caller=Environment.CurrentManagedThreadId,writer=0;
        using(var work=new LatestWork<string>(value=>{
            writer=Environment.CurrentManagedThreadId;
            if(value=="first"){entered.Set();if(!release.Wait(3000))throw new TimeoutException();}
            lock(written)written.Add(value);
        }))
        {
            work.Submit("first");check(entered.Wait(3000),"file worker starts separately from the publishing thread");
            for(int i=0;i<500;i++)work.Submit("snapshot "+i);
            check(written.Count==0&&writer!=caller,"a blocked file write does not block control or playback snapshot publication");
            release.Set();check(work.Flush(3000),"shutdown can finish the latest queued write");
            check(written.SequenceEqual(new[]{"first","snapshot 499"}),"rapid updates coalesce into one latest pending snapshot, without replaying stale states");
        }
        using(var work=new LatestWork<string>(value=>{if(value=="bad")throw new IOException("fixture");written.Add(value);}))
        {
            work.Submit("bad");check(work.Flush(3000)&&work.TakeError()=="fixture","background I/O failures are reported without escaping onto the game thread");
            work.Submit("recovered");check(work.Flush(3000)&&written.Last()=="recovered","a failed write does not permanently stop later saves");
        }
        string path=Path.Combine(module,"artifacts/background-settings-check.ini");
        File.Copy(Path.Combine(module,"config/RadioRamen.ini"),path,true);
        var settings=RadioSettings.Parse(File.ReadAllText(path));settings.LastTrackPosition=50.125;settings.LastTrack="resume.mp3";settings.LastSource=SavedSource.Music;settings.Volume=37;
        var snapshot=settings.Snapshot();settings.LastTrackPosition=0;settings.Volume=5;
        using(var work=new LatestWork<RadioSettings>(state=>state.Save(path)))
        {
            work.Submit(snapshot);check(work.Flush(3000),"queued playback settings finish before shutdown");
        }
        var restored=RadioSettings.Parse(File.ReadAllText(path));
        check(restored.LastTrackPosition==50.125&&restored.LastTrack=="resume.mp3"&&restored.LastSource==SavedSource.Music&&restored.Volume==37,
            "queued immutable settings preserve fractional MP3 resume and controls independently of later live changes");
        check(typeof(RadioSettings).GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic)
            .All(f=>f.FieldType.IsPrimitive||f.FieldType.IsEnum||f.FieldType==typeof(string)),"background settings snapshots contain no Unity objects or mutable collections");
        using var assembly=AssemblyDefinition.ReadAssembly(Path.Combine(module,"src/bin/Release/netstandard2.1/RadioRamen.dll"));
        var plugin=assembly.MainModule.GetType("RadioRamen.Plugin");
        check(new[]{"Update","SaveSettings","WriteStartupStatus"}.All(name=>!plugin.Methods.Single(m=>m.Name==name).Body.Instructions
            .Select(i=>i.Operand).OfType<MethodReference>().Any(m=>m.DeclaringType.FullName=="System.IO.File"&&m.Name.StartsWith("Write")
                ||m.DeclaringType.FullName=="RadioRamen.RadioSettings"&&m.Name=="Save")),
            "periodic radio update, diagnostics and playback checkpoint publication perform no synchronous file writes");
        check(plugin.Methods.Single(m=>m.Name=="OnApplicationQuit").Body.Instructions.Any(i=>i.Operand is MethodReference m&&m.Name=="Flush"),
            "application shutdown explicitly completes pending resume settings");
    }
}
