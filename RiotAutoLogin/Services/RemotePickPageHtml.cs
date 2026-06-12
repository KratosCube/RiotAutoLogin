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
    :root {
      color-scheme: dark;
      --bg: #03070d;
      --panel: rgba(8, 16, 26, .82);
      --panel-strong: rgba(7, 12, 20, .94);
      --line: rgba(196, 161, 92, .35);
      --gold: #c8aa6e;
      --gold-strong: #f0d58a;
      --cyan: #0ac8b9;
      --red: #a13d45;
      --blue: #1b70a6;
      --text: #f3ead7;
      --muted: #9aa6b2;
      font-family: Georgia, 'Times New Roman', serif;
    }

    * { box-sizing: border-box; }
    html, body { min-height: 100%; }
    body {
      margin: 0;
      color: var(--text);
      background:
        radial-gradient(circle at 50% 8%, rgba(50, 91, 116, .34), transparent 31rem),
        radial-gradient(circle at 80% 25%, rgba(190, 135, 54, .16), transparent 28rem),
        linear-gradient(135deg, #02060b 0%, #0b1018 47%, #02050a 100%);
      overflow-x: hidden;
    }

    body::before {
      content: '';
      position: fixed;
      inset: 0;
      pointer-events: none;
      background:
        linear-gradient(90deg, rgba(200,170,110,.05), transparent 20%, transparent 80%, rgba(200,170,110,.05)),
        repeating-linear-gradient(90deg, transparent 0 90px, rgba(255,255,255,.018) 91px 92px);
      opacity: .9;
    }

    .app { min-height: 100vh; padding: 14px; position: relative; }
    .topbar {
      display: grid;
      grid-template-columns: 1fr auto 1fr;
      align-items: center;
      gap: 10px;
      padding: 12px 10px 16px;
      border-bottom: 1px solid var(--line);
      position: sticky;
      top: 0;
      z-index: 10;
      backdrop-filter: blur(10px);
      background: linear-gradient(180deg, rgba(3,7,13,.96), rgba(3,7,13,.78));
    }

    .side-title { color: var(--muted); font: 700 11px system-ui, sans-serif; text-transform: uppercase; letter-spacing: .16em; }
    .side-title.right { text-align: right; }
    h1 {
      margin: 0;
      text-align: center;
      letter-spacing: .08em;
      font-size: clamp(22px, 6vw, 34px);
      color: var(--gold-strong);
      text-shadow: 0 2px 18px rgba(240,213,138,.22);
    }

    .status-card {
      margin: 14px auto;
      max-width: 980px;
      padding: 14px;
      border: 1px solid var(--line);
      background: linear-gradient(180deg, rgba(18, 31, 44, .9), rgba(5, 11, 19, .9));
      box-shadow: inset 0 0 35px rgba(200,170,110,.04), 0 18px 45px rgba(0,0,0,.35);
      position: relative;
      overflow: hidden;
    }

    .status-card::before, .status-card::after {
      content: '';
      position: absolute;
      top: 0;
      width: 32%;
      height: 2px;
      background: linear-gradient(90deg, transparent, var(--gold), transparent);
      opacity: .8;
    }
    .status-card::before { left: 0; }
    .status-card::after { right: 0; }

    .phase { color: var(--cyan); font: 800 12px system-ui, sans-serif; letter-spacing: .18em; text-transform: uppercase; }
    .message { margin-top: 6px; font-size: 18px; line-height: 1.25; }
    .turn-pill {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      margin-top: 12px;
      padding: 8px 12px;
      border: 1px solid rgba(10,200,185,.45);
      color: #d7fffb;
      background: rgba(10,200,185,.09);
      font: 800 12px system-ui, sans-serif;
      text-transform: uppercase;
      letter-spacing: .12em;
    }
    .turn-pill.waiting { border-color: rgba(200,170,110,.33); color: var(--gold); background: rgba(200,170,110,.08); }

    .layout {
      display: grid;
      grid-template-columns: 250px minmax(0, 1fr) 250px;
      gap: 14px;
      max-width: 1180px;
      margin: 0 auto;
      align-items: start;
    }

    .panel {
      border: 1px solid rgba(200,170,110,.27);
      background: var(--panel);
      box-shadow: inset 0 0 30px rgba(0,0,0,.28);
      min-height: 120px;
    }
    .panel h2 {
      margin: 0;
      padding: 12px 14px;
      border-bottom: 1px solid rgba(200,170,110,.18);
      color: var(--gold);
      font: 800 12px system-ui, sans-serif;
      text-transform: uppercase;
      letter-spacing: .16em;
    }

    .rail-list { padding: 10px; display: grid; gap: 9px; }
    .rail-empty { color: var(--muted); font: 13px system-ui, sans-serif; padding: 12px; }
    .mini-card {
      display: flex;
      align-items: center;
      gap: 10px;
      padding: 8px;
      background: rgba(255,255,255,.035);
      border: 1px solid rgba(255,255,255,.055);
      min-height: 54px;
    }
    .mini-card img { width: 42px; height: 42px; object-fit: cover; border: 1px solid rgba(200,170,110,.45); }
    .mini-card .name { font: 700 13px system-ui, sans-serif; }
    .mini-card .sub { color: var(--muted); font: 11px system-ui, sans-serif; margin-top: 2px; }
    .mini-card.banned img { filter: grayscale(1) brightness(.6); border-color: rgba(161,61,69,.8); }

    .center { min-width: 0; }
    .search-wrap {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: 10px;
      margin-bottom: 12px;
    }
    input {
      width: 100%;
      border: 1px solid rgba(200,170,110,.32);
      background: rgba(5, 10, 18, .88);
      color: var(--text);
      padding: 14px 14px;
      font: 16px system-ui, sans-serif;
      outline: none;
    }
    input:focus { border-color: rgba(10,200,185,.75); box-shadow: 0 0 0 2px rgba(10,200,185,.12); }
    .count {
      display: grid;
      place-items: center;
      min-width: 72px;
      border: 1px solid rgba(200,170,110,.32);
      color: var(--gold);
      background: rgba(5, 10, 18, .88);
      font: 800 12px system-ui, sans-serif;
      text-transform: uppercase;
    }

    .champion-grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(118px, 1fr));
      gap: 10px;
    }
    .champion {
      position: relative;
      min-height: 150px;
      overflow: hidden;
      border: 1px solid rgba(200,170,110,.34);
      background: #111;
      color: var(--text);
      padding: 0;
      text-align: left;
      box-shadow: 0 8px 24px rgba(0,0,0,.25);
      cursor: pointer;
    }
    .champion:disabled { cursor: not-allowed; }
    .champion img {
      width: 100%;
      height: 112px;
      object-fit: cover;
      display: block;
      transition: transform .18s ease, filter .18s ease;
      background: #080d14;
    }
    .champion:not(:disabled):active img { transform: scale(1.04); }
    .champion:not(:disabled):hover { border-color: var(--gold-strong); }
    .champion:not(:disabled):hover img { filter: brightness(1.12); }
    .champion-name {
      padding: 9px 10px 10px;
      min-height: 38px;
      background: linear-gradient(180deg, rgba(8, 14, 23, .92), rgba(4, 7, 12, .98));
      font: 800 12px system-ui, sans-serif;
      text-transform: uppercase;
      letter-spacing: .04em;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
    }
    .champion.disabled img { filter: grayscale(1) brightness(.38); }
    .champion.disabled::after {
      content: '';
      position: absolute;
      inset: 0;
      background: linear-gradient(135deg, transparent 46%, rgba(161,61,69,.95) 48%, rgba(161,61,69,.95) 52%, transparent 54%);
      pointer-events: none;
    }
    .badge {
      position: absolute;
      top: 8px;
      left: 8px;
      padding: 5px 7px;
      background: rgba(5, 8, 12, .82);
      border: 1px solid rgba(200,170,110,.4);
      color: var(--gold-strong);
      font: 800 10px system-ui, sans-serif;
      letter-spacing: .08em;
      text-transform: uppercase;
    }
    .badge.banned { color: #ffb3ba; border-color: rgba(161,61,69,.9); }

    .loadout {
      margin-top: 14px;
      padding: 12px;
      color: var(--muted);
      font: 13px system-ui, sans-serif;
    }
    .rune-placeholder {
      margin-top: 10px;
      padding: 12px;
      border: 1px dashed rgba(200,170,110,.28);
      color: var(--gold);
      text-align: center;
      background: rgba(200,170,110,.05);
    }

    .toast {
      position: fixed;
      left: 14px;
      right: 14px;
      bottom: 14px;
      z-index: 20;
      padding: 14px 16px;
      border: 1px solid rgba(200,170,110,.42);
      background: rgba(7, 13, 21, .96);
      color: var(--text);
      box-shadow: 0 16px 40px rgba(0,0,0,.45);
      display: none;
      font: 14px system-ui, sans-serif;
    }

    @media (max-width: 900px) {
      .layout { grid-template-columns: 1fr; }
      .panel.side { display: none; }
      .topbar { grid-template-columns: 1fr; }
      .side-title { display: none; }
      .champion-grid { grid-template-columns: repeat(auto-fill, minmax(92px, 1fr)); gap: 8px; }
      .champion { min-height: 128px; }
      .champion img { height: 92px; }
      .champion-name { font-size: 11px; padding: 8px; }
      .message { font-size: 16px; }
    }
  </style>
</head>
<body>
  <div class="app">
    <div class="topbar">
      <div class="side-title">Ally picks</div>
      <h1>CHOOSE YOUR CHAMPION</h1>
      <div class="side-title right">Enemy picks</div>
    </div>

    <section class="status-card">
      <div id="phase" class="phase">Connecting</div>
      <div id="message" class="message">Connecting to RiotAutoLogin Remote Pick...</div>
      <div id="turn" class="turn-pill waiting">Waiting</div>
    </section>

    <section class="layout">
      <aside class="panel side">
        <h2>Picked</h2>
        <div id="pickedList" class="rail-list"></div>
      </aside>

      <main class="center">
        <div class="search-wrap">
          <input id="search" placeholder="Search champion..." autocomplete="off">
          <div id="count" class="count">0</div>
        </div>
        <div id="champions" class="champion-grid"></div>
      </main>

      <aside class="panel side">
        <h2>Banned</h2>
        <div id="bannedList" class="rail-list"></div>
        <div class="loadout">
          <h2 style="margin: 8px -12px 0;">Loadout</h2>
          <div class="rune-placeholder">Runes coming later</div>
        </div>
      </aside>
    </section>
  </div>

  <div id="toast" class="toast"></div>

  <script>
    let state = null;
    let query = '';
    const championsEl = document.getElementById('champions');
    const pickedListEl = document.getElementById('pickedList');
    const bannedListEl = document.getElementById('bannedList');
    const phaseEl = document.getElementById('phase');
    const messageEl = document.getElementById('message');
    const turnEl = document.getElementById('turn');
    const searchEl = document.getElementById('search');
    const countEl = document.getElementById('count');
    const toastEl = document.getElementById('toast');

    searchEl.addEventListener('input', () => {
      query = searchEl.value.trim().toLowerCase();
      render();
    });

    function showToast(message) {
      toastEl.textContent = message;
      toastEl.style.display = 'block';
      clearTimeout(showToast.timer);
      showToast.timer = setTimeout(() => toastEl.style.display = 'none', 2600);
    }

    async function loadState() {
      try {
        const response = await fetch('/api/state', { cache: 'no-store' });
        state = await response.json();
        render();
      } catch (error) {
        phaseEl.textContent = 'Disconnected';
        messageEl.textContent = 'Disconnected from Remote Pick server.';
        turnEl.textContent = 'Offline';
        turnEl.className = 'turn-pill waiting';
      }
    }

    function render() {
      if (!state) return;

      phaseEl.textContent = state.phase || 'Unknown';
      messageEl.textContent = state.message || `Phase: ${state.phase}`;
      const canPick = state.isMyTurn && state.actionType === 'pick';
      turnEl.textContent = canPick ? 'Your pick turn' : state.isInChampSelect ? 'Watching champ select' : 'Waiting';
      turnEl.className = canPick ? 'turn-pill' : 'turn-pill waiting';

      const filtered = state.champions.filter(champion => champion.name.toLowerCase().includes(query));
      countEl.textContent = filtered.length;
      championsEl.innerHTML = '';

      for (const champion of filtered) {
        const button = document.createElement('button');
        button.className = 'champion' + (champion.isDisabled ? ' disabled' : '');
        button.disabled = champion.isDisabled || !canPick;
        const badge = champion.isBanned ? '<span class="badge banned">Banned</span>' : champion.isPicked ? '<span class="badge">Picked</span>' : '';
        button.innerHTML = `${badge}<img src="${escapeAttr(champion.imageUrl)}" alt=""><div class="champion-name">${escapeHtml(champion.name)}</div>`;
        button.onclick = () => pick(champion.id, champion.name);
        championsEl.appendChild(button);
      }

      renderRail(pickedListEl, state.champions.filter(c => c.isPicked), 'Picked');
      renderRail(bannedListEl, state.champions.filter(c => c.isBanned), 'Banned', true);
    }

    function renderRail(element, champions, label, banned = false) {
      element.innerHTML = '';
      if (!champions.length) {
        element.innerHTML = `<div class="rail-empty">No ${label.toLowerCase()} champions yet.</div>`;
        return;
      }

      for (const champion of champions.slice(0, 10)) {
        const row = document.createElement('div');
        row.className = 'mini-card' + (banned ? ' banned' : '');
        row.innerHTML = `<img src="${escapeAttr(champion.imageUrl)}" alt=""><div><div class="name">${escapeHtml(champion.name)}</div><div class="sub">${label}</div></div>`;
        element.appendChild(row);
      }
    }

    async function pick(championId, championName) {
      if (!confirm(`Lock in ${championName}?`)) return;

      const response = await fetch('/api/pick', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ championId })
      });
      const result = await response.json();
      showToast(result.message || (result.success ? 'Champion picked.' : 'Pick failed.'));
      await loadState();
    }

    function escapeHtml(text) {
      return String(text || '').replace(/[&<>'"]/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', "'": '&#039;', '"': '&quot;' }[char]));
    }

    function escapeAttr(text) {
      return escapeHtml(text).replace(/`/g, '&#096;');
    }

    loadState();
    setInterval(loadState, 800);
  </script>
</body>
</html>
""";
    }
}
