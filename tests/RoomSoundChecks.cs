using RadioRamen;

internal static class RoomSoundChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var room = new RoomFootprint(4,3);
        var route = new RoomRoute();
        var doors = Enumerable.Range(-4,9).Select(i => new RoomPoint(i*.2f,3)).ToArray();
        var source = new RoomPoint(0,0);
        check(!room.CrossesInterior(new RoomPoint(-4,-3),new RoomPoint(-4,3))
            && room.CrossesInterior(new RoomPoint(0,3),new RoomPoint(0,-4)),
            "exterior sound can follow a building edge but cannot take a shortcut through the building");
        var front = new RoomPoint(0,4); var back = new RoomPoint(0,-4);
        float frontPath = route.ThroughEntrance(room,source,front,doors);
        float backPath = route.ThroughEntrance(room,source,back,doors);
        check(Math.Abs(frontPath-4)<.001 && backPath>13,
            "an open entrance preserves a direct route while the rear wall requires an exterior detour");
        check(RoomSoundFade.Target(4,frontPath)==0 && RoomSoundFade.Target(4,backPath)>.7,
            "equally distant front and rear listeners receive different room transmission");
        check(float.IsNaN(route.ThroughEntrance(room,source,front,Array.Empty<RoomPoint>()))
            && RoomSoundFade.Target(4,float.NaN)==0,
            "unconfirmed entrances fall back to the accepted distance model instead of silencing playback");
        check(RoomSoundFade.Target(3.02f,3.02f)==0 && RoomSoundFade.RouteGain(50,3.02f,3.02f)==1,
            "crossing an open doorway by two centimetres retains a continuous clear path");
        foreach (int face in new[]{0,1,2,3})
        {
            RoomPoint Rotate(RoomPoint p)=>face switch { 1=>new(-p.X,-p.Z),2=>new(p.Z,p.X),3=>new(-p.Z,-p.X),_=>p };
            var square=new RoomFootprint(3,3);
            var aperture=Enumerable.Range(-4,9).Select(i=>Rotate(new RoomPoint(i*.2f,3))).ToArray();
            var listener=Rotate(new RoomPoint(.1f,3.001f));
            check(Math.Abs(route.ThroughEntrance(square,source,listener,aperture)-RoomPoint.Distance(source,listener))<.00001f,
                "a direct sound path between opening samples crosses wall face "+face+" without a sampling detour");
        }
        check(RoomSoundFade.Target(8,8)==0 && RoomSoundFade.RouteGain(50,8,8)==1,
            "the slatted entrance adds no fixed loss or muffling to a direct path, regardless of leaf position");
        check(Enumerable.Range(0,500).All(i => {
            var p=new RoomPoint(i*.02f-5,4); var q=new RoomPoint(p.X+.02f,p.Z);
            float a=route.ThroughEntrance(room,source,p,doors), b=route.ThroughEntrance(room,source,q,doors);
            return Math.Abs(a-b)<=.0201f;
        }), "the doorway route is spatially continuous while walking past the entrance");
        check(Enumerable.Range(0,500).All(i => {
            var p=new RoomPoint(5,i*.02f-5); var q=new RoomPoint(5,p.Z+.02f);
            return Math.Abs(route.ThroughEntrance(room,source,p,doors)-route.ThroughEntrance(room,source,q,doors))<=.0201f;
        }), "changing between shortest routes around building corners has no path-length jump");
        check(Math.Abs(route.Outside(room,new RoomPoint(0,3),new RoomPoint(0,-4))
            - route.Outside(room,new RoomPoint(0,-3),new RoomPoint(0,4)))<.001,
            "front/back exterior routing is symmetric for a symmetric building");
        // Native indoor-box footprints and entrance offsets, transformed from
        // the two reviewed restaurant scenes. No saved player position is used.
        foreach (var fixture in new[] { (HalfZ:2.6510f, DoorZ:.3963f), (HalfZ:3.1886f, DoorZ:.7465f) })
        {
            var native=new RoomFootprint(4.0259f,fixture.HalfZ);
            var openings=Enumerable.Range(-4,9).Select(i=>new RoomPoint(native.X,fixture.DoorZ+i*.21f)).ToArray();
            var inside=new RoomPoint(-3,fixture.DoorZ);
            var nearDoor=new RoomPoint(native.X+.5f,fixture.DoorZ);
            var nearRear=new RoomPoint(-native.X-1.5f,fixture.DoorZ);
            float doorPath=route.ThroughEntrance(native,inside,nearDoor,openings);
            float rearPath=route.ThroughEntrance(native,inside,nearRear,openings);
            check(RoomSoundFade.Target(RoomPoint.Distance(inside,nearDoor),doorPath)==0
                && RoomSoundFade.Target(RoomPoint.Distance(inside,nearRear),rearPath)>.9f,
                "native restaurant depth "+fixture.HalfZ+" preserves its open entrance and strongly muffles the short rear-wall shortcut");

            // Sample the actual boundary crossing, including the runtime indoor
            // branch, source inset, height difference and native distance rolloff.
            // Earlier default-open tests missed the runtime closed-door penalty.
            var acoustic=RoomFootprint.Restaurant(native.X,native.Z);
            float maxOutputStep=0,maxThresholdLoss=0,maxMix=0;
            foreach (int volume in new[]{25,50,100})
            foreach (var radio in new[]{new RoomPoint(1.53503f,2.53010f),new RoomPoint(-3,fixture.DoorZ),new RoomPoint(2,fixture.DoorZ-.6f)})
            foreach (float offset in new[]{-.7f,0,.7f})
            {
                float previous=-1;
                for(int step=-50;step<=50;step++)
                {
                    float normal=step*.02f;
                    var ear=new RoomPoint(acoustic.X+normal,fixture.DoorZ+offset+normal*.15f);
                    float horizontal=RoomPoint.Distance(radio,ear);
                    float direct=(float)Math.Sqrt(horizontal*horizontal+.27f*.27f);
                    float gain=1,mix=0;
                    if(!acoustic.Contains(ear))
                    {
                        var inset=acoustic.InsetSource(radio);
                        float path=route.ThroughEntrance(acoustic,inset,ear,openings)+RoomPoint.Distance(radio,inset);
                        float travel=Math.Max(direct,(float)Math.Sqrt(path*path+.27f*.27f));
                        gain=RoomSoundFade.RouteGain(volume,direct,travel);
                        mix=RoomSoundFade.Target(direct,travel);
                    }
                    float output=RadioRange.Gain(true,volume,direct)*gain;
                    if(previous>=0)maxOutputStep=Math.Max(maxOutputStep,Math.Abs(output-previous));
                    previous=output;maxMix=Math.Max(maxMix,mix);
                    if(step==1)maxThresholdLoss=Math.Max(maxThresholdLoss,1-gain);
                }
            }
            check(maxOutputStep<.012f,"native doorway depth "+fixture.HalfZ+" stays gradual in 2cm steps at 25/50/100 percent, including the indoor/outdoor switch (max "+maxOutputStep.ToString("G5")+")");
            check(maxThresholdLoss<.01f&&maxMix<.02f,"native doorway depth "+fixture.HalfZ+" has no threshold transmission drop or sudden muffling across its walkable width");
        }
        var fade=new RoomSoundFade();
        var edgeRoom=new RoomFootprint(4,3);
        check(edgeRoom.ContainsSource(new RoomPoint(4.2f,0))&&!edgeRoom.Contains(new RoomPoint(4.2f,0)),
            "wall-mounted radio slightly outside an inset indoor trigger still has a source room, without expanding the listener boundary");
        check(!edgeRoom.ContainsSource(new RoomPoint(4.5f,0)),"source allowance cannot swallow radios well outside the building");
        var projected=edgeRoom.InsetSource(new RoomPoint(4.2f,0));
        check(edgeRoom.Contains(projected)&&projected.X<4&&projected.X>3.9f,"near-wall source is projected safely into its room for entrance routing");
        check(float.IsFinite(route.ThroughEntrance(edgeRoom,projected,new RoomPoint(-5,0),new[]{new RoomPoint(4,0)})),
            "near-wall source produces a valid rear-wall route rather than a silent distance-only fallback");
        var physical=RoomFootprint.Restaurant(4.0258856f,2.6510153f);
        var reportedSource=new RoomPoint(1.53503f,2.53010f);var reportedEar=new RoomPoint(-3.94497f,1.84011f);
        check(physical.ContainsSource(reportedSource)&&!physical.Contains(reportedEar),"the reported rear-wall position is outside the acoustic interior, while the radio remains inside");
        var reportedDoors=Enumerable.Range(-4,9).Select(i=>new RoomPoint(3.9457f,.3963f+i*.21f)).ToArray();
        float reportedDirect=RoomPoint.Distance(reportedSource,reportedEar);
        float reportedRoute=route.ThroughEntrance(physical,physical.InsetSource(reportedSource),reportedEar,reportedDoors);
        check(reportedRoute>reportedDirect+6&&RoomSoundFade.RouteGain(35,reportedDirect,reportedRoute)==0,
            "the actual reported radio/listener geometry falls silent at the selected volume because its entrance route exceeds Nearby reach");
        check(RoomSoundFade.RouteGain(100,reportedDirect,reportedRoute)<.005f,"even full volume is strongly suppressed behind the reported brick wall");
        check(RadioRange.Gain(true,100,reportedDirect)*RoomSoundFade.RouteGain(100,reportedDirect,reportedRoute)<.0001f,
            "the accepted rear-wall position remains below 0.01 percent output at full volume without relying on a closed-door penalty (output "+(RadioRange.Gain(true,100,reportedDirect)*RoomSoundFade.RouteGain(100,reportedDirect,reportedRoute)).ToString("G5")+", detour "+(reportedRoute-reportedDirect).ToString("G5")+")");
        check(RadioRange.Gain(true,100,8)*RoomSoundFade.RouteGain(100,8,8)
            >RadioRange.Gain(true,50,8/RadioRange.Scale(100))*2,
            "full volume keeps its longer entrance-side reach with no artificial door loss");
        check(RoomSoundFade.RouteGain(50,4,float.NaN)==1,"unknown transmission retains native distance sound rather than muting it");
        for(int i=0;i<600;i++)fade.Advance(1,0,1/60f);
        check(fade.Gain==0 && fade.Cutoff>1590 && fade.Cutoff<1610,
            "a blocked long route can reach silence without a forced audible transmission floor");
        fade.Reset(); float maxGainStep=0;
        for(int i=0;i<600;i++) {
            float before=fade.Gain; fade.Advance(1,0,i==0?2f:1/60f);
            maxGainStep=Math.Max(maxGainStep,before-fade.Gain);
        }
        check(maxGainStep<.075f, "even a two-second frame stall cannot cause a sudden full wall attenuation step");
        for(int i=0;i<600;i++)fade.Advance(0,1,1/60f);
        check(fade.Gain>.9999 && fade.Cutoff>21999, "returning indoors restores clear playback smoothly");
        fade.Reset(); fade.Advance(float.NaN,0,1); fade.Advance(1,0,float.PositiveInfinity);
        check(fade.Amount==0 && fade.Gain==1 && fade.Cutoff==22000,
            "invalid measurements preserve baseline audio rather than muting it");
        check(Enumerable.Range(0,101).All(i => {
            var f=new RoomSoundFade(); for(int k=0;k<1000;k++)f.Advance(i/100f,1-i/100f,.02f);
            return f.Gain<=1 && f.Gain>=0 && f.Cutoff>=1599 && f.Cutoff<=22000;
        }), "room acoustics never amplify audio or request an unbounded filter");
        fade.Advance(1,0,1);fade.Reset();
        check(fade.Gain==1 && fade.Cutoff==22000, "Normal can restore the exact distance-only gain and bandwidth");
    }
}
