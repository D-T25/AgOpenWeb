// GNU GPL v3. Force-based disturbance bench; all plant parameters are illustrative, not fitted field data.
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using AgOpenWeb.Models.Base;
using AgOpenWeb.Models.GPS;
using AgOpenWeb.Models.Track;
using AgOpenWeb.Services.Track;
using Track=AgOpenWeb.Models.Track.Track;
namespace AgOpenWeb.PassiveHarness;
public record DraftConfig(int Seeds=3,double DurationS=180,double PhysicsStepS=.01,List<DraftCase>? Cases=null);
public record DraftSample(double T,double E,double N,double H,double Beta,double ToolE,double ToolN,double Error,
    double Offset,double Steer,double Slope,double SoilForce,double DraftForce,double GravityForce,double GripUsage,
    double Speed,bool Engaged,string Reason,double MeasuredE,double MeasuredN,double HitchE,double HitchN);
public record DraftRun(string Name,int Seed,bool Passive,DraftCase Setup,bool Safe,string Problem,double RmsM,double P95M,
    double PeakM,double MeanErrorM,double StdDevM,double CrossingsPerMinute,double OffsetLimitedPercent,double GripLimitedPercent,double EngagedPercent,
    [property:JsonIgnore] List<DraftSample> Samples);
public record DraftReport(string Title,string Model,DateTime CreatedUtc,DraftConfig Config,List<Check> Checks,List<DraftRun> Runs);

