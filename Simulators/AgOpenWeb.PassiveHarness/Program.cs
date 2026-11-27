// GNU GPL v3. Offline bench only: no host, network transport, or steering hardware.
using System.Globalization;
using System.Text.Json;
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.Configuration;
using AgOpenWeb.Models.GPS;
using AgOpenWeb.Models.Track;
using AgOpenWeb.Services.Passive;
using AgOpenWeb.Services.Pipeline;
using AgOpenWeb.Services.Track;
using Track = AgOpenWeb.Models.Track.Track;

namespace AgOpenWeb.PassiveHarness;

public static class Program
{
    internal static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static int Main(string[] args)
    {
        if (args.Contains("--draft")) return DraftSuite.Run(args);
        string output = args.Length == 2 && args[0] == "--output" ? args[1] : "passive-harness-results";
        Directory.CreateDirectory(output);
        var checks = Contracts.Run();
        var runs = new List<Run>();
        foreach (var scenario in Scenarios.All())
        {
            var baseline = Simulation.Run(scenario, false);
            var passive = Simulation.Run(scenario, true);
            runs.Add(baseline); runs.Add(passive);
            checks.Add(new(scenario.Name + ": finite, bounded output", passive.Safe, passive.Detail));
            if(scenario.Radius!=0&&scenario.Shape=="circle"&&scenario.Draft==0&&scenario.Drawbar==8)
            {
                double r=Math.Abs(scenario.Radius), expected=r-Math.Sqrt(r*r+scenario.Hitch*scenario.Hitch-scenario.Drawbar*scenario.Drawbar);
                checks.Add(new(scenario.Name+": independent model cross-check",Math.Abs(baseline.Rms-expected)<.10,
                    FormattableString.Invariant($"Passive-off RMS {baseline.Rms:F3} m; analytic steady trailer off-tracking {expected:F3} m (10 cm numerical tolerance).")));
            }
            if (scenario.Acceptance)
            {
                bool improves = passive.Rms <= baseline.Rms * .70;
                double tolerance = scenario.Radius == 0 ? .20 : .30;
                checks.Add(new(scenario.Name + ": tracking acceptance", improves && passive.Rms <= tolerance,
                    FormattableString.Invariant($"Measured tool RMS {passive.Rms:F3} m, baseline {baseline.Rms:F3} m; requires ≤{tolerance:F2} m and ≥30% reduction.")));
            }
        }
        var report = new Report("AgOpenWeb passive guidance harness", DateTime.UtcNow,
            "Independent bicycle/trailer kinematics; actual TrackGuidanceService, PassiveGuidanceService and heading resolver. Synthetic soil disturbance is not a field-validated soil model. No U-turn planning or calibration changes.",
            checks, runs);
        var options = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(report, options));
        ReportWriter.Write(Path.Combine(output, "report.html"), report);
        using (var csv = new StreamWriter(Path.Combine(output, "samples.csv")))
        {
            csv.WriteLine("scenario,passive,time_s,tractor_e_m,tractor_n_m,tool_e_m,tool_n_m,tool_error_m,offset_m,steer_deg,engaged,reason");
            foreach(var run in runs) foreach(var p in run.Samples)
                csv.WriteLine(string.Join(",", "\""+run.Name+"\"",run.Passive,p.T.ToString("F2",CultureInfo.InvariantCulture),
                    p.E.ToString("F5",CultureInfo.InvariantCulture),p.N.ToString("F5",CultureInfo.InvariantCulture),
                    p.ToolE.ToString("F5",CultureInfo.InvariantCulture),p.ToolN.ToString("F5",CultureInfo.InvariantCulture),
                    p.Error.ToString("F5",CultureInfo.InvariantCulture),p.Offset.ToString("F5",CultureInfo.InvariantCulture),
                    p.Steer.ToString("F5",CultureInfo.InvariantCulture),p.Engaged,"\""+p.Reason.Replace("\"","\"\"")+"\""));
        }
        foreach(var c in checks) Console.WriteLine($"{(c.Pass ? "PASS" : "FAIL")} {c.Name}: {c.Detail}");
        int failed = checks.Count(c=>!c.Pass);
        Console.WriteLine($"{checks.Count-failed}/{checks.Count} checks passed. Report: {Path.GetFullPath(Path.Combine(output,"report.html"))}");
        return failed == 0 ? 0 : 1;
    }
}
public record Check(string Name, bool Pass, string Detail);
public record Sample(double T,double E,double N,double ToolE,double ToolN,double Error,double Offset,double Steer,bool Engaged,string Reason);
public record Run(string Name,bool Passive,bool Safe,string Detail,double Rms,double Peak,double EngagedPercent,Scenario Setup,List<Sample> Samples);
public record Report(string Title,DateTime CreatedUtc,string Model,List<Check> Checks,List<Run> Runs);
public record Scenario(string Name,double Radius=0,double Speed=7.2,double Drawbar=8,double Hitch=-1.5,double WorkingBar=0,
    double Draft=.10,double Interval=4,bool Geometry=true,bool Opposite=false,string Heading="dual",bool Dropout=false,
    double Noise=0,double Limit=2,bool Acceptance=false,string Shape="circle");
