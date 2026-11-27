// GNU GPL v3. Deterministic lifecycle, heading, receiver and hold/retry replay.
using System.Globalization;
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.Configuration;
using AgOpenWeb.Models.GPS;
using AgOpenWeb.Models.Track;
using AgOpenWeb.Services.Passive;
using AgOpenWeb.Services.Pipeline;
using AgOpenWeb.Services.Track;
using Track = AgOpenWeb.Models.Track.Track;
namespace AgOpenWeb.PassiveHarness;
public static class Contracts
{
    private static TrackGuidanceInput Input(double t=0,bool requested=true,bool enabled=true, bool plane=true, ImplementGpsFix? fix=null)
    {
        var now=Program.Epoch.AddSeconds(t);
        return new() {Track=Track.FromABLine("Contract",new Vec3(0,-100,0),new Vec3(0,1000,0)),
            PivotPosition=new Vec3(0,20,0),SteerPosition=new Vec3(0,23,0),Wheelbase=3,MaxSteerAngle=35,GoalPointDistance=5,
            AvgSpeed=7.2,FixHeading=0,IsAutoSteerOn=true,IsHeadingSameWay=true,
            Passive=new(){Settings=new(){Enabled=enabled},Now=now,Requested=requested,HasLocalPlane=plane,
                Fix=fix??new(now,49,-98,4,2,0,double.NaN,double.NaN,now,0),ToolEast=.6,ToolNorth=12,Width=24.3,Context="contract"}};
    }
    public static List<Check> Run()
    {
        var checks=new List<Check>();
        void Check(string name,Func<bool> test,string detail)
        {try{checks.Add(new(name,test(),detail));}catch(Exception e){checks.Add(new(name,false,e.GetType().Name+": "+e.Message));}}
        var gates=new (string Name,Action<TrackGuidanceInput> Change)[] {
            ("Tool request off",x=>x.Passive=Input(20,requested:false).Passive),
            ("Passive mode disabled",x=>x.Passive=Input(20,enabled:false).Passive),
            ("Tractor steering off",x=>x.IsAutoSteerOn=false),
            ("Missing field coordinates",x=>x.Passive=Input(20,plane:false).Passive),
            ("Stale GPS",x=>x.Passive=Input(20,fix:new(Program.Epoch,49,-98,4,2,0,0,0,Program.Epoch,0)).Passive),
            ("Invalid GPS quality",x=>x.Passive=Input(20,fix:new(Program.Epoch.AddSeconds(20),49,-98,0,2,0,0,0,Program.Epoch.AddSeconds(20),0)).Passive),
            ("Below working speed",x=>x.AvgSpeed=1.9),
            ("Stanley selected",x=>x.UseStanley=true),
            ("U-turn bypass",x=>x.IsYouTurnTriggered=true),
            ("Invalid path",x=>x.Track.Points.Clear()),
            ("Invalid setting",x=>x.Passive!.Settings.Tracking=double.NaN)
        };
        foreach(var gate in gates) Check("Reset gate / "+gate.Name,()=>{
            var service=new TrackGuidanceService();TrackGuidanceOutput previous=null!;
            for(int i=0;i<200;i++)previous=service.CalculateGuidance(Input(i*.1));
            if(!previous.Passive.Engaged||Math.Abs(previous.Passive.Offset)<.05)return false;
            var input=Input(20);gate.Change(input);var after=service.CalculateGuidance(input);
            return !after.Passive.Engaged&&after.Passive.Offset==0&&double.IsFinite(after.SteerAngle);
        },"Start with a nonzero held correction; the triggering cycle must disarm and clear it.");
        Check("Acquisition guard",()=>{var input=Input();input.PivotPosition=new Vec3(.11,20,0);var p=new TrackGuidanceService().CalculateGuidance(input).Passive;
            return !p.Engaged&&p.Offset==0&&p.Reason.Contains("Acquire",StringComparison.Ordinal);},"No correction outside the tractor's 10 cm acquisition gate.");
        Check("Disabled mode parity",()=>{var input=Input(enabled:false);input.PivotPosition=new Vec3(.6,20,0);
            var a=new TrackGuidanceService().CalculateGuidance(input);input.Passive=null;var b=new TrackGuidanceService().CalculateGuidance(input);
            return a.SteerAngle==b.SteerAngle&&a.GoalPoint.Equals(b.GoalPoint);},"Disabled passive produces exactly the original steering and goal.");
        Check("Independent tool GPS intent",()=>{var p=new PipelineIntents();p.RequestPassiveGuidance(true);p.RequestToolGpsSimulation(true);p.RequestToolGpsSimulation(false);
            var b=p.Drain();return b.PassiveRequested==true&&b.ToolGpsSimulation==false&&p.Drain().ToolGpsSimulation==null;},"Last-wins simulator intent does not change the steering request.");
        Check("IMU requires GNSS anchor",()=>{var resolver=new CToolHeadingResolver();bool valid=resolver.Resolve(Program.Epoch,0,0,true,Program.Epoch,2,false,true,
            double.NaN,Program.Epoch,double.NaN,Program.Epoch,97,Program.Epoch,0,out _);return !valid;},"A raw IMU yaw cannot be treated as absolute implement heading.");
        Check("Dual heading wraps through north",()=>{var r=new CToolHeadingResolver();bool ok=true;double[] heads={359,0,1};
            for(int i=0;i<heads.Length;i++){var t=Program.Epoch.AddSeconds(i*.1);ok&=r.Resolve(t,0,i*.2,true,t,2,false,true,heads[i],t,double.NaN,t,double.NaN,t,0,out double h)&&Math.Abs(CToolHeadingResolver.Difference(h,heads[i]))<1e-9;}return ok;},"359°, 0°, 1° heading samples preserve direction.");
        Check("Hold escapes a stalled correction",()=>{
            var c=new CPassiveController();bool retry=false;
            for(int i=0;i<=100;i++){c.Update(Program.Epoch.AddSeconds(i*.1),.6,0,8,100,100,100,100,2,true);retry|=c.CorrectionReason=="Not improving";}
            return retry;
        },"Early retry must occur during the 8-second hold when error does not improve.");
        Check("Early retry can be disabled",()=>{
            var c=new CPassiveController();bool retry=false;
            for(int i=0;i<=100;i++){c.Update(Program.Epoch.AddSeconds(i*.1),.6,0,8,100,100,100,100,2,false);retry|=c.CorrectionReason=="Not improving";}
            return !retry;
        },"Manual hold preference is respected.");
        foreach(double degrees in new[]{0d,45,180,270}) Check("Antenna transform / "+degrees.ToString(CultureInfo.InvariantCulture)+" degrees",()=>{
            var input=Input();var cfg=input.Passive!.Settings;cfg.AntennaOffset=1.1;cfg.AntennaToWorkingPoint=3.4;
            double h=degrees*Math.PI/180;var a=ToolGpsSimulator.AntennaPosition(.6,12,h,cfg);
            input.Passive=new(){Settings=cfg,Now=Program.Epoch,Requested=true,HasLocalPlane=true,ToolEast=a.East,ToolNorth=a.North,Width=24.3,
                Fix=new(Program.Epoch,49,-98,4,2,degrees,double.NaN,double.NaN,Program.Epoch,0)};
            var p=new TrackGuidanceService().CalculateGuidance(input).Passive;
            return Math.Abs(p.ToolEast-.6)<1e-9&&Math.Abs(p.ToolNorth-12)<1e-9;
        },"Virtual antenna resolves back to the independent working-bar position.");
        foreach(double radius in new[]{-40d,-25,25,40}) Check("Steady curve geometry / R="+radius.ToString(CultureInfo.InvariantCulture),()=>{
            var g=new CPassiveGeometry{Trailing=true,Hitch=-1.5,Drawbar=8,Width=24.3};
            double vehicle=Math.Abs(radius)-Math.Sign(radius)*g.Offset(1/radius);
            double bar=Math.Sqrt(vehicle*vehicle+1.5*1.5-8*8);
            return Math.Abs(bar-Math.Abs(radius))<1e-8;
        },"Independent concentric-circle radius identity, both turn directions.");
        string Sentence(string body){int sum=0;foreach(char c in body)sum^=c;return "$"+body+"*"+sum.ToString("X2",CultureInfo.InvariantCulture);}
        Check("Receiver / dual GNSS",()=>ImplementGpsReceiver.TryDecode(Sentence("PAOGI,120000,4900.000,N,09800.000,W,4,14,0.8,300,0.5,4,90,0,0,0,1,1,100,88"),Program.Epoch,out var f)&&f!.DualHeading==90&&f.Longitude==-98,"Decode implement input without routing it into tractor GPS.");
        Check("Receiver / single antenna no IMU",()=>ImplementGpsReceiver.TryDecode(Sentence("PANDA,120000,4900.000,N,09800.000,W,4,14,0.8,300,0.5,4,65535,0,0,0"),Program.Epoch,out var f)&&double.IsNaN(f!.ImuHeading)&&double.IsNaN(f.DualHeading),"Missing heading remains missing; movement can supply it later.");
        Check("Receiver / bad checksum rejected",()=>!ImplementGpsReceiver.TryDecode("$PAOGI,1*00",Program.Epoch,out _),"Malformed input cannot become a valid implement fix.");
        return checks;
    }
}
