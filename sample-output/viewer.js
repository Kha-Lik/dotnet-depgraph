/* Cytoscape.js is bundled separately under its MIT license. Repository-derived strings are only assigned through textContent. */
(() => {
  'use strict';
  const payload = JSON.parse(document.getElementById('graph-data').textContent);
  const $ = id => document.getElementById(id);
  const state = { view: payload.defaults.filterMode || 'contract', selected: null, collapsed: !!payload.defaults.collapseLocalPackages };
  const palette = ['#58a6ff','#f0883e','#3fb950','#a371f7','#f85149','#d29922','#39c5cf','#db61a2','#7ee787','#79c0ff','#ffa657','#bc8cff'];
  const hash = (s, seed) => { let h = seed|0; for (let i=0;i<s.length;i++) h = Math.imul(h ^ s.charCodeAt(i), 16777619); return h >>> 0; };
  function graph(){ return payload[state.view]; }
  function elements(){
    const g=graph(), producer=new Map();
    if(state.collapsed) payload.raw.edges.filter(e=>e.kind==='produces-package').forEach(e=>producer.set(e.target,e.source));
    const visibleNodes=g.nodes.filter(n=>!producer.has(n.id));
    const edges=[], seen=new Set();
    for(const e of g.edges){
      if(e.kind==='produces-package' && state.collapsed) continue;
      const source=producer.get(e.source)||e.source, target=producer.get(e.target)||e.target;
      if(source===target) continue;
      const key=`${source}|${target}|${e.kind}`; if(seen.has(key)) continue; seen.add(key);
      edges.push({data:{...e,id:e.id+(state.collapsed?':collapsed':''),source,target}});
    }
    return [...visibleNodes.map(n=>({data:{...n,size:18+Math.min(34,Math.log2(2+n.transitiveDependents+n.inDegree)*6),color:palette[(n.community<0?0:n.community)%palette.length]}})),...edges];
  }
  let cy=cytoscape({container:$('cy'),elements:elements(),pixelRatio:'auto',wheelSensitivity:.18,minZoom:.08,maxZoom:3,
    style:[
      {selector:'node',style:{'background-color':'data(color)','width':'data(size)','height':'data(size)','border-width':2,'border-color':'#0d1117','label':'','font-size':10,'color':'#e6edf3','text-outline-width':2,'text-outline-color':'#0d1117'}},
      {selector:'node[kind="project"]',style:{shape:'round-rectangle','border-color':'#f0f6fc'}},
      {selector:'node[classification="test-project"]',style:{shape:'diamond'}},{selector:'node[classification="executable"],node[classification="web-application"],node[classification="azure-functions"]',style:{shape:'hexagon'}},
      {selector:'node[versionSkew]',style:{'border-color':'#f85149','border-width':5}},
      {selector:'node.show-label, node:selected',style:{label:'data(label)'}},{selector:'node:selected',style:{'overlay-color':'#58a6ff','overlay-opacity':.18,'overlay-padding':8}},
      {selector:'edge',style:{width:1,'line-color':'#484f58','target-arrow-color':'#484f58','target-arrow-shape':'triangle','curve-style':'bezier','opacity':.28,'arrow-scale':.7}},
      {selector:'edge[kind="project-reference"]',style:{'line-color':'#58a6ff','target-arrow-color':'#58a6ff',width:2}},
      {selector:'edge[kind="package-reference"]',style:{'line-color':'#8b949e','target-arrow-color':'#8b949e',width:1.8}},
      {selector:'edge[kind="contracted-path"]',style:{'line-style':'dashed','line-color':'#d29922','target-arrow-color':'#d29922',width:2}},
      {selector:'edge[kind="produces-package"]',style:{'line-style':'dotted','line-color':'#a371f7','target-arrow-color':'#a371f7',opacity:.45}},
      {selector:'.faded',style:{opacity:.06}},{selector:'.upstream',style:{'line-color':'#f0883e','target-arrow-color':'#f0883e',opacity:1,'z-index':10}},{selector:'.downstream',style:{'line-color':'#3fb950','target-arrow-color':'#3fb950',opacity:1,'z-index':10}}
    ],layout:{name:'preset'}});
  function positionAndLayout(name='cose'){
    const seed=payload.defaults.seed||42;
    cy.nodes().forEach(n=>{const h=hash(n.id(),seed);n.position({x:(h%1000)-500,y:((h>>>10)%1000)-500});});
    cy.layout(name==='breadthfirst'?{name:'breadthfirst',directed:true,padding:40,spacingFactor:1.2}:{name:'cose',randomize:false,animate:false,componentSpacing:140,nodeRepulsion:node=>5000+node.degree()*250,idealEdgeLength:edge=>edge.data('kind')==='contracted-path'?130:75,numIter:800,padding:50}).run();
  }
  function replace(){cy.elements().remove();cy.add(elements());populateFilters();positionAndLayout();applyFilters();}
  function populate(id,values){const s=$(id),old=s.value;s.replaceChildren(new Option('All',''));[...values].sort((a,b)=>String(a).localeCompare(String(b),undefined,{numeric:true})).forEach(v=>s.add(new Option(String(v),String(v))));s.value=old;}
  function populateFilters(){const g=graph();populate('kind',new Set(g.nodes.map(n=>n.kind)));populate('edgeKind',new Set(g.edges.map(e=>e.kind)));populate('component',new Set(g.nodes.map(n=>n.component)));populate('community',new Set(g.nodes.map(n=>n.community)));populate('tfm',new Set(g.nodes.flatMap(n=>n.targetFrameworks||[])));populate('rid',new Set(g.nodes.flatMap(n=>n.runtimeIdentifiers||[])));const box=$('components');box.replaceChildren(Object.assign(document.createElement('b'),{textContent:'Components'}));const groups=new Map();g.nodes.forEach(n=>{if(!groups.has(n.component))groups.set(n.component,[]);groups.get(n.component).push(n);});[...groups.entries()].sort((a,b)=>b[1].length-a[1].length).forEach(([id,nodes])=>{const b=document.createElement('button');b.className='component-link';b.textContent=`${id}: ${nodes.length} nodes · ${nodes.sort((a,b)=>b.centrality-a.centrality)[0]?.label||''}`;b.onclick=()=>cy.elements().filter(e=>e.isNode()&&String(e.data('component'))===String(id)).fit(50);box.appendChild(b);});}
  function applyFilters(){
    cy.elements().removeClass('faded').style('display','element');
    const kind=$('kind').value,ek=$('edgeKind').value,component=$('component').value,community=$('community').value,tfm=$('tfm').value,rid=$('rid').value,skew=$('skew').checked,q=$('search').value.trim().toLowerCase();
    cy.nodes().forEach(n=>{const d=n.data(),match=(!kind||d.kind===kind)&&(!component||String(d.component)===component)&&(!community||String(d.community)===community)&&(!tfm||(d.targetFrameworks||[]).includes(tfm))&&(!rid||(d.runtimeIdentifiers||[]).includes(rid))&&(!skew||d.versionSkew)&&(!q||d.label.toLowerCase().includes(q)||d.id.toLowerCase().includes(q));if(!match)n.style('display','none');});
    cy.edges().forEach(e=>{if((ek&&e.data('kind')!==ek)||e.source().style('display')==='none'||e.target().style('display')==='none')e.style('display','none');});
    const hops=Number($('hops').value);if(hops&&state.selected){let keep=cy.$id(state.selected),front=keep;for(let i=0;i<hops;i++){front=front.neighborhood();keep=keep.union(front);}cy.elements().difference(keep).style('display','none');}
    updateLabels();
  }
  function updateLabels(){const zoom=cy.zoom();cy.nodes().removeClass('show-label');cy.nodes().filter(n=>zoom>1.05||n.data('centrality')>3.2||($('search').value&&n.style('display')!=='none')).addClass('show-label');}
  function detail(n){state.selected=n.id();cy.elements().removeClass('faded upstream downstream');const up=n.incomers(),down=n.outgoers();cy.elements().difference(n.union(up).union(down)).addClass('faded');up.edges().addClass('upstream');down.edges().addClass('downstream');const d=n.data(),panel=$('details');panel.replaceChildren();const title=document.createElement('h2');title.textContent=d.label;panel.appendChild(title);const values={Identifier:d.id,Kind:d.kind,Classification:d.classification||'—',Path:d.path||'—',Versions:(d.versions||[]).join(', ')||'—',Frameworks:(d.targetFrameworks||[]).join(', ')||'—',RIDs:(d.runtimeIdentifiers||[]).join(', ')||'—',Component:d.component,Community:d.community,'Direct dependencies':d.directDependencies,'Transitive dependencies':d.transitiveDependencies,'Direct dependents':d.directDependents,'Transitive dependents':d.transitiveDependents,'Version skew':d.versionSkew?'yes':'no','Cycle member':d.inCycle?'yes':'no'};for(const [k,v] of Object.entries(values)){const row=document.createElement('div');row.className='detail';const b=document.createElement('b');b.textContent=k;const span=document.createElement('span');span.textContent=String(v);row.append(b,span);panel.appendChild(row);}const rel=document.createElement('div');rel.className='detail';const b=document.createElement('b');b.textContent='Immediate relationships';rel.appendChild(b);[...up.nodes(),...down.nodes()].sort((a,b)=>a.data('label').localeCompare(b.data('label'))).forEach(x=>{const p=document.createElement('div');p.textContent=(up.contains(x)?'dependent: ':'dependency: ')+x.data('label');rel.appendChild(p);});panel.appendChild(rel);const diagnostics=(graph().diagnostics||[]).filter(x=>x.projectId===d.id);if(diagnostics.length){const warnings=document.createElement('div');warnings.className='detail';const heading=document.createElement('b');heading.textContent='Diagnostics';warnings.appendChild(heading);diagnostics.forEach(x=>{const p=document.createElement('div');p.textContent=`${x.severity}: ${x.message}`;warnings.appendChild(p);});panel.appendChild(warnings);}}
  cy.on('tap','node',e=>detail(e.target));cy.on('tap',e=>{if(e.target===cy){state.selected=null;cy.elements().removeClass('faded upstream downstream');}});cy.on('zoom',updateLabels);
  ['search','kind','edgeKind','component','community','tfm','rid','skew','hops'].forEach(id=>$(id).addEventListener(id==='search'?'input':'change',applyFilters));
  $('view').value=state.view;$('collapse').checked=state.collapsed;$('view').onchange=e=>{state.view=e.target.value;replace();};$('collapse').onchange=e=>{state.collapsed=e.target.checked;replace();};$('fit').onclick=()=>cy.elements(':visible').fit(45);$('reset').onclick=()=>{state.selected=null;['search','kind','edgeKind','component','community','tfm','rid'].forEach(x=>$(x).value='');$('skew').checked=false;$('hops').value='0';cy.elements().removeClass('faded upstream downstream');applyFilters();cy.fit(45);};$('layout').onclick=()=>positionAndLayout($('layout').dataset.mode==='tree'?'breadthfirst':'cose');$('layout').ondblclick=()=>{positionAndLayout('breadthfirst');};$('png').onclick=()=>{const a=document.createElement('a');a.download='dependency-graph.png';a.href=cy.png({full:true,scale:2,bg:'#0d1117'});a.click();};$('json').onclick=()=>{const shown={schemaVersion:'display-1.0',nodes:cy.nodes(':visible').map(n=>n.data()),edges:cy.edges(':visible').map(e=>e.data())};const a=document.createElement('a');a.download='displayed-graph.json';a.href=URL.createObjectURL(new Blob([JSON.stringify(shown,null,2)],{type:'application/json'}));a.click();setTimeout(()=>URL.revokeObjectURL(a.href),1000);};
  if(!payload.raw.completeness.complete){const w=$('warning');w.hidden=false;w.replaceChildren(document.createTextNode('Incomplete extraction — some projects lack authoritative data. '));const a=document.createElement('a');a.href='diagnostics.json';a.textContent='Open diagnostics';w.appendChild(a);}
  populateFilters();positionAndLayout();updateLabels();
})();