public static class Scenarios
{
    public static IEnumerable<Scenario> All()
    {
        yield return new("Straight / dual GNSS",Acceptance:true);
        yield return new("Straight / opposite travel",Opposite:true,Acceptance:true);
        yield return new("Straight / position-derived heading",Heading:"position",Acceptance:true);
        yield return new("Straight / single GNSS plus IMU",Heading:"imu",Acceptance:true);
        yield return new("Straight / GPS noise 2 cm",Noise:.02,Acceptance:true);
        yield return new("Straight / continuous correction",Interval:0,Acceptance:true);
        yield return new("Straight / GPS dropout and rearm",Dropout:true);
        yield return new("Right curve / 40 m radius",Radius:40,Draft:0,Acceptance:true);
        yield return new("Left curve / 40 m radius",Radius:-40,Draft:0,Acceptance:true);
        yield return new("Right curve / feedback only",Radius:40,Draft:0,Geometry:false);
        yield return new("Right curve / 25 m radius",Radius:25,Draft:0,Acceptance:true);
        yield return new("Left curve / 25 m radius",Radius:-25,Draft:0,Acceptance:true);
        yield return new("Right curve / default 1 m limit",Radius:25,Draft:0,Limit:1,Acceptance:true);
        yield return new("Curve / slow 4 kmh",Radius:40,Speed:4,Draft:0,Acceptance:true);
        yield return new("Curve / fast 12 kmh",Radius:40,Speed:12,Draft:0,Acceptance:true);
        yield return new("Straight / no side-draft",Draft:0);
        yield return new("Curve entry / right 25 m",Radius:25,Draft:0,Shape:"entry",Acceptance:true);
        yield return new("Curve entry / left 25 m",Radius:-25,Draft:0,Shape:"entry",Acceptance:true);
        yield return new("S-curve / curvature reversals",Radius:40,Draft:0,Shape:"s",Acceptance:true);
        yield return new("Stress / long drawbar and 1 m offset limit",Radius:20,Drawbar:12,Limit:1,Draft:0);
        yield return new("Stress / noisy single antenna on curve",Radius:30,Heading:"position",Noise:.05,Draft:.10);
    }
}
public static class Simulation
{
    public static Run Run(Scenario s,bool passive)
    {
        const double dt=.1, duration=150, wheelbase=3;
        double speed=s.Speed/3.6, e=0,n=0,h=s.Opposite?Math.PI:0, steer=0;
        if(s.Shape=="entry")n=-40;
        double beta=h-(s.Radius==0?.075:0), v=speed;
        var random=new Random(7411); var samples=new List<Sample>();
        Track track=s.Shape=="entry"?Entry(s.Radius):s.Shape=="s"?SCurve():s.Radius==0 ? Track.FromABLine("Straight",new Vec3(0,-1000,0),new Vec3(0,1000,0)) : Circle(s.Radius);
        var settings=new PassiveGuidanceConfig {Enabled=passive,UseGeometry=s.Geometry,Interval=s.Interval,StraightLimit=s.Limit,CurveLimit=s.Limit};
        var service=new TrackGuidanceService();TrackGuidanceState? previous=null;bool safe=true;
        double toolE=0,toolN=0;string problem="";
        for(int tick=0;tick<(int)(duration/dt);tick++)
        {
            double t=tick*dt;
            // Model equations use hitch velocity projected onto the trailer lateral axis.
            // No production tool-position or passive geometry code generates ground truth.
            toolE=e+s.Hitch*Math.Sin(h)-(s.Drawbar+s.WorkingBar)*Math.Sin(beta);
            toolN=n+s.Hitch*Math.Cos(h)-(s.Drawbar+s.WorkingBar)*Math.Cos(beta);
            double noiseE=s.Noise*(random.NextDouble()*2-1), noiseN=s.Noise*(random.NextDouble()*2-1);
            var now=Program.Epoch.AddSeconds(t);
            bool outage=s.Dropout && t>=40&&t<45, arm=!(s.Dropout&&t>=70&&t<74);
            double dual=s.Heading=="dual"?Degrees(beta):double.NaN;
            double course=s.Heading=="imu"?Degrees(beta):double.NaN;
            var fix=new ImplementGpsFix(outage?now.AddSeconds(-3):now,49,-98,4,v,dual,course,
                s.Heading=="imu"?Degrees(beta)+23:double.NaN,now,0);
            var input=new TrackGuidanceInput {
                Track=track,PivotPosition=new Vec3(e,n,h),SteerPosition=new Vec3(e+wheelbase*Math.Sin(h),n+wheelbase*Math.Cos(h),h),
                FixHeading=h,Wheelbase=wheelbase,MaxSteerAngle=35,GoalPointDistance=Math.Max(4,v*2),AvgSpeed=s.Speed,
                IsAutoSteerOn=true,IsHeadingSameWay=!s.Opposite,PreviousState=previous,
                Passive=new(){Settings=settings,Now=now,Requested=arm,Fix=fix,HasLocalPlane=true,ToolEast=toolE+noiseE,ToolNorth=toolN+noiseN,
                    Context="bench",Trailing=true,Hitch=s.Hitch,Drawbar=s.Drawbar,WorkingBar=s.WorkingBar,Width=24.3}
            };
            var result=service.CalculateGuidance(input);previous=result.State;
            bool finite=new[]{result.SteerAngle,result.Passive.Offset,e,n,beta}.All(double.IsFinite);
            if(!finite || Math.Abs(result.Passive.Offset)>s.Limit+1e-8 || Math.Abs(result.SteerAngle)>35+1e-8 ||
                (passive&&(outage||!arm)&&(result.Passive.Engaged||Math.Abs(result.Passive.Offset)>1e-8)))
            {safe=false;problem="Non-finite/bounds/gating violation at "+t.ToString("F1",CultureInfo.InvariantCulture);break;}
            samples.Add(new(t,e,n,toolE,toolN,(s.Shape=="circle"?Error(s.Radius,toolE,toolN):PathError(track.Points,toolE,toolN)),result.Passive.Offset,result.SteerAngle,result.Passive.Engaged,result.Passive.Reason));
            double demand=Math.Clamp(result.SteerAngle*Math.PI/180,-35*Math.PI/180,35*Math.PI/180);
            steer+=Math.Clamp((demand-steer)*dt/.35,-30*Math.PI/180*dt,30*Math.PI/180*dt);
            double yaw=v/wheelbase*Math.Tan(steer);
            double draft=t<15?0:s.Draft;
            double trailerYaw=(v*Math.Sin(h-beta)+s.Hitch*yaw*Math.Cos(h-beta)+draft)/s.Drawbar;
            e+=v*Math.Sin(h)*dt;n+=v*Math.Cos(h)*dt;h+=yaw*dt;beta+=trailerYaw*dt;
        }
        var late=samples.Where(p=>s.Shape=="entry"?p.T>=25&&p.T<=70:p.T>=100).ToArray();
        double rms=late.Length==0?999:Math.Sqrt(late.Average(p=>p.Error*p.Error));
        return new(s.Name,passive,safe,problem.Length>0?problem:(s.Shape=="entry"?"150 s; RMS window 25–70 s covers entry, arc and exit.":"150 s; RMS window 100–150 s.")+" Bounds and dropout gates checked every 100 ms.",
            rms,samples.Count==0?999:samples.Max(p=>Math.Abs(p.Error)),samples.Count==0?0:samples.Count(p=>p.Engaged)*100.0/samples.Count,s,samples);
    }
    public static double Error(double radius,double e,double n) => radius==0?e:Math.Sign(radius)*(Math.Abs(radius)-Math.Sqrt((e-radius)*(e-radius)+n*n));
    private static double Degrees(double radians)=>(radians*180/Math.PI%360+360)%360;
    // Analytic path coordinates generate these references independently of production curve helpers.
    private static Track Entry(double radius)
    {
        var points=new List<Vec3>{new(0,-1000,0),new(0,0,0)};double r=Math.Abs(radius),sign=Math.Sign(radius);
        for(int i=1;i<=180;i++){double a=i*Math.PI/360;points.Add(new(sign*r*(1-Math.Cos(a)),r*Math.Sin(a),sign*a));}
        points.Add(new(sign*(r+1000),r,sign*Math.PI/2));return Track.FromCurve("Straight–arc–straight",points);
    }
    private static Track SCurve()
    {
        var points=new List<Vec3>{new(0,-1000,0)};
        for(int i=0;i<=800;i++){double n=i*.5,k=2*Math.PI/100;points.Add(new(6*(1-Math.Cos(k*n)),n,Math.Atan2(6*k*Math.Sin(k*n),1)));}
        return Track.FromCurve("S",points);
    }
    private static double PathError(IList<Vec3> points,double e,double n)
    {
        double distance=double.PositiveInfinity,signed=0;
        for(int i=1;i<points.Count;i++){
            double x=points[i].Easting-points[i-1].Easting,y=points[i].Northing-points[i-1].Northing;
            double lengthSquared=x*x+y*y;if(lengthSquared==0)continue;
            double u=Math.Clamp(((e-points[i-1].Easting)*x+(n-points[i-1].Northing)*y)/lengthSquared,0,1);
            double de=e-points[i-1].Easting-u*x,dn=n-points[i-1].Northing-u*y,d2=de*de+dn*dn;
            if(d2<distance){distance=d2;signed=Math.CopySign(Math.Sqrt(d2),de*y-dn*x);}
        }
        return signed;
    }
    private static Track Circle(double radius)
    {
        double r=Math.Abs(radius),direction=Math.Sign(radius);var points=new List<Vec3>();
        for(int i=0;i<720;i++){double a=i*Math.PI*2/720;points.Add(new(radius-direction*r*Math.Cos(a),r*Math.Sin(a),direction*a));}
        return Track.FromCurve("Circle",points,true);
    }
}
