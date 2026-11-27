// GNU GPL v3. One-control-at-a-time field adjustment experiment. No production tuning changes.
using System.Globalization;
using System.Text.Json;
using AgOpenWeb.Models.Configuration;
namespace AgOpenWeb.PassiveHarness;

public static class SettingSweepSuite
{
    public record Variant(string Control,string Value,PassiveGuidanceConfig Settings);
    public record Trace(double T,double Error,double Offset);
    public record Result(string Scenario,string Control,string Value,int Seed,bool Passive,bool Safe,string Problem,
        double RmsM,double BiasM,double WeaveM,double P95M,double PeakM,double CrossingsPerMinute,double CapPercent,
        double EngagementPercent,double? SettleSeconds,double LateRmsM,double SteerRmsDegrees,double SteerTravelDegrees,
        Dictionary<string,int> Reasons,List<Trace> Trace);
    public record Finding(string Scenario,string Control,string Value,double RmsM,double WeaveM,double BiasM,
        double BaselineRmsM,double RmsChangePercent,double CapPercent,int SafeRuns,int Runs);
    private static PassiveGuidanceConfig Copy(PassiveGuidanceConfig c)=>JsonSerializer.Deserialize<PassiveGuidanceConfig>(JsonSerializer.Serialize(c))!;
    public static List<Variant> Variants()
    {
        var baseline=new PassiveGuidanceConfig{UseGeometry=true};
        var list=new List<Variant>{new("Baseline","default controls; geometry on",baseline)};
        void Levels(string name,Action<PassiveGuidanceConfig,double> set,params double[] values)
        {foreach(double value in values){var c=Copy(baseline);set(c,value);list.Add(new(name,value.ToString(CultureInfo.InvariantCulture),c));}}
        Levels("Tracking Sensitivity",(c,v)=>c.Tracking=v,50,75,150,200);
        Levels("Motion Sensitivity",(c,v)=>c.Heading=v,50,75,150,200);
        Levels("Acquire Sensitivity",(c,v)=>c.Acquire=v,50,150,200);
        Levels("Correction Strength",(c,v)=>c.Strength=v,50,150,200);
        Levels("Correction Interval (s)",(c,v)=>c.Interval=v,0,2,6,10);
        Levels("Tool Look-ahead (s)",(c,v)=>c.LookAhead=v,.5,1,2);
        Levels("Straight Maximum Offset (m)",(c,v)=>c.StraightLimit=v,.5,2,3);
        Levels("Curve Maximum Offset (m)",(c,v)=>c.CurveLimit=v,.5,2,3);
        Levels("Curve Response (%)",(c,v)=>c.CurveResponse=v,0,50,150,200);
        var retry=Copy(baseline);retry.EarlyRetry=false;list.Add(new("Retry early","off",retry));
        var geometry=Copy(baseline);geometry.UseGeometry=false;list.Add(new("Geometry compensation","off",geometry));
        return list;
    }
    public static List<DraftCase> Scenarios()
    {
        var p=new DraftPlantParameters(SlopeDegrees:0);
        return new(){
            new("Flat straight / no draft",Plant:p with{Asymmetry=0},Sensors:new()),
            new("Flat straight / steady draft",Plant:p,Sensors:new()),
            new("Flat straight / draft changes sides",Terrain:"draft-reversal",Plant:p,Sensors:new()),
            new("Flat straight / alternating draft",Terrain:"draft-patches",Plant:p,Sensors:new()),
            new("Flat straight / engage 1 m off line",Plant:p,Sensors:new(),InitialErrorM:1),
            new("Flat right curve / 40 m radius","circle",40,Plant:p,Sensors:new()),
            new("Flat left curve / 40 m radius","circle",-40,Plant:p,Sensors:new())};
    }
    public static int Run(string[] args)
    {
        string Arg(string key,string fallback){int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:fallback;}
        try
        {
            string output=Arg("--output","setting-sweep-results");
            int seeds=int.Parse(Arg("--seeds","3"),CultureInfo.InvariantCulture);
            double duration=double.Parse(Arg("--duration","180"),CultureInfo.InvariantCulture);
            if(seeds<1||seeds>20||duration<120||duration>600)throw new ArgumentException("Use 1–20 seeds and 120–600 seconds.");
            var variants=Variants();var scenarios=Scenarios();var results=new List<Result>();var checks=new List<Check>();
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output,"settings.json"),JsonSerializer.Serialize(new{Seeds=seeds,DurationS=duration,PhysicsStepS=.01,Scenarios=scenarios,Variants=variants},DraftSuite.Json));
            // Check the experiment itself: every variant differs from baseline in exactly one property.
            var original=JsonSerializer.SerializeToElement(variants[0].Settings);
            checks.Add(new("One control at a time",variants.Skip(1).All(v=>{
                var candidate=JsonSerializer.SerializeToElement(v.Settings);
                return original.EnumerateObject().Count(p=>p.Value.GetRawText()!=candidate.GetProperty(p.Name).GetRawText())==1;
            }),"Every variant differs from the common baseline in exactly one controller property."));
            foreach(var scenario in scenarios)
            {
                for(int seed=1;seed<=seeds;seed++)
                {
                    // Same physical plant and random sequence for every setting, including passive OFF.
                    var rng=new Random(seed*7919);var p=scenario.Plant!;
                    var physical=scenario with{Plant=p with{MassKg=p.MassKg*(.9+.2*rng.NextDouble()),CorneringStiffnessNPerRad=p.CorneringStiffnessNPerRad*(.8+.4*rng.NextDouble())}};
                    var off=DraftSuite.Drive(physical with{Controller=variants[0].Settings},seed,false,duration,.01);
                    results.Add(Summarize(off,"Passive OFF","reference",seed));
                    foreach(var variant in variants)
                    {
                        var run=DraftSuite.Drive(physical with{Controller=variant.Settings},seed,true,duration,.01);
                        results.Add(Summarize(run,variant.Control,variant.Value,seed));
                    }
                }
                // Convergence checked per path/disturbance, rather than only a generic hill case.
                var c=scenario with{Controller=variants[0].Settings};
                var coarse=DraftSuite.Drive(c,7411,true,duration,.01);var fine=DraftSuite.Drive(c,7411,true,duration,.005);
                checks.Add(new(scenario.Name+" / timestep convergence",coarse.Safe&&fine.Safe&&Math.Abs(coarse.RmsM-fine.RmsM)<.02,
                    FormattableString.Invariant($"10 ms {coarse.RmsM:F4} m; 5 ms {fine.RmsM:F4} m; tolerance 0.02 m.")));
                Console.WriteLine($"SWEEP {scenario.Name}: {variants.Count} settings × {seeds} seeds plus passive OFF complete.");
            }
            checks.Add(new("All trajectories finite, bounded and complete",results.All(r=>r.Safe),$"{results.Count(r=>r.Safe)}/{results.Count} trajectories within the existing plant domain."));
            // Negative controls on exact straights. Curve limits blend with straight limits on gentle bends.
            bool Same(Result a,Result b)=>a.RmsM==b.RmsM&&a.BiasM==b.BiasM&&a.SteerTravelDegrees==b.SteerTravelDegrees;
            checks.Add(new("Curve-only controls leave exact straights unchanged",results
                .Where(r=>r.Scenario.Contains("straight")&&(r.Control is "Curve Maximum Offset (m)" or "Curve Response (%)" or "Geometry compensation"))
                .All(r=>Same(r,results.Single(b=>b.Scenario==r.Scenario&&b.Seed==r.Seed&&b.Control=="Baseline"))),"Curve limit, curve response and geometry toggle must leave an exactly straight path identical."));
            var findings=results.GroupBy(r=>new{r.Scenario,r.Control,r.Value}).Select(g=>{
                var b=results.Where(r=>r.Scenario==g.Key.Scenario&&r.Control=="Baseline").Average(r=>r.RmsM);var rms=g.Average(r=>r.RmsM);
                return new Finding(g.Key.Scenario,g.Key.Control,g.Key.Value,rms,g.Average(r=>r.WeaveM),g.Average(r=>r.BiasM),b,100*(rms-b)/Math.Max(1e-9,b),g.Average(r=>r.CapPercent),g.Count(r=>r.Safe),g.Count());}).ToList();
            File.WriteAllText(Path.Combine(output,"results.json"),JsonSerializer.Serialize(new{CreatedUtc=DateTime.UtcNow,Checks=checks,Findings=findings,Runs=results},DraftSuite.Json));
            using(var csv=new StreamWriter(Path.Combine(output,"metrics.csv")))
            {
                csv.WriteLine("scenario,control,value,seed,passive,domain_ok,rms_m,bias_m,weave_sd_m,p95_m,peak_m,crossings_min,cap_percent,engagement_percent,settle_s,late_rms_m,steer_rms_deg,steer_travel_deg");
                foreach(var r in results)csv.WriteLine(string.Join(",",new[]{Q(r.Scenario),Q(r.Control),Q(r.Value),r.Seed.ToString(CultureInfo.InvariantCulture),r.Passive.ToString(),r.Safe.ToString()}
                    .Concat(new double?[]{r.RmsM,r.BiasM,r.WeaveM,r.P95M,r.PeakM,r.CrossingsPerMinute,r.CapPercent,r.EngagementPercent,r.SettleSeconds,r.LateRmsM,r.SteerRmsDegrees,r.SteerTravelDegrees}.Select(v=>v?.ToString("G17",CultureInfo.InvariantCulture)??""))));
            }
            WriteHtml(Path.Combine(output,"Field-Adjustment-Report.html"),findings,results,checks,seeds,duration);
            foreach(var check in checks)Console.WriteLine($"{(check.Pass?"PASS":"FAIL")} {check.Name}: {check.Detail}");
            Console.WriteLine($"SWEEP COMPLETE: {results.Count} trajectories; {checks.Count(x=>x.Pass)}/{checks.Count} experiment/domain checks passed. Tracking accuracy is reported separately, not asserted to pass.");
            return checks.All(x=>x.Pass)?0:1;
        }
        catch(Exception e){Console.Error.WriteLine("Setting sweep: "+e);return 2;}
    }
    private static string Q(string s)=>"\""+s.Replace("\"","\"\"")+"\"";
    private static Result Summarize(DraftRun r,string control,string value,int seed)
    {
        var eval=r.Samples.Where(s=>s.T>=30).ToArray();double? settle=null;
        // First 10 continuous seconds within ±10 cm following the known disturbance.
        double start=r.Setup.Terrain=="draft-reversal"?73:r.Setup.InitialErrorM!=0?0:25;
        int consecutive=0;foreach(var s in r.Samples.Where(s=>s.T>=start)){
            consecutive=Math.Abs(s.Error)<=.1?consecutive+1:0;
            if(consecutive>=100){settle=s.T-9.9-start;break;}}
        var late=r.Samples.Where(s=>s.T>=r.Samples[^1].T-30).ToArray();
        return new(r.Name,control,value,seed,r.Passive,r.Safe,r.Problem,r.RmsM,r.MeanErrorM,r.StdDevM,r.P95M,r.PeakM,
            r.CrossingsPerMinute,r.OffsetLimitedPercent,r.EngagedPercent,settle,Math.Sqrt(late.Average(x=>x.Error*x.Error)),
            Math.Sqrt(eval.Average(x=>x.Steer*x.Steer)),eval.Skip(1).Select((s,i)=>Math.Abs(s.Steer-eval[i].Steer)).Sum(),
            eval.GroupBy(s=>s.Reason).ToDictionary(g=>g.Key,g=>g.Count()),seed==1?r.Samples.Where((s,i)=>i%5==0).Select(s=>new Trace(s.T,s.Error,s.Offset)).ToList():new());
    }
    private static void WriteHtml(string path,List<Finding> findings,List<Result> runs,List<Check> checks,int seeds,double duration)
    {
        string json=JsonSerializer.Serialize(new{findings,runs,checks,seeds,duration},DraftSuite.Json);
        File.WriteAllText(path,"""
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>Passive guidance field adjustment report</title>
<style>body{font:16px system-ui;max-width:1200px;margin:auto;padding:18px;background:#10201b;color:#eef5f1}h1,h2{color:#b8ead0}p{max-width:1000px;line-height:1.5}select,button{font:inherit;padding:8px;max-width:100%;margin:5px 0}table{border-collapse:collapse;width:100%;font-size:14px}td,th{padding:9px;text-align:left;border-bottom:1px solid #486056}th{background:#244638;position:sticky;top:0}.scroll{overflow:auto}svg{background:#fff;width:100%;height:auto;color:#18261f}code{color:#b8ead0}.bad{color:#ffbead}.good{color:#abe8b3}.card{padding:15px;background:#1b342a;margin:16px 0;border-radius:8px}li{margin-bottom:8px}</style>
<h1>Passive guidance: one setting at a time</h1>
<p id="count"></p><p><b>These are simulated comparisons, not field-calibrated settings.</b> The plant is an illustrative 8 m wide, 2,500 kg drawbar implement with an 8 m hitch-to-axle distance and working bar 1.5 m behind the axle; speed 7.2 km/h. It is not a measured Great Plains, Monosem or Harriston model. Ground is level in every case. Default controls are 100, interval 4 s, look-ahead 0 s, both offset limits 1 m, retry early on. Geometry compensation is explicitly on for the baseline.</p>
<p>The actual production TrackGuidanceService runs at 10 Hz against an independent force-based trailer plant at 100 Hz, with noisy, delayed 5 Hz tool GNSS and dual heading. Matched seeds use identical mass, ground resistance and noise for every setting. Every variant changes exactly one property; combinations are not optimized here. Production software and saved user settings are unchanged.</p>
<div class="card"><h2>What these runs found</h2><div id="observations"></div></div>
<div class="card"><h2>Explore a setting</h2><select id="scenario"></select> <select id="control"></select><div id="table" class="scroll"></div><h3>Time trace: seed 1</h3><select id="variant"></select><p>Blue: selected setting. Grey: baseline. Top: true working-bar error (m). Bottom: commanded tractor offset (m). Traces shown every 0.5 s; metrics use every 0.1 s.</p><div id="plot"></div><p id="detail"></p></div>
<h2>What the numbers mean</h2><p>RMS measures total tracking error. Bias is the signed average error. Weave is standard deviation about that bias: a low RMS alone does not establish smooth steering. Crossings count complete movements across a ±5 cm band per minute. Cap is time at maximum offset. Negative RMS change is better than baseline. Results average all seeds; traces show seed 1 only. Peak and RMS in the table use 30 s onward, so inspect the engagement trace for initial overshoot.</p><p>Settle time in the raw metrics means the first uninterrupted 10 s within ±10 cm after engagement, after the draft ramp ends (25 s), or after reversal ends (73 s). Blank means not achieved. Alternating draft has no final settled state. Late RMS uses the last 30 s. Controller reasons and steering effort are included in results.json/metrics.csv. Domain checks are separate from accuracy: ≤20 cm straight / ≤30 cm curve RMS plus ≥30% improvement over passive OFF remains the draft harness tracking target.</p>
<h2>Field adjustment order</h2><ol><li>First get tractor-only autosteer smooth at working speed with passive off. Passive cannot correct tractor steering that already weaves.</li><li>Measure hitch-to-axle, axle-to-working-bar and antenna offsets; check that the displayed tool point matches the real rows. Mechanical play, wrong geometry, delayed fixes and antenna movement can resemble bad tuning.</li><li>Begin on a long flat straight at constant speed and working depth. Save the current profile. Change one control and repeat in both travel directions under similar soil/load.</li><li>For weaving, compare Motion Sensitivity, Tracking Sensitivity, Strength and Interval. Judge both tool error and steering activity. Do not raise all gains together. Interval is a minimum rhythm with exceptions: early retry and line-crossing release can act sooner.</li><li>For a persistent error, check whether offset is at its limit. A higher limit gives permission to move farther; it does not itself increase response. If the cap is seldom reached, investigate control behaviour instead.</li><li>Tune acquisition with a deliberate modest starting error, then recheck straight stability. Acquire Sensitivity mainly acts when error is large.</li><li>After straight rows are satisfactory, test left and right curves. Curve Response adjusts geometric compensation; Curve Maximum Offset caps available correction. Their effects depend on geometry, radius and draft direction. The effective offset limit blends from the straight limit at 200 m radius to the curve limit at 25 m radius; both limits therefore affect a 40 m curve.</li></ol>
<h2>Measurements are not gain adjustments</h2><p>Antenna height, lateral offset, antenna-to-working-point, heading mounting offset, roll zero and roll inversion describe installation. Enter measured values and verify sign/orientation. They are held fixed in this sweep, not varied to hide tracking error. Heading source, speed, hitch geometry and GNSS quality also need separate validation on your equipment.</p>
<h2>Experiment checks</h2><div id="checks"></div><p>The planar plant omits full tyre/soil mechanics, frame flex, suspension and row-unit forces. Coefficients are illustrative. Numerical convergence does not validate the model against a field. No U-turns or automatic calibration feature are added.</p>
<script>
const data=DATA;
const $=id=>document.getElementById(id), esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
$('count').textContent=`${data.runs.length} trajectories, ${data.seeds} matched seeds, ${data.duration} seconds each. Five flat straight scenarios and two curves.`;
const options=(el,values)=>el.innerHTML=[...new Set(values)].map(x=>`<option>${esc(x)}</option>`).join('');
options($('scenario'),data.findings.map(r=>r.scenario));options($('control'),data.findings.filter(r=>!['Baseline','Passive OFF'].includes(r.control)).map(r=>r.control));
const cm=x=>(100*x).toFixed(1), num=x=>x.toFixed(1);

const row=(part,control,value)=>data.findings.find(r=>r.scenario.includes(part)&&r.control===control&&(value===undefined||r.value===value));
const steady=row('steady draft','Baseline'), gentle=row('steady draft','Correction Strength','50'), continuous=row('steady draft','Correction Interval (s)','0');
const alternating=row('alternating draft','Baseline'), fast=row('alternating draft','Correction Interval (s)','0');
const left=row('left curve','Baseline'), leftOff=row('left curve','Passive OFF'), right=row('right curve','Baseline');
let overlap=0,pairs=0;for(const [motion,preview] of [['150','0.5'],['200','1']])for(const r of data.runs.filter(r=>r.control==='Motion Sensitivity'&&r.value===motion)){const q=data.runs.find(q=>q.scenario===r.scenario&&q.seed===r.seed&&q.control==='Tool Look-ahead (s)'&&q.value===preview);if(q){pairs++;overlap=Math.max(overlap,Math.abs(r.rmsM-q.rmsM),Math.abs(r.weaveM-q.weaveM));}}
$('observations').innerHTML=`<ul><li>Steady draft: reducing Strength from 100 to 50 changed RMS from <b>${cm(steady.rmsM)} to ${cm(gentle.rmsM)} cm</b> and error variation from ${cm(steady.weaveM)} to ${cm(gentle.weaveM)} cm. This value was tested alone, not combined with other changes.</li><li>Continuous corrections (interval 0) under steady draft changed RMS to ${cm(continuous.rmsM)} cm and error variation to ${cm(continuous.weaveM)} cm. Under alternating draft they changed RMS from ${cm(alternating.rmsM)} to ${cm(fast.rmsM)} cm. The best interval depends on the disturbance.</li><li>In these straight cases increasing the offset limit above 1 m gave no improvement: the limit was not the bottleneck. Do not increase it just because the tool remains off line.</li><li>Curves need more work: baseline right-curve RMS was ${cm(right.rmsM)} cm, but left-curve RMS was ${cm(left.rmsM)} cm versus ${cm(leftOff.rmsM)} cm with passive OFF. Both turns experience the same side-draft direction. Do not take one successful direction as validation of the other.</li><li>Motion and Look-ahead overlap: Motion 150 / preview 0 matches Motion 100 / preview 0.5 s; Motion 200 / preview 0 matches Motion 100 / preview 1 s. Across ${pairs} paired runs, maximum RMS/variation difference was ${overlap.toExponential(2)} m. Their formulas currently add the same rate-based compensation. Tune one at a time; these should be consolidated in a future UI change.</li></ul><p>These comparisons do not establish an inch-accuracy field setup. For flat steady draft, gentler corrections are a better next experiment than raising every gain. Changing side draft still needs different compromises. Installation measurements and real logs are needed before final settings.</p>`;

function update(){const scenario=$('scenario').value,control=$('control').value;const rows=data.findings.filter(r=>r.scenario===scenario&&['Baseline','Passive OFF',control].includes(r.control));
$('table').innerHTML='<table><thead><tr><th>Setting</th><th>RMS cm</th><th>Bias cm</th><th>Weave cm</th><th>RMS change</th><th>Cap %</th><th>Domain</th></tr></thead><tbody>'+rows.map(r=>`<tr><td>${esc(r.control)}: ${esc(r.value)}</td><td>${cm(r.rmsM)}</td><td>${cm(r.biasM)}</td><td>${cm(r.weaveM)}</td><td>${num(r.rmsChangePercent)}%</td><td>${num(r.capPercent)}</td><td>${r.safeRuns}/${r.runs}</td></tr>`).join('')+'</tbody></table>';
const traces=data.runs.filter(r=>r.scenario===scenario&&r.seed===1&&['Passive OFF',control].includes(r.control));options($('variant'),traces.map(r=>r.control+': '+r.value));plot();}
function plot(){const scenario=$('scenario').value;const selected=data.runs.find(r=>r.scenario===scenario&&r.seed===1&&r.control+': '+r.value===$('variant').value);const baseline=data.runs.find(r=>r.scenario===scenario&&r.seed===1&&r.control==='Baseline');if(!selected)return;
function chart(key,label){const all=[...selected.trace,...baseline.trace];const lim=Math.max(.1,...all.map(p=>Math.abs(p[key])))*1.1;const W=1000,H=220,left=65,right=980,top=24,bottom=184;const x=t=>left+t/data.duration*(right-left),y=v=>(top+bottom)/2-v/lim*(bottom-top)/2;const line=(run,color)=>`<polyline fill="none" stroke="${color}" stroke-width="2" points="${run.trace.map(p=>x(p.t).toFixed(2)+','+y(p[key]).toFixed(2)).join(' ')}"/>`;
return `<svg viewBox="0 0 ${W} ${H}" role="img" aria-label="${label}"><text x="65" y="17">${label}</text>${[-1,-.5,0,.5,1].map(q=>`<line x1="${left}" x2="${right}" y1="${y(q*lim)}" y2="${y(q*lim)}" stroke="#ddd"/><text x="3" y="${y(q*lim)+4}" font-size="12">${(q*lim).toFixed(2)}</text>`).join('')}${[0,30,60,90,120,150,180,300,600].filter(t=>t<=data.duration).map(t=>`<text x="${x(t)}" y="208" font-size="12">${t}s</text>`).join('')}${line(baseline,'#888')}${line(selected,'#006dcc')}</svg>`;}
$('plot').innerHTML=chart('error','Working-bar error (m)')+chart('offset','Tractor offset (m)');$('detail').textContent=`Selected seed: peak after 30 s ${cm(selected.peakM)} cm; crossings ${num(selected.crossingsPerMinute)}/min; settle ${selected.settleSeconds===null?'not achieved':num(selected.settleSeconds)+' s'}; ${selected.safe?'full duration within plant domain':selected.problem}.`;}
$('scenario').onchange=update;$('control').onchange=update;$('variant').onchange=plot;$('checks').innerHTML=data.checks.map(c=>`<p class="${c.pass?'good':'bad'}">${c.pass?'PASS':'FAIL'} ${esc(c.name)}: ${esc(c.detail)}</p>`).join('');update();
</script></html>
""".Replace("DATA",json));
    }
}