public static class DraftSuite
{
    internal static readonly JsonSerializerOptions Json=new(){WriteIndented=true,PropertyNamingPolicy=JsonNamingPolicy.CamelCase,PropertyNameCaseInsensitive=true};
    public static int Run(string[] args)
    {
        string Argument(string key,string fallback){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        string output=Argument("--output","draft-results"),configPath=Argument("--config","");
        try
        {
            var config=configPath.Length>0?JsonSerializer.Deserialize<DraftConfig>(File.ReadAllText(configPath),Json)!:new();
            config=config with {Cases=(config.Cases??Cases().ToList()).Select(c=>c with {Plant=c.Plant??new(),Sensors=c.Sensors??new()}).ToList()};Validate(config);
            Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"draft-settings.json"),JsonSerializer.Serialize(config,Json));
            var checks=ModelChecks();var runs=new List<DraftRun>();
            foreach(var c0 in config.Cases!)for(int seed=1;seed<=config.Seeds;seed++)
            {
                var random=new Random(seed*7919);var p=c0.Plant??new();
                var c=c0 with {Plant=p with {MassKg=p.MassKg*(.9+.2*random.NextDouble()),CorneringStiffnessNPerRad=p.CorneringStiffnessNPerRad*(.8+.4*random.NextDouble())},Sensors=c0.Sensors??new()};
                var off=Drive(c,seed,false,config.DurationS,config.PhysicsStepS);var on=Drive(c,seed,true,config.DurationS,config.PhysicsStepS);
                runs.Add(off);runs.Add(on);
                checks.Add(new(c.Name+" / seed "+seed+" / bounds and full duration",off.Safe&&on.Safe,
                    off.Safe&&on.Safe?"Finite forces/positions, ≤35° commanded steer, configured offset cap, hitch constraint and full configured duration evaluated.":off.Problem+" "+on.Problem));
                if(!c.Stress)checks.Add(new(c.Name+" / seed "+seed+" / tracking",on.Safe&&off.Safe&&on.RmsM<=(c.Shape=="straight"?.20:.30)&&on.RmsM<=off.RmsM*.70,
                    FormattableString.Invariant($"RMS {on.RmsM:F3} m versus {off.RmsM:F3} m off. Same ≤20/30 cm and ≥30% improvement target; evaluation includes transitions (entry turns: 25–70 s; others: 30 s to end).")));
                Console.WriteLine(FormattableString.Invariant($"DRAFT {c.Name} seed {seed}: off {off.RmsM:F3} m, on {on.RmsM:F3} m; limited {on.OffsetLimitedPercent:F1}%"));
            }
            var convergenceCase=config.Cases.First(c=>c.Shape=="straight") with {Plant=new(),Sensors=new(PositionSigmaM:0,HeadingSigmaDegrees:0,RollSigmaDegrees:0,SlowBiasM:0)};
            var coarse=Drive(convergenceCase,7411,true,config.DurationS,config.PhysicsStepS);var fine=Drive(convergenceCase,7411,true,config.DurationS,config.PhysicsStepS/2);
            checks.Add(new("Physics timestep convergence",Math.Abs(coarse.RmsM-fine.RmsM)<.02,
                FormattableString.Invariant($"{config.PhysicsStepS*1000:F1} ms RMS {coarse.RmsM:F4} m; {config.PhysicsStepS*500:F1} ms RMS {fine.RmsM:F4} m. Difference must be below 2 cm.")));
            var report=new DraftReport("Implement draft: straight and curve simulation",
                "Independent planar rigid trailer with yaw inertia, nonlinear friction-limited cornering resistance, gravity and asymmetric draft forces; hitch reaction affects tractor slip/yaw. 100 Hz physics, 10 Hz actual production guidance, delayed/noisy 5 Hz implement GNSS. Illustrative engineering parameters; not a field-identified soil/tire model or commercial system replica.",
                DateTime.UtcNow,config,checks,runs);
            File.WriteAllText(Path.Combine(output,"draft-results.json"),JsonSerializer.Serialize(report,Json));
            using(var csv=new StreamWriter(Path.Combine(output,"draft-samples.csv")))
            {
                csv.WriteLine("case,seed,passive,time_s,tractor_e_m,tractor_n_m,tractor_heading_rad,tool_heading_rad,tool_e_m,tool_n_m,true_error_m,offset_m,steer_deg,slope_deg,soil_N,asymmetric_draft_N,gravity_N,grip_usage,speed_mps,engaged,reason,measured_bar_e_m,measured_bar_n_m");
                foreach(var r in runs)foreach(var s in r.Samples)
                    csv.WriteLine(string.Join(",",new[]{"\""+r.Name.Replace("\"","\"\"")+"\"",r.Seed.ToString(CultureInfo.InvariantCulture),r.Passive.ToString()}
                        .Concat(new[]{s.T,s.E,s.N,s.H,s.Beta,s.ToolE,s.ToolN,s.Error,s.Offset,s.Steer,s.Slope,s.SoilForce,s.DraftForce,s.GravityForce,s.GripUsage,s.Speed}.Select(v=>v.ToString("F6",CultureInfo.InvariantCulture)))
                        .Concat(new[]{s.Engaged.ToString(),"\""+s.Reason+"\"",s.MeasuredE.ToString("F6",CultureInfo.InvariantCulture),s.MeasuredN.ToString("F6",CultureInfo.InvariantCulture)})));
            }
            DraftReportWriter.Write(Path.Combine(output,"draft-simulation.html"),report);
            foreach(var check in checks.Where(c=>!c.Pass))Console.WriteLine("FAIL "+check.Name+": "+check.Detail);
            Console.WriteLine($"DRAFT SUMMARY: {checks.Count(c=>c.Pass)}/{checks.Count} checks passed; {runs.Count} trajectories. {Path.GetFullPath(output)}");
            return checks.All(c=>c.Pass)?0:1;
        }
        catch(Exception e){Console.Error.WriteLine("Draft harness: "+e.Message);return 2;}
    }
    public static IEnumerable<DraftCase> Cases()
    {
        var p=new DraftPlantParameters();
        yield return new("Straight / 3 deg side-slope",Plant:p);
        yield return new("Straight / draft on level ground",Plant:p with {SlopeDegrees=0});
        yield return new("Straight / mild 1 deg side-slope",Plant:p with {SlopeDegrees=1});
        yield return new("Straight / moderate 2 deg side-slope",Plant:p with {SlopeDegrees=2});
        yield return new("Straight / opposite side-slope",Plant:p with {SlopeDegrees=-3,Asymmetry=-.02});
        yield return new("Straight / uneven soil patches",Terrain:"patches",Plant:p);
        yield return new("Straight / slope reversal",Terrain:"reversal",Plant:p);
        yield return new("Straight / rolling terrain",Terrain:"rolling",Plant:p);
        yield return new("Straight / heavy 12 m drawbar",Plant:p with {MassKg=4500,DrawbarM=12});
        yield return new("Right curve 40 m / slope and draft","circle",40,Plant:p);
        yield return new("Left curve 40 m / slope and draft","circle",-40,Plant:p);
        yield return new("Right curve 25 m / soft soil patches","circle",25,"patches",p);
        yield return new("Left curve 25 m / soft soil patches","circle",-25,"patches",p);
        yield return new("S-curve / rolling terrain","s",40,"rolling",p with {SlopeDegrees=2});
        yield return new("Turn entry / cross-slope","entry",25,"patches",p with {SlopeDegrees=2});
        yield return new("Straight / GNSS plus IMU",Plant:p,Sensors:new(Heading:"imu"));
        yield return new("Right curve / GNSS plus IMU","circle",40,Plant:p,Sensors:new(Heading:"imu"));
        yield return new("Straight / position-only GNSS",Plant:p,Sensors:new(Heading:"position"));
        yield return new("Right curve / position-only GNSS","circle",40,Plant:p,Sensors:new(Heading:"position"));
        yield return new("Stress / 2 Hz GNSS, 300 ms delay, 5 cm noise",Plant:p,Sensors:new(.05,2,.3),Stress:true);
        yield return new("Stress / fast curve and patches","circle",25,"patches",p,SpeedKph:12,Stress:true);
        yield return new("Stress / long trailer, tight curve, 1 m cap","circle",20,Plant:p with {DrawbarM=12,MassKg=4500},OffsetLimitM:1,Stress:true);
        yield return new("Stress / 6 deg hill and weak grip",Plant:p with {SlopeDegrees=6,Friction=.12,CorneringStiffnessNPerRad=10000},Stress:true);
        yield return new("Stress / RTK outage on S-curve","s",Plant:p,Sensors:new(Dropout:true),Stress:true);
        yield return new("Straight / entered drawbar 20 percent too long",Plant:p,ProfileDrawbarScale:1.2);
        yield return new("Right curve / entered drawbar 20 percent too long","circle",40,Plant:p,ProfileDrawbarScale:1.2);
        yield return new("Stress / 50 mm hitch play and slow steering",Plant:p with {HitchPlayM=.05,SteeringLagS=.75},Stress:true);
        yield return new("Diagnostic / same draft, ideal GNSS",Plant:p,Sensors:new(PositionSigmaM:0,ImplementHz:10,ImplementDelayS:0,TractorDelayS:0,HeadingSigmaDegrees:0,RollSigmaDegrees:0,SlowBiasM:0,ImuDriftDegPerSec:0));
        yield return new("Diagnostic / same draft, 10 Hz GNSS",Plant:p,Sensors:new(ImplementHz:10));
        yield return new("Diagnostic / same draft, continuous correction",Plant:p,IntervalS:0);
        yield return new("Right curve / feedback only","circle",40,Plant:p,Geometry:false);
    }
    private static void Validate(DraftConfig c)
    {
        if(c.Seeds<1||c.Seeds>20||c.DurationS<60||c.DurationS>600||c.PhysicsStepS<.002||c.PhysicsStepS>.02||Math.Abs(.1/c.PhysicsStepS-Math.Round(.1/c.PhysicsStepS))>1e-8)
            throw new ArgumentException("Use 1–20 seeds, 60–600 s duration, and a 2–20 ms physics step dividing 100 ms.");
        if(c.Cases is not {Count:>0}||c.Cases.Count>100||c.Cases.Count*c.Seeds*c.DurationS*20>1000000||!c.Cases.Any(x=>x.Shape=="straight"))throw new ArgumentException("Supply 1–100 cases including a straight reference case.");
        if(c.Cases.Select(x=>x.Name).Distinct().Count()!=c.Cases.Count||c.Cases.Any(x=>string.IsNullOrWhiteSpace(x.Name)))throw new ArgumentException("Case names must be nonempty and unique.");
        foreach(var x in c.Cases){var p=x.Plant??new();var s=x.Sensors??new();
            if(new[]{p.MassKg,p.DrawbarM,p.WidthM,p.DepthM,p.CorneringStiffnessNPerRad,p.SteeringLagS,p.TractorMassKg,p.TractorCorneringStiffnessNPerRad,p.TractorYawDampingNms,s.ImplementHz}.Any(v=>!double.IsFinite(v)||v<=0)
                ||new[]{p.HitchM,p.WorkingBarM,p.Asymmetry,p.LongitudinalDraftN,p.SlopeDegrees,p.HitchPlayM,p.Friction,s.PositionSigmaM,s.ImplementDelayS,s.TractorDelayS,s.HeadingSigmaDegrees,s.RollSigmaDegrees,s.SlowBiasM,s.ImuDriftDegPerSec,x.SpeedKph,x.OffsetLimitM,x.ProfileDrawbarScale,x.RadiusM,x.IntervalS,x.InitialErrorM}.Any(v=>!double.IsFinite(v))
                ||p.MassKg<100||p.MassKg>20000||p.WidthM<.5||p.WidthM>30||p.DrawbarM<.5||p.DrawbarM>20||Math.Abs(p.HitchM)>5||p.WorkingBarM<0||p.WorkingBarM>5||Math.Abs(p.Asymmetry)>.15||p.LongitudinalDraftN>250000||s.RollSigmaDegrees<0||s.RollSigmaDegrees>5||s.SlowBiasM<0||s.SlowBiasM>.2||Math.Abs(s.ImuDriftDegPerSec)>.1||s.PositionSigmaM>.2||s.HeadingSigmaDegrees>5||p.Friction<=0||p.Friction>1||Math.Abs(p.SlopeDegrees)>12||p.HitchPlayM<0||p.HitchPlayM>.1||p.LongitudinalDraftN<0||s.PositionSigmaM<0||s.HeadingSigmaDegrees<0||s.ImplementDelayS<0||s.ImplementDelayS>1||s.TractorDelayS<0||s.TractorDelayS>1||s.ImplementHz>10||x.SpeedKph<3||x.SpeedKph>15||x.OffsetLimitM<.1||x.OffsetLimitM>3||x.ProfileDrawbarScale<.5||x.ProfileDrawbarScale>1.5||x.IntervalS<0||x.IntervalS>25
                ||!new[]{"straight","circle","s","entry"}.Contains(x.Shape)||!new[]{"uniform","patches","rolling","reversal","draft-reversal","draft-patches"}.Contains(x.Terrain)||!new[]{"dual","imu","position"}.Contains(s.Heading)||(x.Shape is "circle" or "entry")&&Math.Abs(x.RadiusM)<15)
                throw new ArgumentException("Invalid physical/sensor parameters in "+x.Name);
        }
    }
    internal static List<Vec3> ReferencePath(DraftCase c)
    {
        if(c.Shape=="straight")return new(){new(0,-1000,0),new(0,2000,0)};
        if(c.Shape=="s"){var p=new List<Vec3>{new(0,-1000,0)};for(int i=0;i<=1200;i++){double n=i*.5,k=2*Math.PI/100;p.Add(new(6*(1-Math.Cos(k*n)),n,Math.Atan2(6*k*Math.Sin(k*n),1)));}return p;}
        double r=Math.Abs(c.RadiusM),sign=Math.Sign(c.RadiusM);var points=new List<Vec3>();
        if(c.Shape=="entry"){points.Add(new(0,-1000,0));points.Add(new(0,0,0));for(int i=1;i<=180;i++){double a=i*Math.PI/360;points.Add(new(sign*r*(1-Math.Cos(a)),r*Math.Sin(a),sign*a));}points.Add(new(sign*(r+1500),r,sign*Math.PI/2));}
        else for(int i=0;i<720;i++){double a=i*Math.PI*2/720;points.Add(new(sign*r*(1-Math.Cos(a)),r*Math.Sin(a),sign*a));}
        return points;
    }
    private static double Error(DraftCase c,List<Vec3> p,double e,double n)
    {
        if(c.Shape=="straight")return e;
        if(c.Shape=="circle")return Simulation.Error(c.RadiusM,e,n);
        double best=double.PositiveInfinity,result=0;
        for(int i=1;i<p.Count;i++){double x=p[i].Easting-p[i-1].Easting,y=p[i].Northing-p[i-1].Northing,l=x*x+y*y;if(l==0)continue;
            double u=Math.Clamp(((e-p[i-1].Easting)*x+(n-p[i-1].Northing)*y)/l,0,1),dx=e-p[i-1].Easting-u*x,dn=n-p[i-1].Northing-u*y,d=dx*dx+dn*dn;
            if(d<best){best=d;result=Math.CopySign(Math.Sqrt(d),dx*y-dn*x);}}
        return result;
    }
    private static double Deg(double r)=>(r*180/Math.PI%360+360)%360;
    public static DraftRun Drive(DraftCase c,int seed,bool enabled,double duration,double physicsDt)
    {
        var p=c.Plant!;var sensor=c.Sensors!;var random=new Random(seed*17041);double Noise()=>Math.Sqrt(-2*Math.Log(Math.Max(1e-12,random.NextDouble())))*Math.Cos(2*Math.PI*random.NextDouble());
        var plant=new DraftPlant(p,c.SpeedKph/3.6,c.Shape=="entry"?-40:0){E=c.InitialErrorM};var points=ReferencePath(c);
        var track=c.Shape=="straight"?Track.FromABLine(c.Name,points[0],points[1]):Track.FromCurve(c.Name,points,c.Shape=="circle");
        // A private copy prevents a sweep case changing another case's settings.
        var settings=c.Controller is null
            ?new AgOpenWeb.Models.Configuration.PassiveGuidanceConfig{UseGeometry=c.Geometry,Interval=c.IntervalS,StraightLimit=c.OffsetLimitM,CurveLimit=c.OffsetLimitM}
            :JsonSerializer.Deserialize<AgOpenWeb.Models.Configuration.PassiveGuidanceConfig>(JsonSerializer.Serialize(c.Controller,Json),Json)!;
        settings.Enabled=enabled;settings.AntennaToWorkingPoint=p.WorkingBarM;settings.AntennaHeight=2;
        double activeLimit=AgOpenWeb.Services.Passive.CPassiveCurve.Limit(settings.StraightLimit,settings.CurveLimit,c.Shape=="straight"?0:1/Math.Abs(c.RadiusM));
        double boundLimit=Math.Max(settings.StraightLimit,settings.CurveLimit);
        var service=new TrackGuidanceService();TrackGuidanceState? prior=null;var history=new List<DraftPose>();var samples=new List<DraftSample>();bool safe=true;string problem="";
        ImplementGpsFix? fix=null;double measuredE=0,measuredN=0,fixNext=0,distance=0;
        DraftPose Delayed(double time){int i=Math.Clamp((int)Math.Floor(time/physicsDt),0,history.Count-1);return history[i];}
        for(int step=0;step<(int)Math.Round(duration/.1);step++)
        {
            double t=step*.1;var truePose=plant.Pose(t);if(history.Count==0)history.Add(truePose);
            var tractor=Delayed(t-sensor.TractorDelayS);var now=Program.Epoch.AddSeconds(t);bool outage=sensor.Dropout&&t>=90&&t<94;
            if(t+1e-8>=fixNext&&!outage)
            {
                var obs=Delayed(t-sensor.ImplementDelayS);double slowBias=sensor.SlowBiasM*Math.Sin(t/17),roll=obs.RollDegrees;
                // Receiver sits over axle, 2 m high. The bar center is behind the axle.
                measuredE=obs.ToolE+p.WorkingBarM*Math.Sin(obs.Beta)+2*Math.Sin(roll*Math.PI/180)*Math.Cos(obs.Beta)+sensor.PositionSigmaM*Noise()+slowBias;
                measuredN=obs.ToolN+p.WorkingBarM*Math.Cos(obs.Beta)-2*Math.Sin(roll*Math.PI/180)*Math.Sin(obs.Beta)+sensor.PositionSigmaM*Noise();
                double body=Deg(obs.Beta)+sensor.HeadingSigmaDegrees*Noise(),course=Deg(Math.Atan2(obs.ToolVE,obs.ToolVN));
                fix=new(now,49,-98,4,Math.Sqrt(obs.ToolVE*obs.ToolVE+obs.ToolVN*obs.ToolVN),sensor.Heading=="dual"?(body+360)%360:double.NaN,sensor.Heading=="position"?double.NaN:course,
                    sensor.Heading=="imu"?(body+23+t*sensor.ImuDriftDegPerSec+360)%360:double.NaN,Program.Epoch.AddSeconds(obs.T),sensor.Heading=="position"?double.NaN:roll+sensor.RollSigmaDegrees*Noise());
                fixNext=t+1/sensor.ImplementHz;
            }
            double tractorE=tractor.E+sensor.PositionSigmaM*.5*Noise(),tractorN=tractor.N+sensor.PositionSigmaM*.5*Noise();
            var input=new TrackGuidanceInput{Track=track,PivotPosition=new Vec3(tractorE,tractorN,tractor.H),
                SteerPosition=new Vec3(tractorE+3*Math.Sin(tractor.H),tractorN+3*Math.Cos(tractor.H),tractor.H),FixHeading=tractor.H,Wheelbase=3,MaxSteerAngle=35,GoalPointDistance=Math.Max(4,plant.Speed*2),AvgSpeed=plant.Speed*3.6,IsAutoSteerOn=true,IsHeadingSameWay=true,PreviousState=prior,
                Passive=new(){Settings=settings,Now=now,Requested=true,Fix=fix,HasLocalPlane=true,ToolEast=measuredE,ToolNorth=measuredN,Context="draft-bench",Trailing=true,Hitch=p.HitchM,Drawbar=p.DrawbarM*c.ProfileDrawbarScale,WorkingBar=p.WorkingBarM,Width=p.WidthM}};
            var result=service.CalculateGuidance(input);prior=result.State;
            double error=Error(c,points,truePose.ToolE,truePose.ToolN);
            if(!new[]{plant.E,plant.N,plant.Beta,plant.BetaRate,error,result.SteerAngle,result.Passive.Offset,plant.SoilForceN}.All(double.IsFinite)||Math.Abs(result.Passive.Offset)>boundLimit+1e-8||Math.Abs(result.SteerAngle)>35.00001||plant.Saturation>1.00001||plant.MaxConstraintError>1e-8||Math.Abs(Math.Atan2(Math.Sin(plant.Beta-plant.H),Math.Cos(plant.Beta-plant.H)))>70*Math.PI/180)
            {safe=false;problem="Non-finite/bounds/constraint violation at "+t.ToString("F1",CultureInfo.InvariantCulture)+" s";break;}
            if(enabled&&outage&&t>91.6&&(result.Passive.Engaged||result.Passive.Offset!=0)){safe=false;problem="Stale implement GPS did not disengage";break;}
            samples.Add(new(t,plant.E,plant.N,plant.H,plant.Beta,truePose.ToolE,truePose.ToolN,error,result.Passive.Offset,result.SteerAngle,
                DraftPlant.Environment(c,t,distance).SlopeDegrees,plant.SoilForceN,plant.DraftForceN,plant.GravityForceN,plant.Saturation,plant.Speed,result.Passive.Engaged,result.Passive.Reason,
                result.Passive.ToolValid?result.Passive.ToolEast:0,result.Passive.ToolValid?result.Passive.ToolNorth:0,truePose.HitchE,truePose.HitchN));
            int sub=(int)Math.Round(.1/physicsDt);
            for(int k=0;k<sub;k++){double time=t+k*physicsDt;var env=DraftPlant.Environment(c,time,distance);double target=c.SpeedKph/3.6*(c.Terrain=="patches"?1-.2*(env.DraftFactor-1)/.6:1);
                plant.Step(physicsDt,result.SteerAngle,target,env);distance+=plant.Speed*physicsDt;history.Add(plant.Pose(time+physicsDt));}
        }
        var eval=samples.Where(s=>c.Shape=="entry"?s.T>=25&&s.T<=70:s.T>=30).ToArray();var errors=eval.Select(s=>Math.Abs(s.Error)).OrderBy(x=>x).ToArray();
        if(samples.Count!=(int)Math.Round(duration/.1)){safe=false;if(problem.Length==0)problem="Incomplete run";}
        double mean=eval.Length>0?eval.Average(x=>x.Error):0;
        double sd=eval.Length>0?Math.Sqrt(eval.Average(x=>(x.Error-mean)*(x.Error-mean))):0;
        int crossings=0,side=0;foreach(var point in eval)if(Math.Abs(point.Error)>.05){int next=Math.Sign(point.Error);if(side!=0&&side!=next)crossings++;side=next;}
        return new(c.Name,seed,enabled,c,safe,problem,errors.Length>0?Math.Sqrt(errors.Average(x=>x*x)):999,
            errors.Length>0?errors[(int)Math.Floor(.95*(errors.Length-1))]:999,errors.Length>0?errors[^1]:999,mean,sd,eval.Length>0?crossings*60.0/(eval.Length*.1):0,
            eval.Length>0?100.0*eval.Count(x=>Math.Abs(x.Offset)>=activeLimit-.01)/eval.Length:0,
            eval.Length>0?100.0*eval.Count(x=>x.GripUsage>.95)/eval.Length:0,eval.Length>0?100.0*eval.Count(x=>x.Engaged)/eval.Length:0,samples);
    }
    private static List<Check> ModelChecks()
    {
        var checks=new List<Check>();var p=new DraftPlantParameters(SlopeDegrees:0,Asymmetry:0,HitchPlayM:0,TractorCorneringStiffnessNPerRad:1e15,TractorYawDampingNms:1e15);
        var zero=new DraftPlant(p,2);for(int i=0;i<10000;i++)zero.Step(.01,0,2,new(0,1,1,0));
        checks.Add(new("Plant / zero-force straight invariance",Math.Abs(zero.Pose(0).ToolE)<1e-10&&Math.Abs(zero.BetaRate)<1e-10,"No lateral drift is manufactured without slope/asymmetric force."));
        var loaded=p with {Asymmetry=800.0/30000};double limit=loaded.Friction*loaded.MassKg*9.80665;
        double effective=800*(loaded.DrawbarM+loaded.WorkingBarM)/loaded.DrawbarM;
        double angle=limit/loaded.CorneringStiffnessNPerRad*Math.Atanh(effective/limit),expected=(loaded.DrawbarM+loaded.WorkingBarM)*Math.Sin(angle);
        var pos=new DraftPlant(loaded,2);var neg=new DraftPlant(loaded with {Asymmetry=-loaded.Asymmetry},2);
        for(int i=0;i<15000;i++){pos.Step(.01,0,2,new(0,1,1,1));neg.Step(.01,0,2,new(0,1,1,1));}
        checks.Add(new("Plant / analytic steady side-force balance",Math.Abs(pos.Pose(0).ToolE-expected)<.01,
            FormattableString.Invariant($"Predicted {expected:F4} m versus integrated {pos.Pose(0).ToolE:F4} m. Rigid-tractor fixture; independent force-balance oracle.")));
        checks.Add(new("Plant / left-right force symmetry",Math.Abs(pos.Pose(0).ToolE+neg.Pose(0).ToolE)<1e-6,"Equal opposite forces must produce equal opposite offsets."));
        var damp=new DraftPlant(p,0){BetaRate=.1};double initial=.1;for(int i=0;i<1000;i++)damp.Step(.01,0,0,new(0,1,1,0));
        checks.Add(new("Plant / lateral damping dissipates yaw motion",Math.Abs(damp.BetaRate)<initial*.01,"A stationary free-yaw fixture must lose yaw kinetic energy under soil drag."));
        return checks;
    }
}
