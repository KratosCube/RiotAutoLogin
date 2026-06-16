namespace RiotAutoLogin.Services
{
    public static class RemotePickPageHtml
    {
        public static string Get() => """
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <title>Remote Pick</title>
  <style>
    :root { color-scheme: dark; --bg:#03070d; --panel:rgba(8,16,26,.92); --line:rgba(200,170,110,.38); --gold:#c8aa6e; --gold2:#f0d58a; --cyan:#0ac8b9; --red:#b64a55; --text:#f3ead7; --muted:#94a3b8; font-family: Georgia, 'Times New Roman', serif; }
    * { box-sizing: border-box; -webkit-tap-highlight-color: transparent; }
    body { margin:0; color:var(--text); background: radial-gradient(circle at 50% 0%, rgba(43,82,104,.38), transparent 30rem), radial-gradient(circle at 85% 20%, rgba(200,170,110,.16), transparent 24rem), linear-gradient(135deg,#02060b,#0b1018 45%,#02050a); overflow-x:hidden; }
    .app { min-height:100vh; padding:10px; }
    .sticky-status { position:sticky; top:0; z-index:20; margin:-10px -10px 10px; padding:9px 10px 10px; border-bottom:1px solid var(--line); background:linear-gradient(180deg,rgba(3,7,13,.99),rgba(3,7,13,.92)); backdrop-filter:blur(12px); box-shadow:0 12px 35px rgba(0,0,0,.45); }
    .topbar { display:grid; grid-template-columns:1fr auto 1fr; align-items:center; gap:8px; margin-bottom:9px; }
    h1 { margin:0; text-align:center; letter-spacing:.08em; font-size:clamp(18px,5vw,31px); color:var(--gold2); text-shadow:0 2px 18px rgba(240,213,138,.22); }
    .side-title { color:var(--muted); font:700 10px system-ui,sans-serif; text-transform:uppercase; letter-spacing:.18em; }
    .side-title.right { text-align:right; }
    .phase-dock { max-width:1120px; margin:0 auto; display:grid; grid-template-columns:1fr auto; border:1px solid var(--line); background:linear-gradient(180deg,rgba(18,31,44,.94),rgba(5,11,19,.96)); }
    .phase-main { padding:10px; min-width:0; }
    .phase { color:var(--cyan); font:800 11px system-ui,sans-serif; letter-spacing:.16em; text-transform:uppercase; }
    .message { margin-top:5px; font-size:15px; line-height:1.25; overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
    .status-actions { display:flex; flex-wrap:wrap; gap:7px; margin-top:9px; align-items:center; }
    .turn-pill { padding:6px 9px; border:1px solid rgba(10,200,185,.48); color:#d7fffb; background:rgba(10,200,185,.09); font:800 10px system-ui,sans-serif; text-transform:uppercase; letter-spacing:.1em; }
    .turn-pill.waiting { border-color:rgba(200,170,110,.33); color:var(--gold); background:rgba(200,170,110,.08); }
    .turn-pill.ban { border-color:rgba(182,74,85,.7); color:#ffd7dc; background:rgba(182,74,85,.14); }
    .turn-pill.loading { border-color:rgba(240,213,138,.58); color:#ffe7a6; background:rgba(240,213,138,.11); }
    .turn-pill.ingame { border-color:rgba(10,200,185,.7); color:#d7fffb; background:rgba(10,200,185,.15); }
    .leave-button { border:1px solid rgba(182,74,85,.72); background:rgba(182,74,85,.13); color:#ffd7dc; min-height:30px; padding:0 10px; font:800 10px system-ui,sans-serif; text-transform:uppercase; letter-spacing:.1em; cursor:pointer; }
    .leave-button:disabled, button:disabled { opacity:.35; cursor:not-allowed; }
    .timer-box { min-width:96px; display:grid; place-items:center; padding:8px 10px; border-left:1px solid rgba(200,170,110,.25); background:rgba(0,0,0,.2); }
    .timer-value { font:900 30px system-ui,sans-serif; color:var(--gold2); line-height:1; }
    .timer-value.game { font-size:22px; }
    .timer-label { margin-top:3px; font:800 9px system-ui,sans-serif; color:var(--muted); text-transform:uppercase; letter-spacing:.16em; }
    .accepted-alert { display:none; max-width:1120px; margin:9px auto 0; padding:10px 12px; border:1px solid rgba(10,200,185,.65); color:#d7fffb; background:linear-gradient(90deg,rgba(10,200,185,.18),rgba(10,200,185,.06)); font:900 13px system-ui,sans-serif; letter-spacing:.08em; text-transform:uppercase; }
    .layout { display:grid; grid-template-columns:230px minmax(0,1fr) 270px; gap:12px; max-width:1180px; margin:0 auto; align-items:start; }
    .panel { border:1px solid rgba(200,170,110,.27); background:var(--panel); box-shadow:inset 0 0 30px rgba(0,0,0,.28); }
    .panel h2,.section-title { margin:0; padding:11px 12px; border-bottom:1px solid rgba(200,170,110,.18); color:var(--gold); font:800 11px system-ui,sans-serif; text-transform:uppercase; letter-spacing:.16em; }
    .rail-list { padding:10px; display:grid; gap:8px; }
    .rail-empty { color:var(--muted); font:12px system-ui,sans-serif; padding:10px; }
    .mini-card { display:flex; align-items:center; gap:9px; padding:7px; background:rgba(255,255,255,.035); border:1px solid rgba(255,255,255,.055); min-height:50px; }
    .mini-card img { width:38px; height:38px; object-fit:cover; border:1px solid rgba(200,170,110,.45); background:#061019; }
    .mini-card .name { font:700 12px system-ui,sans-serif; }
    .mini-card .sub { color:var(--muted); font:10px system-ui,sans-serif; margin-top:2px; }
    .mini-card.banned img { filter:grayscale(1) brightness(.6); border-color:rgba(182,74,85,.8); }
    .mobile-loadout { display:none; }
    .search-wrap { display:grid; grid-template-columns:1fr auto; gap:9px; margin-bottom:10px; }
    input { width:100%; border:1px solid rgba(200,170,110,.32); background:rgba(5,10,18,.9); color:var(--text); padding:13px; font:15px system-ui,sans-serif; outline:none; border-radius:0; }
    .count { display:grid; place-items:center; min-width:64px; border:1px solid rgba(200,170,110,.32); color:var(--gold); background:rgba(5,10,18,.9); font:800 11px system-ui,sans-serif; text-transform:uppercase; }
    .champion-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(112px,1fr)); gap:9px; }
    .champion { position:relative; overflow:hidden; border:1px solid rgba(200,170,110,.34); background:#080d14; min-height:178px; box-shadow:0 8px 24px rgba(0,0,0,.25); }
    .portrait { position:relative; height:98px; overflow:hidden; background:#061019; }
    .portrait img { width:100%; height:100%; object-fit:cover; display:block; transition:transform .18s ease,filter .18s ease; }
    .champion.available:hover { border-color:var(--gold2); }
    .champion.available:hover img { filter:brightness(1.12); transform:scale(1.03); }
    .champion-name { padding:8px 8px 7px; background:linear-gradient(180deg,rgba(8,14,23,.92),rgba(4,7,12,.98)); font:800 11px system-ui,sans-serif; text-transform:uppercase; letter-spacing:.04em; white-space:nowrap; overflow:hidden; text-overflow:ellipsis; }
    .champion.disabled .portrait img { filter:grayscale(1) brightness(.38); }
    .champion.disabled .portrait::after { content:''; position:absolute; inset:0; background:linear-gradient(135deg,transparent 45%,rgba(182,74,85,.95) 48%,rgba(182,74,85,.95) 52%,transparent 55%); }
    .badge { position:absolute; top:7px; left:7px; z-index:2; padding:4px 6px; background:rgba(5,8,12,.84); border:1px solid rgba(200,170,110,.42); color:var(--gold2); font:800 9px system-ui,sans-serif; letter-spacing:.08em; text-transform:uppercase; }
    .badge.banned { color:#ffb3ba; border-color:rgba(182,74,85,.9); }
    .badge.intent { color:#d7fffb; border-color:rgba(10,200,185,.8); }
    .champion-actions { display:grid; grid-template-columns:1fr 1fr; gap:6px; padding:0 7px 8px; }
    button { border:1px solid rgba(200,170,110,.35); background:rgba(9,16,25,.92); color:var(--text); min-height:34px; font:800 10px system-ui,sans-serif; text-transform:uppercase; letter-spacing:.08em; cursor:pointer; }
    .lock { border-color:rgba(10,200,185,.55); color:#d7fffb; }
    .ban { border-color:rgba(182,74,85,.7); color:#ffd7dc; }
    .hover { color:var(--gold); }
    .loadout { padding:10px; color:var(--muted); font:13px system-ui,sans-serif; }
    .spell-slots { display:grid; grid-template-columns:1fr 1fr; gap:8px; }
    .spell-button { display:flex; align-items:center; gap:8px; padding:8px; min-height:52px; text-align:left; text-transform:none; letter-spacing:0; }
    .spell-button img { width:34px; height:34px; border:1px solid rgba(200,170,110,.45); background:#061019; }
    .spell-button span { display:block; color:var(--gold); font-size:10px; text-transform:uppercase; letter-spacing:.1em; }
    .spell-button strong { display:block; font-size:12px; color:var(--text); margin-top:2px; }
    .rune-block { margin-top:10px; border:1px solid rgba(200,170,110,.18); background:rgba(200,170,110,.04); }
    .rune-current { padding:9px 10px; color:var(--gold); font:800 11px system-ui,sans-serif; text-transform:uppercase; letter-spacing:.08em; border-bottom:1px solid rgba(200,170,110,.14); }
    .rune-list { display:grid; gap:6px; padding:8px; }
    .rune-button { display:grid; grid-template-columns:1fr auto; align-items:center; gap:8px; padding:8px; text-align:left; text-transform:none; letter-spacing:0; min-height:40px; }
    .rune-button.current { border-color:rgba(10,200,185,.55); color:#d7fffb; }
    .rune-button.recommended { border-color:rgba(240,213,138,.45); }
    .rune-button span { overflow:hidden; text-overflow:ellipsis; white-space:nowrap; }
    .rune-button small { color:var(--muted); display:block; margin-top:2px; font:10px system-ui,sans-serif; }
    .rune-button em { color:var(--gold); font-style:normal; font-size:10px; text-transform:uppercase; letter-spacing:.08em; }
    .modal-backdrop { position:fixed; inset:0; z-index:30; display:none; background:rgba(0,0,0,.72); padding:16px; overflow:auto; }
    .modal { max-width:680px; margin:4vh auto; border:1px solid var(--line); background:rgba(4,9,16,.98); box-shadow:0 20px 70px rgba(0,0,0,.6); }
    .modal-head { display:flex; justify-content:space-between; align-items:center; gap:10px; padding:12px; border-bottom:1px solid rgba(200,170,110,.18); }
    .modal-title { color:var(--gold); font:800 12px system-ui,sans-serif; text-transform:uppercase; letter-spacing:.15em; }
    .spell-grid { display:grid; grid-template-columns:repeat(auto-fill,minmax(140px,1fr)); gap:8px; padding:12px; }
    .spell-option { display:flex; align-items:center; gap:9px; padding:8px; min-height:52px; border:1px solid rgba(200,170,110,.28); background:rgba(255,255,255,.035); color:var(--text); text-align:left; cursor:pointer; }
    .spell-option img { width:34px; height:34px; border:1px solid rgba(200,170,110,.4); }
    .toast { position:fixed; left:12px; right:12px; bottom:12px; z-index:40; padding:13px 15px; border:1px solid rgba(200,170,110,.42); background:rgba(7,13,21,.97); color:var(--text); box-shadow:0 16px 40px rgba(0,0,0,.45); display:none; font:14px system-ui,sans-serif; }
    @media (max-width:900px) { .app{padding:8px;padding-bottom:96px}.sticky-status{margin:-8px -8px 8px;padding:8px}.topbar{grid-template-columns:1fr;margin-bottom:7px}.side-title{display:none}h1{font-size:20px}.phase-main{padding:9px}.message{font-size:13px;white-space:normal;display:-webkit-box;-webkit-line-clamp:2;-webkit-box-orient:vertical}.timer-box{min-width:76px;padding:7px}.timer-value{font-size:25px}.timer-value.game{font-size:18px}.layout{grid-template-columns:1fr;gap:8px}.panel.side{display:none}.mobile-loadout{display:block;margin-bottom:9px}.search-wrap{grid-template-columns:1fr 54px;position:sticky;top:151px;z-index:8;background:rgba(3,7,13,.92);padding-bottom:8px}.champion-grid{grid-template-columns:repeat(3,minmax(0,1fr));gap:7px}.champion{min-height:162px}.portrait{height:82px}.champion-name{font-size:10px;padding:7px 6px}.champion-actions{gap:5px;padding:0 5px 6px}.champion-actions button{min-height:32px;font-size:9px;letter-spacing:.04em}.spell-grid{grid-template-columns:1fr 1fr} }
  </style>
</head>
<body>
  <div class="app">
    <div class="sticky-status">
      <div class="topbar"><div class="side-title">Ally picks</div><h1>CHOOSE YOUR CHAMPION</h1><div class="side-title right">Enemy picks</div></div>
      <section class="phase-dock"><div class="phase-main"><div id="phase" class="phase">Connecting</div><div id="message" class="message">Connecting to RiotAutoLogin Remote Pick...</div><div class="status-actions"><div id="turn" class="turn-pill waiting">Waiting</div><div id="gameStatus" class="turn-pill waiting">Game Waiting</div><button id="leaveButton" class="leave-button" disabled>Leave Lobby</button></div></div><div class="timer-box"><div id="timer" class="timer-value">--</div><div id="timerLabel" class="timer-label">seconds</div></div></section>
      <div id="acceptedAlert" class="accepted-alert">Match accepted — champion select started. Pick/ban from this page.</div>
    </div>
    <section id="mobileLoadout" class="panel mobile-loadout"></section>
    <section class="layout"><aside class="panel side"><h2>Picked</h2><div id="pickedList" class="rail-list"></div></aside><main class="center"><div class="search-wrap"><input id="search" placeholder="Search champion..." autocomplete="off"><div id="count" class="count">0</div></div><div id="champions" class="champion-grid"></div></main><aside class="panel side"><h2>Banned</h2><div id="bannedList" class="rail-list"></div><div id="desktopLoadout" class="loadout"></div></aside></section>
  </div>
  <div id="spellModal" class="modal-backdrop"><div class="modal"><div class="modal-head"><div id="spellModalTitle" class="modal-title">Choose spell</div><button id="closeSpellModal">Close</button></div><div id="spellGrid" class="spell-grid"></div></div></div>
  <div id="toast" class="toast"></div>
<script>
let state=null, previousPhase=null, query='', timerBaseMs=null, timerBasePerf=0, timerInfinite=false, timerKey='', lastVisibleSecond=null, timerRequestId=0;
const championsEl=document.getElementById('champions'), pickedListEl=document.getElementById('pickedList'), bannedListEl=document.getElementById('bannedList'), phaseEl=document.getElementById('phase'), messageEl=document.getElementById('message'), turnEl=document.getElementById('turn'), gameStatusEl=document.getElementById('gameStatus'), timerEl=document.getElementById('timer'), timerLabelEl=document.getElementById('timerLabel'), acceptedAlertEl=document.getElementById('acceptedAlert'), searchEl=document.getElementById('search'), countEl=document.getElementById('count'), toastEl=document.getElementById('toast'), desktopLoadoutEl=document.getElementById('desktopLoadout'), mobileLoadoutEl=document.getElementById('mobileLoadout'), spellModalEl=document.getElementById('spellModal'), spellGridEl=document.getElementById('spellGrid'), spellModalTitleEl=document.getElementById('spellModalTitle'), leaveButtonEl=document.getElementById('leaveButton');
searchEl.addEventListener('input',()=>{query=searchEl.value.trim().toLowerCase();renderChampionGrid();});
document.getElementById('closeSpellModal').onclick=closeSpellModal; spellModalEl.addEventListener('click',e=>{if(e.target===spellModalEl)closeSpellModal();}); leaveButtonEl.onclick=leaveLobby;
function showToast(message){toastEl.textContent=message;toastEl.style.display='block';clearTimeout(showToast.timer);showToast.timer=setTimeout(()=>toastEl.style.display='none',2600);}
function showAcceptedAlert(){acceptedAlertEl.style.display='block';showToast('Match accepted. Champion select started.');if('vibrate' in navigator)navigator.vibrate([300,120,300]);clearTimeout(showAcceptedAlert.timer);showAcceptedAlert.timer=setTimeout(()=>acceptedAlertEl.style.display='none',15000);}
async function loadState(){try{const r=await fetch('/api/state',{cache:'no-store'});const n=await r.json();applyPhaseTransitionAlert(n);state=n;renderAll();}catch{showDisconnectedState();}}
async function loadTimer(){const requestId=++timerRequestId;try{const r=await fetch('/api/timer',{cache:'no-store'});const t=await r.json();if(requestId<timerRequestId)return;syncTimer(t);applyPhaseTransitionAlert(t);if(state){Object.assign(state,{phase:t.phase,champSelectPhase:t.champSelectPhase,phaseLabel:t.phaseLabel,timeLeftInPhaseMs:t.timeLeftInPhaseMs,totalTimeInPhaseMs:t.totalTimeInPhaseMs,isTimerInfinite:t.isTimerInfinite,isInChampSelect:t.isInChampSelect,isMyTurn:t.isMyTurn,canLeave:t.canLeave,actionId:t.actionId,actionType:t.actionType,pickActionId:t.pickActionId,actionGroupIndex:t.actionGroupIndex,timerActionKey:t.timerActionKey,assignedPosition:t.assignedPosition,mapId:t.mapId,queueId:t.queueId,queueName:t.queueName,champSelectMode:t.champSelectMode,isRandomChampionMode:t.isRandomChampionMode,isGameClientRunning:t.isGameClientRunning,isGameLoaded:t.isGameLoaded,gameTimeSeconds:t.gameTimeSeconds,gameStatus:t.gameStatus,message:t.message,selectedChampionId:t.selectedChampionId,pickIntentChampionId:t.pickIntentChampionId,spell1Id:t.spell1Id,spell2Id:t.spell2Id,bannedChampionIds:t.bannedChampionIds||state.bannedChampionIds,availableSpellIds:t.availableSpellIds||state.availableSpellIds,availableChampionIds:t.availableChampionIds||state.availableChampionIds});}else{state=t;}updateStatusOnly();}catch{}}
function timerIdentity(s){return [s.phase||'',s.champSelectPhase||'',s.timerActionKey||'',s.actionGroupIndex??'',s.actionType||'',s.actionId||''].join('|');}
function syncTimer(s){const nextKey=timerIdentity(s);const keyChanged=nextKey!==timerKey;timerInfinite=Boolean(s.isTimerInfinite);if(typeof s.timeLeftInPhaseMs!=='number'||s.timeLeftInPhaseMs<0){timerBaseMs=null;timerBasePerf=performance.now();timerKey=nextKey;lastVisibleSecond=null;return;}const serverMs=Math.max(0,s.timeLeftInPhaseMs);const currentMs=currentTimerMs();if(keyChanged||currentMs===null||currentMs===Infinity){timerBaseMs=serverMs;timerBasePerf=performance.now();timerKey=nextKey;lastVisibleSecond=null;return;}const serverIsLower=serverMs<currentMs-150;const hardResyncAfterZero=currentMs<500&&serverMs>2000;if(serverIsLower||hardResyncAfterZero){timerBaseMs=serverMs;timerBasePerf=performance.now();if(hardResyncAfterZero)lastVisibleSecond=null;} }
function currentTimerMs(){if(timerInfinite)return Infinity;if(timerBaseMs===null)return null;return Math.max(0,timerBaseMs-(performance.now()-timerBasePerf));}
function formatGameClock(seconds){if(typeof seconds!=='number'||seconds<0)return'--:--';const total=Math.floor(seconds);const m=Math.floor(total/60);const s=total%60;return `${m}:${String(s).padStart(2,'0')}`;}
function isGameStatusPhase(){return state&&!state.isInChampSelect&&(state.phase==='GameStart'||state.phase==='InProgress'||state.isGameClientRunning||state.isGameLoaded);}
function updateTimerDisplay(){if(isGameStatusPhase()){timerEl.classList.add('game');if(state.isGameLoaded){timerEl.textContent=formatGameClock(state.gameTimeSeconds);timerLabelEl.textContent='game time';}else if(state.isGameClientRunning){timerEl.textContent='LOAD';timerLabelEl.textContent='loading';}else{timerEl.textContent='START';timerLabelEl.textContent='game';}lastVisibleSecond=null;return;}timerEl.classList.remove('game');timerLabelEl.textContent='seconds';const ms=currentTimerMs();if(ms===Infinity){timerEl.textContent='∞';lastVisibleSecond=null;return;}if(ms===null){timerEl.textContent='--';lastVisibleSecond=null;return;}const raw=Math.max(0,Math.ceil(ms/1000));const visible=lastVisibleSecond===null?raw:Math.min(lastVisibleSecond,raw);lastVisibleSecond=visible;timerEl.textContent=String(visible);}
function updateGameStatus(){if(!state){gameStatusEl.textContent='Game Waiting';gameStatusEl.className='turn-pill waiting';return;}if(state.isGameLoaded){gameStatusEl.textContent=`In Game ${formatGameClock(state.gameTimeSeconds)}`;gameStatusEl.className='turn-pill ingame';return;}if(state.phase==='GameStart'||state.phase==='InProgress'||state.isGameClientRunning){gameStatusEl.textContent=state.isGameClientRunning?'Loading Screen':'Starting Game';gameStatusEl.className='turn-pill loading';return;}gameStatusEl.textContent='Game Waiting';gameStatusEl.className='turn-pill waiting';}
function applyPhaseTransitionAlert(n){const old=previousPhase;previousPhase=n.phase;if(n.phase==='ChampSelect'&&old&&old!=='ChampSelect')showAcceptedAlert();}
function showDisconnectedState(){phaseEl.textContent='Disconnected';messageEl.textContent='Disconnected from Remote Pick server.';turnEl.textContent='Offline';turnEl.className='turn-pill waiting';gameStatusEl.textContent='Game Unknown';gameStatusEl.className='turn-pill waiting';timerBaseMs=null;lastVisibleSecond=null;updateTimerDisplay();leaveButtonEl.disabled=true;}
function updateStatusOnly(){if(!state)return;const canPick=state.canPick||(state.isMyTurn&&state.actionType==='pick');const canBan=state.canBan||(state.isMyTurn&&state.actionType==='ban');phaseEl.textContent=`${state.phaseLabel||state.phase||'Unknown'}${state.champSelectPhase?' · '+state.champSelectPhase:''}`;messageEl.textContent=state.message||`Phase: ${state.phase}`;updateTimerDisplay();updateGameStatus();turnEl.textContent=canBan?'Your ban turn':canPick?'Your pick turn':state.isInChampSelect?'Hover intent available':'Waiting';turnEl.className=canBan?'turn-pill ban':canPick?'turn-pill':'turn-pill waiting';leaveButtonEl.disabled=!state.canLeave;leaveButtonEl.textContent=state.phase==='Matchmaking'?'Cancel Queue':state.phase==='ReadyCheck'?'Decline':state.phase==='ChampSelect'?'Dodge':'Leave Lobby';}
function renderAll(){if(!state)return;updateStatusOnly();renderLoadout(desktopLoadoutEl);renderLoadout(mobileLoadoutEl);renderChampionGrid();renderRail(pickedListEl,(state.champions||[]).filter(c=>c.isPicked),'Picked');renderRail(bannedListEl,(state.champions||[]).filter(c=>c.isBanned),'Banned',true);}
function renderChampionGrid(){if(!state)return;const champions=state.champions||[];const filtered=champions.filter(c=>c.name.toLowerCase().includes(query));const canPick=state.canPick||(state.isMyTurn&&state.actionType==='pick');const canBan=state.canBan||(state.isMyTurn&&state.actionType==='ban');countEl.textContent=filtered.length;championsEl.innerHTML='';for(const c of filtered){const disabled=c.isDisabled;const badge=c.isBanned?'<span class="badge banned">Banned</span>':c.isPicked?'<span class="badge">Picked</span>':c.isIntent?'<span class="badge intent">Intent</span>':c.isSelected?'<span class="badge">Selected</span>':'';const card=document.createElement('article');card.className='champion '+(disabled?'disabled':'available');card.innerHTML=`<div class="portrait">${badge}<img src="${escapeAttr(c.imageUrl)}" alt=""></div><div class="champion-name">${escapeHtml(c.name)}</div><div class="champion-actions"><button class="hover" ${disabled||!state.isInChampSelect?'disabled':''}>Hover</button><button class="${canBan?'ban':'lock'}" ${disabled||(!canPick&&!canBan)?'disabled':''}>${canBan?'Ban':'Lock'}</button></div>`;card.querySelector('.hover').onclick=()=>hoverChampion(c.id,c.name);card.querySelector('.lock, .ban').onclick=()=>canBan?banChampion(c.id,c.name):pickChampion(c.id,c.name);championsEl.appendChild(card);}}
function renderLoadout(target){if(!target||!state)return;const spells=state.summonerSpells||[];const spell1=spells.find(s=>s.isSpell1)||null;const spell2=spells.find(s=>s.isSpell2)||null;target.innerHTML=`<h2 class="section-title">Loadout</h2><div class="loadout"><div class="spell-slots">${renderSpellButton(1,spell1)}${renderSpellButton(2,spell2)}</div>${renderRecommendedRunes()}${renderSavedRunePages()}</div>`;target.querySelector('[data-slot="1"]')?.addEventListener('click',()=>openSpellModal(1));target.querySelector('[data-slot="2"]')?.addEventListener('click',()=>openSpellModal(2));for(const b of target.querySelectorAll('[data-rune-page-id]'))b.onclick=()=>selectRunePage(Number(b.dataset.runePageId));for(const b of target.querySelectorAll('[data-recommended-rune-index]'))b.onclick=()=>selectRecommendedRunePage(Number(b.dataset.recommendedRuneIndex));}
function renderRecommendedRunes(){const pages=state.recommendedRunePages||[];if(!pages.length)return `<div class="rune-block"><div class="rune-current">Recommended runes unavailable</div></div>`;return `<div class="rune-block"><div class="rune-current">Recommended by Riot</div><div class="rune-list">${pages.map(p=>`<button class="rune-button recommended" data-recommended-rune-index="${p.index}" ${p.canApply===false?'disabled':''}><span>${escapeHtml(p.name||('Recommended '+(p.index+1)))}<small>${escapeHtml(p.subtitle||`${p.selectedPerkIds?.length||0} perks`)}</small></span><em>Apply</em></button>`).join('')}</div></div>`;}
function renderSavedRunePages(){const pages=state.runePages||[];const current=state.currentRunePage||pages.find(p=>p.isCurrent)||null;if(!pages.length)return `<div class="rune-block"><div class="rune-current">Saved runes unavailable</div></div>`;return `<div class="rune-block"><div class="rune-current">Saved: ${current?escapeHtml(current.name):'Choose page'}</div><div class="rune-list">${pages.map(p=>`<button class="rune-button ${p.isCurrent?'current':''}" data-rune-page-id="${p.id}" ${p.isCurrent?'disabled':''}><span>${escapeHtml(p.name)}</span><em>${p.isCurrent?'Active':'Set'}</em></button>`).join('')}</div></div>`;}
function renderSpellButton(slot,spell){return `<button class="spell-button" data-slot="${slot}">${spell?`<img src="${escapeAttr(spell.imageUrl)}" alt="">`:'<span></span>'}<div><span>Spell ${slot}</span><strong>${spell?escapeHtml(spell.name):'Choose'}</strong></div></button>`;}
function renderRail(el,champions,label,banned=false){el.innerHTML='';if(!champions.length){el.innerHTML=`<div class="rail-empty">No ${label.toLowerCase()} champions yet.</div>`;return;}for(const c of champions.slice(0,10)){const row=document.createElement('div');row.className='mini-card'+(banned?' banned':'');row.innerHTML=`<img src="${escapeAttr(c.imageUrl)}" alt=""><div><div class="name">${escapeHtml(c.name)}</div><div class="sub">${label}</div></div>`;el.appendChild(row);}}
async function hoverChampion(id,name){await runChampionAction('/api/hover',id,`${name} intent sent.`);} async function pickChampion(id,name){if(!confirm(`Lock in ${name}?`))return;await runChampionAction('/api/pick',id,'Champion locked in.');} async function banChampion(id,name){if(!confirm(`Ban ${name}?`))return;await runChampionAction('/api/ban',id,'Champion banned.');}
async function runChampionAction(url,championId,fallback){const r=await fetch(url,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({championId})});const result=await r.json();showToast(result.message||(result.success?fallback:'Action failed.'));await loadState();await loadTimer();}
function openSpellModal(slot){spellModalTitleEl.textContent=`Choose summoner spell ${slot}`;spellGridEl.innerHTML='';const spells=state.summonerSpells||[];const available=spells.filter(s=>s.isAvailable);const show=available.length?available:spells;for(const s of show){const b=document.createElement('button');b.className='spell-option';b.innerHTML=`<img src="${escapeAttr(s.imageUrl)}" alt=""><div><strong>${escapeHtml(s.name)}</strong></div>`;b.onclick=()=>selectSpell(slot,s.id);spellGridEl.appendChild(b);}spellModalEl.style.display='block';}
function closeSpellModal(){spellModalEl.style.display='none';}
async function selectSpell(slot,spellId){const r=await fetch('/api/spell',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({slot,spellId})});const result=await r.json();showToast(result.message||(result.success?'Spell updated.':'Spell change failed.'));closeSpellModal();await loadState();}
async function selectRunePage(pageId){const r=await fetch('/api/rune-page',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({pageId})});const result=await r.json();showToast(result.message||(result.success?'Rune page selected.':'Rune page change failed.'));await loadState();}
async function selectRecommendedRunePage(index){const r=await fetch('/api/recommended-rune-page',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({index})});const result=await r.json();showToast(result.message||(result.success?'Recommended runes applied.':'Recommended runes failed.'));await loadState();}
async function leaveLobby(){const label=leaveButtonEl.textContent||'Leave Lobby';if(!confirm(`${label}?`))return;const r=await fetch('/api/leave',{method:'POST'});const result=await r.json();showToast(result.message||(result.success?'Leave request sent.':'Leave failed.'));await loadState();await loadTimer();}
function escapeHtml(text){return String(text||'').replace(/[&<>'\"]/g,ch=>({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#039;','\"':'&quot;'}[ch]));}
function escapeAttr(text){return escapeHtml(text).replace(/`/g,'&#096;');}
loadState();loadTimer();setInterval(loadTimer,250);setInterval(loadState,2500);setInterval(updateTimerDisplay,100);
</script>
</body>
</html>
""";
    }
}
