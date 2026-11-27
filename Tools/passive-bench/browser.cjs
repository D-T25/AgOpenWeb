// GNU GPL v3. Start an isolated local backend; never attach to an existing operator session.
const { chromium } = require('playwright');
const { spawn } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');
const os = require('node:os');
const dgram = require('node:dgram');
const options = Object.fromEntries(process.argv.slice(2).reduce((a,v,i,all)=>{if(v.startsWith('--'))a.push([v.slice(2),all[i+1]]);return a},[]));
if(!options.head){console.error('Usage: node browser.cjs --head <AgOpenWeb.Desktop.exe or .dll> [--dotnet <dotnet>] [--output <folder>] [--browser <chromium>]');process.exit(2)}
const output=path.resolve(options.output||'passive-browser-results');fs.mkdirSync(output,{recursive:true});
const checks=[],errors=[];let host,browser,udp,operator;let log;
const data=fs.mkdtempSync(path.join(os.tmpdir(),'agopenweb-passive-bench-'));
async function check(name,test){try{const detail=await test();checks.push({name,pass:true,detail:detail||'Observed in live host telemetry.'});console.log('PASS',name)}catch(e){checks.push({name,pass:false,detail:e.message});process.exitCode=1;if(name==='Section controls are independent')return false;throw e}}
const delay=ms=>new Promise(r=>setTimeout(r,ms));
async function portOpen(){try{await fetch('http://127.0.0.1:5174',{signal:AbortSignal.timeout(1000)});return true}catch{return false}}
(async()=>{
 if(fs.existsSync(path.join(output,'browser-results.json'))){const archive=path.join(output,'history',new Date().toISOString().replaceAll(':','-'));fs.mkdirSync(archive,{recursive:true});for(const f of ['browser-results.json','browser-report.html','host.log','transitions.json'])if(fs.existsSync(path.join(output,f)))fs.renameSync(path.join(output,f),path.join(archive,f))}
 if(await portOpen())throw Error('Port 5174 is already in use. Close AgOpenWeb before this isolated test; no commands were sent.');
 log=fs.openSync(path.join(output,'host.log'),'w');
 const head=path.resolve(options.head),dll=head.toLowerCase().endsWith('.dll');
 host=spawn(dll?(options.dotnet||'dotnet'):head,dll?[head,'--headless']:['--headless'],{env:{...process.env,AGOPENWEB_DATA:data},stdio:['ignore',log,log]});
 host.on('error',e=>errors.push(e.message));
 const start=Date.now();while(!await portOpen()){if(Date.now()-start>30000||host.exitCode!==null)throw Error('Isolated backend did not start. See host.log.');await delay(100)}
 browser=await chromium.launch({headless:true,...(options.browser?{executablePath:path.resolve(options.browser)}:{}),args:['--no-sandbox','--disable-dev-shm-usage']});
 const page=await browser.newPage({viewport:{width:1280,height:800}});operator=page;page.on('pageerror',e=>errors.push(e.message));
 await page.goto('http://127.0.0.1:5174');await page.waitForFunction(()=>typeof tick!=='undefined'&&tick&&typeof statusBar!=='undefined'&&statusBar&&iHoldControl,{timeout:30000});
 await page.evaluate(()=>{window.benchTransitions=[];let previous='';setInterval(()=>{if(!tick)return;const state={requested:tick.passive.requested,engaged:tick.passive.engaged,simulatedGps:tick.passive.simulatedGps,reason:tick.passive.reason,control:iHoldControl,autoSteer:tick.op.autoSteer,enabled:config?.passive?.enabled};const json=JSON.stringify(state);if(json!==previous){benchTransitions.push({time:performance.now(),...state});previous=json}},100)});
 const wait=async predicate=>page.waitForFunction(predicate,{},{timeout:20000});
 const send=async cmd=>{await page.evaluate(cmd=>transport.send(cmd),cmd);await delay(350)};
 const tool=()=>page.locator('#sim-tool-gps').click();
 await check('Tool GPS and passive default off',async()=>{const p=await page.evaluate(()=>tick.passive);if(p.simulatedGps||p.requested||p.engaged)throw Error(JSON.stringify(p))});
 if(!await page.evaluate(()=>statusBar.simEnabled))await send('sim.toggleEnable');
 if(!await page.evaluate(()=>statusBar.simPanelVisible))await send('sim.togglePanel');
 await send('field.new|Passive harness isolated field');
 await check('Tool GPS does not arm steering',async()=>{await tool();await wait(()=>tick.passive.simulatedGps);if(await page.evaluate(()=>tick.passive.requested||tick.op.autoSteer))throw Error('Tool GPS armed steering')});
 await send('config.set|tool.type:trailing');await send('config.set|tool.trailingHitchLength:8');
 await send('passive.config|passive.enabled:1');
 for(let i=0;i<12;i++)await send('sim.speedUp');
 await send('track.aPlus');
 await check('Tool request alone cannot engage',async()=>{await send('passive.set|1');await wait(()=>tick.passive.requested);if(await page.evaluate(()=>tick.passive.engaged))throw Error('Engaged with tractor steering off')});
 await check('Trailing implement engages',async()=>{await send('autosteer.toggle');await wait(()=>tick.passive.engaged&&tick.passive.toolValid);return JSON.stringify(await page.evaluate(()=>tick.passive))});
 await check('Off-line tool produces a bounded tractor correction',async()=>{
   await send('config.set|tool.offset:0.6');
   await wait(()=>tick.passive.engaged&&Math.abs(tick.passive.offset)>.02);
   const p=await page.evaluate(()=>tick.passive);
   if(Math.abs(p.offset)>1.000001)throw Error('Exceeded configured 1 m limit');
   await send('config.set|tool.offset:0');
   return JSON.stringify(p);
 });
 await check('Passive button clears correction',async()=>{await send('passive.set|0');await wait(()=>!tick.passive.requested&&!tick.passive.engaged&&tick.passive.offset===0);await send('passive.set|1');await wait(()=>tick.passive.engaged)});
 await check('Tool GPS off removes simulated fixes',async()=>{await tool();await wait(()=>!tick.passive.simulatedGps&&!tick.passive.engaged&&tick.passive.offset===0)});
 await check('Real implement UDP input works without Tool GPS',async()=>{
   udp=dgram.createSocket('udp4');
   function coord(value,latitude){value=Math.abs(value);const degrees=Math.floor(value),minutes=(value-degrees)*60;return String(degrees).padStart(latitude?2:3,'0')+minutes.toFixed(6).padStart(9,'0')}
   const timer=setInterval(async()=>{try{const s=await page.evaluate(()=>({lat:statusBar.lat,lon:statusBar.lon}));const body=`PAOGI,120000,${coord(s.lat-8/111320,true)},N,${coord(s.lon,false)},W,4,14,0.8,300,0.5,4,0,0,0,0,1,1,0,0`;let sum=0;for(const c of body)sum^=c.charCodeAt(0);udp.send(Buffer.from('$'+body+'*'+sum.toString(16).padStart(2,'0').toUpperCase()+'\r\n'),19999,'127.0.0.1')}catch{}},100);
   try{await wait(()=>tick.passive.engaged&&!tick.passive.simulatedGps&&tick.passive.headingSource==='Dual GNSS')}
   finally{clearInterval(timer)}
   await wait(()=>!tick.passive.engaged&&tick.passive.reason.includes('stale'));
   return 'Loopback PAOGI drove live guidance with simulator Tool GPS off; stopping packets caused stale-fix disengagement.';
 });
 await tool();await wait(()=>tick.passive.engaged);
 await check('Fixed implement engages',async()=>{await send('config.set|tool.type:rear');await wait(()=>tick.passive.engaged&&tick.passive.toolValid)});
 await check('Observer commands cannot change simulator or arm state',async()=>{
   const observer=await browser.newPage();await observer.goto('http://127.0.0.1:5174');await observer.waitForFunction(()=>typeof tick!=='undefined'&&tick);
   try{await observer.evaluate(()=>{transport.send('passive.simulator|0');transport.send('passive.set|0');transport.send('config.set|passive.enabled:0')});await delay(800);
     if(!await page.evaluate(()=>tick.passive.simulatedGps&&tick.passive.requested&&tick.passive.engaged))throw Error('Observer changed passive state')}
   finally{await observer.close()}
 });
 await check('Tractor off resets passive',async()=>{await send('autosteer.toggle');await wait(()=>!tick.passive.engaged&&tick.passive.offset===0)});
 await check('Losing operator authority clears passive request',async()=>{
   await send('autosteer.toggle');await wait(()=>tick.passive.engaged);
   const observer=await browser.newPage();await observer.goto('http://127.0.0.1:5174');await observer.waitForFunction(()=>typeof tick!=='undefined'&&tick);
   fs.writeFileSync(path.join(output,'transitions.json'),JSON.stringify(await page.evaluate(()=>window.benchTransitions),null,2));await page.close();await observer.waitForFunction(()=>!tick.passive.requested&&!tick.passive.engaged&&tick.passive.offset===0,{},{timeout:20000});await observer.close();
 });
 await check('Section controls are independent',async()=>{
   // Run potentially disruptive coverage updates last so a deadman reset does not hide other tests.
   const sectionPage=await browser.newPage({viewport:{width:1280,height:800}});operator=sectionPage;
   sectionPage.on('pageerror',e=>errors.push(e.message));await sectionPage.goto('http://127.0.0.1:5174');
   await sectionPage.waitForFunction(()=>typeof tick!=='undefined'&&tick&&iHoldControl,{},{timeout:20000});
   await sectionPage.evaluate(()=>{window.benchTransitions=[];let previous='';setInterval(()=>{const state={requested:tick.passive.requested,engaged:tick.passive.engaged,control:iHoldControl,autoSteer:tick.op.autoSteer,reason:tick.passive.reason};const json=JSON.stringify(state);if(json!==previous){benchTransitions.push({time:performance.now(),...state});previous=json}},100)});
   await sectionPage.evaluate(()=>{transport.send('autosteer.toggle');transport.send('passive.set|1')});
   await sectionPage.waitForFunction(()=>tick.passive.engaged,{},{timeout:20000});
   for(const cmd of ['section.master','section.master','section.manual','section.manual']){
     await sectionPage.evaluate(cmd=>transport.send(cmd),cmd);await delay(800);
     const state=await sectionPage.evaluate(()=>({p:tick.passive,op:tick.op,control:iHoldControl}));
     if(!state.p.requested||!state.p.engaged||!state.control)throw Error('Reset during '+cmd+': '+JSON.stringify(state));
   }
 });
 await check('Browser has no script errors',async()=>{if(errors.length)throw Error(errors.join('; '))});
})().catch(e=>{console.error(e.stack||e);if(!checks.some(c=>!c.pass))checks.push({name:'Harness setup/runtime',pass:false,detail:e.message});process.exitCode=1})
.finally(async()=>{
 if(operator&&!operator.isClosed()){try{fs.writeFileSync(path.join(output,'transitions.json'),JSON.stringify(await operator.evaluate(()=>window.benchTransitions||[]),null,2))}catch{}}
 if(udp)udp.close();if(browser)await browser.close();
 if(host&&host.exitCode===null){host.kill();await Promise.race([new Promise(r=>host.once('exit',r)),delay(3000)]);if(host.exitCode===null)host.kill('SIGKILL')}
 if(log!==undefined)fs.closeSync(log);
 const report={plannedChecks:14,notRun:Math.max(0,14-checks.length),title:'AgOpenWeb live simulator integration harness',createdUtc:new Date().toISOString(),dataDirectory:data,checks,errors};
 fs.writeFileSync(path.join(output,'browser-results.json'),JSON.stringify(report,null,2));
 const escape=s=>String(s).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('>','&gt;');
 fs.writeFileSync(path.join(output,'browser-report.html'),`<!doctype html><meta charset="utf-8"><title>Simulator integration results</title><style>body{font:16px system-ui;max-width:1000px;margin:30px auto;padding:20px}td{padding:10px;border-bottom:1px solid #ddd}.pass{color:green}.fail{color:#b22}</style><h1>${report.title}</h1><p>${checks.filter(c=>c.pass).length} passed, ${checks.filter(c=>!c.pass).length} failed, ${report.notRun} not run. Isolated test profile; actual shared desktop backend and web UI.</p><table>${checks.map(c=>`<tr><td class="${c.pass?'pass':'fail'}">${c.pass?'PASS':'FAIL'}</td><td>${escape(c.name)}</td><td>${escape(c.detail)}</td></tr>`).join('')}</table><p>Windows native shell and hardware field behaviour are not tested by this harness.</p>`);
 console.log(`${checks.filter(c=>c.pass).length} passed, ${checks.filter(c=>!c.pass).length} failed, ${report.notRun} not run. Results: ${output}`);
});
