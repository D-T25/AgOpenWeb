// GNU GPL v3. Self-contained report; no remote scripts, no telemetry upload.
using System.Text.Json;
namespace AgOpenWeb.PassiveHarness;
public static class ReportWriter
{
    public static void Write(string path,Report report)
    {
        var json=JsonSerializer.Serialize(report,new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
        File.WriteAllText(path,"""
<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>AgOpenWeb passive harness</title><style>
body{font:16px system-ui,sans-serif;background:#f3f6f8;color:#173042;max-width:1200px;margin:28px auto;padding:0 20px}h1{font-size:28px}section{background:white;border:1px solid #d7e0e5;border-radius:12px;padding:20px;margin:18px 0}table{width:100%;border-collapse:collapse;font-size:14px}th,td{padding:10px;text-align:left;border-bottom:1px solid #dce5ea}.pass{color:#16743e}.fail{color:#ae2828}select{font:inherit;padding:8px;max-width:100%}svg{width:100%;height:auto}small{color:#526774}button{padding:8px;font:inherit}#summary{font-size:22px;font-weight:650}.scroll{overflow:auto}p{line-height:1.5}
</style><h1>AgOpenWeb passive guidance harness</h1><p id="summary"></p><p id="model"></p>
<section><h2>Tracking comparisons</h2><p>RMS uses seconds 100–150, except straight–arc–straight entries which use seconds 25–70 to include the turn. Acceptance limits were declared in the scenarios before running: ≤20 cm on straight lines or ≤30 cm on curves, plus ≥30% improvement over passive off. Stress cases check bounds and expose saturation; they do not count as field accuracy acceptance.</p><div class="scroll"><table id="metrics"><thead><tr><th>Scenario</th><th>Off RMS</th><th>On RMS</th><th>Change</th><th>Engaged</th></tr></thead><tbody></tbody></table></div></section>
<section><h2>Inspect a run</h2><select id="scenario"></select><p id="setup"></p><p>Blue: passive on · Grey: passive off · Green: applied tractor offset. Click chart to read a sample.</p><svg id="plot" viewBox="0 0 1000 330" role="img" aria-label="Tool error over time"></svg><p id="sample"></p><svg id="map" viewBox="0 0 1000 430" role="img" aria-label="Tractor and implement paths"></svg><p>Blue: implement · Orange: tractor. Independent model positions are plotted in metres.</p></section>
<section><h2>Check details</h2><div class="scroll"><table id="checks"><thead><tr><th>Result</th><th>Check</th><th>Evidence</th></tr></thead><tbody></tbody></table></div></section>
<p><small>Reproducible seed 7411. CSV and JSON are included beside this report. This is an offline development bench, not calibration or a field validation certificate.</small></p>
<script>const report=
"""+json+"""
;
const $=s=>document.querySelector(s),fmt=v=>(v*100).toFixed(1)+' cm',names=[...new Set(report.runs.map(r=>r.name))];
const failed=report.checks.filter(c=>!c.pass).length;$('#summary').textContent=`${report.checks.length-failed}/${report.checks.length} checks passed · ${failed} failed`;$('#summary').className=failed?'fail':'pass';$('#model').textContent=report.model;
for(const n of names){const [a,b]=report.runs.filter(r=>r.name===n);const tr=document.createElement('tr');for(const x of [n,fmt(a.rms),fmt(b.rms),((1-b.rms/Math.max(a.rms,1e-9))*100).toFixed(0)+'%',b.engagedPercent.toFixed(0)+'%']){const td=document.createElement('td');td.textContent=x;tr.append(td)}$('#metrics tbody').append(tr);const o=new Option(n,n);$('#scenario').add(o)}
for(const c of report.checks){const tr=document.createElement('tr');for(const x of [c.pass?'PASS':'FAIL',c.name,c.detail]){const td=document.createElement('td');td.textContent=x;tr.append(td)}tr.firstChild.className=c.pass?'pass':'fail';$('#checks tbody').append(tr)}
const el=(tag,attrs,text)=>{const n=document.createElementNS('http://www.w3.org/2000/svg',tag);for(const [k,v]of Object.entries(attrs))n.setAttribute(k,v);if(text!==undefined)n.textContent=text;return n};
function draw(){const [off,on]=report.runs.filter(r=>r.name===$('#scenario').value),p=$('#plot');$('#setup').textContent='Settings: '+JSON.stringify(on.setup);p.replaceChildren();const all=[...off.samples,...on.samples],max=Math.max(.25,...all.map(s=>Math.abs(s.error)),...on.samples.map(s=>Math.abs(s.offset)));const x=t=>50+t/150*920,y=v=>155-v/max*125;
for(const v of [-max,0,max]){p.append(el('line',{x1:50,y1:y(v),x2:970,y2:y(v),stroke:'#d7e0e5'}));p.append(el('text',{x:2,y:y(v)+5,'font-size':12},v.toFixed(2)+' m'))}for(const t of [0,30,60,90,120,150])p.append(el('text',{x:x(t)-8,y:310,'font-size':12},t+' s'));
for(const [run,key,color]of [[off,'error','#9baab5'],[on,'error','#1478bc'],[on,'offset','#218352']])p.append(el('polyline',{points:run.samples.map(s=>x(s.t)+','+y(s[key])).join(' '),fill:'none',stroke:color,'stroke-width':2}));
p.onclick=e=>{const t=Math.max(0,Math.min(920,(e.clientX-p.getBoundingClientRect().left)/p.getBoundingClientRect().width*1000-50))/920*150;const s=on.samples.reduce((a,b)=>Math.abs(b.t-t)<Math.abs(a.t-t)?b:a);$('#sample').textContent=`${s.t.toFixed(1)} s · Tool error ${fmt(s.error)} · Offset ${fmt(s.offset)} · ${s.engaged?'Engaged':'Disengaged'} · ${s.reason}`};
const m=$('#map');m.replaceChildren();const xs=on.samples.flatMap(s=>[s.e,s.toolE]),ys=on.samples.flatMap(s=>[s.n,s.toolN]);const minx=Math.min(...xs),maxx=Math.max(...xs),miny=Math.min(...ys),maxy=Math.max(...ys),scale=Math.min(920/Math.max(1,maxx-minx),360/Math.max(1,maxy-miny));const mx=e=>500+(e-(minx+maxx)/2)*scale,my=n=>215-(n-(miny+maxy)/2)*scale;
for(const [xe,yn,color]of [['e','n','#d77a20'],['toolE','toolN','#1478bc']])m.append(el('polyline',{points:on.samples.map(s=>mx(s[xe])+','+my(s[yn])).join(' '),stroke:color,'stroke-width':2,fill:'none'}));}
$('#scenario').onchange=draw;draw();</script></html>
""");
    }
}
