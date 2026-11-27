// GNU GPL v3. Executes production browser functions with a deterministic clock and fake canvas.
const fs=require('node:fs'),vm=require('node:vm'),assert=require('node:assert/strict'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../../Shared/AgOpenWeb.RemoteServer/wwwroot/app.js'),'utf8');
const render=source.slice(source.indexOf('function drawCoverageSk(canvas)'),source.indexOf('// Crisp worked-area edge'));
for(const gpu of [true,false]){
 let time=10000,deleted=0;const painted=[];const snapshot=()=>({delete(){deleted++}});
 const context={performance:{now:()=>time++},RENDER_DELAY:0,COV_DRAIN_BUDGET:40000,COV_DRAIN_MS:6,COV_REBUILD_MS:250,covCells:0,covEdgeRgb:0,
   CK:{Color:(...a)=>a,XYWHRect:(...a)=>a,FilterMode:{Linear:1},MipmapMode:{None:0}},drawImageWorldSk:()=>{}};
 const cells=Int32Array.from(Array.from({length:2000},(_,i)=>[i%50,Math.floor(i/50),0xff123456]).flat());
 const paint=(x,y)=>{if(gpu)assert.equal(context.cov.skImg,null,'Snapshot must be released before modifying the render target');painted.push(x+50*(49-y))};
 context.CK.MakeImageFromCanvasImageSource=snapshot;
 context.cov={surface:gpu?{getCanvas:()=>({drawRect:r=>paint(r[0],r[1])}),flush(){},makeImageSnapshot:snapshot}:null,
   covPaint:{setColor(){}},canvas:{},cctx:{clearRect(){},fillRect:paint},width:50,height:50,originE:0,originN:0,cellSize:.1,
   pending:[{cells,t:0}],dirty:true,skImg:snapshot(),lastBuild:0};
 vm.createContext(context);vm.runInContext(render,context);
 let frames=0;while(context.cov.pending.length&&frames++<100){const before=painted.length;context.drawCoverageSk({});assert.ok(painted.length-before<2000,'A time-limited slice must yield before draining this batch');time+=20}
 assert.equal(context.cov.pending.length,0);assert.equal(context.covCells,2000);assert.deepEqual(painted,Array.from({length:2000},(_,i)=>i));
 if(gpu)assert.ok(deleted>1,'Updated snapshots must be released between slices');
 console.log(`PASS ${gpu?'GPU':'2D fallback'}: all 2,000 coverage cells drawn once, in order, over ${frames} bounded slices`);
}
const start=source.indexOf('function pulseControlPresence('),end=source.indexOf('setInterval(pulseControlPresence',start);
const sent=[];const c={performance:{now:()=>0},lastPresenceSent:-Infinity,iHoldControl:true,transport:{send:x=>sent.push(x)}};
vm.createContext(c);vm.runInContext(source.slice(start,end),c);
for(const t of [0,100,249,250,300,500])c.pulseControlPresence(t);
assert.equal(sent.length,3);c.iHoldControl=false;c.pulseControlPresence(750);assert.equal(sent.length,3);
c.iHoldControl=true;c.pulseControlPresence(751);assert.equal(sent.length,4);
console.log('PASS UI-thread presence: frame/timer callbacks share a 250 ms rate limit; observers send no pulses');
