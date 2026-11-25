const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright-core');

const html = fs.readFileSync(path.join(__dirname, '../RiotAutoLogin/Services/RemotePickPage.html'), 'utf8');
const champions = Array.from({length: 90}, (_, i) => ({
  id: i + 1, name: i === 0 ? 'Annie' : i === 1 ? 'Ahri' : `Champion ${i + 1}`,
  positions: i === 0 ? ['mid', 'support'] : ['top'],
  imageUrl: 'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=', isDisabled: i === 1
}));
const lobby = {phase: 'Lobby', phaseLabel: 'Lobby', queueName: 'Normal Draft', canStartLobby: true, partySize: 3,
  queueId: 400, canChangeQueue: true, lobbyMembers: [{playerName: 'My Player', tagLine: 'EUW', isLocalPlayer: true, isLeader: true}, {playerName: 'Friend', firstPosition: 'MIDDLE'}, {playerName: '<friend>'}], queues: [{id: 400, name: 'Normal Draft', isAvailable: true}, {id: 420, name: 'Ranked Solo / Duo', isAvailable: true}, {id: 2400, name: 'ARAM Mayhem', isAvailable: true}, {id: 9876, name: 'Arena', isAvailable: false, unavailableReason: 'Unavailable'}], isInChampSelect: false, canLeave: true, phaseElapsedSeconds: -1, timeLeftInPhaseMs: -1};
const draft = {...lobby, phase: 'ChampSelect', draftId: 'match-101', swaps: [], phaseLabel: 'Pick Phase', isInChampSelect: true, canStartLobby: false,
  champions, canPick: true, actionType: 'pick', actionId: '42', timeLeftInPhaseMs: 21000,
  allySlots: [{cellId: 0, isLocalPlayer: true}], enemySlots: [{cellId: 5}]};

async function run() {
  const browser = await chromium.launch({...(process.env.REMOTE_PICK_CHROME ? {executablePath: process.env.REMOTE_PICK_CHROME} : {channel: 'chrome'}), headless: true});
  try {
    const page = await browser.newPage({viewport: {width: 390, height: 844}});
    page.setDefaultTimeout(6500);
    const errors = [], posts = [];
    page.on('pageerror', e => errors.push(e.message));
    let state = {...lobby}, tick = 0, delayFull = null, skinSelected = 1000, failSkin = false;
    await page.route('https://remote-pick.test/**', async route => {
      const url = new URL(route.request().url());
      if (url.pathname === '/') return route.fulfill({contentType: 'text/html', body: html});
      if (url.pathname === '/api/skins') return route.fulfill({json: {draftId:state.draftId,championId:state.selectedChampionId,selectedSkinId:skinSelected,canSelect:!!state.canSelectSkin,skins:[
        {id:1000,name:'Annie',isAvailable:true,imageUrl:champions[0].imageUrl,thumbnailUrl:champions[0].imageUrl},
        {id:1001,name:'Gothic Annie',isAvailable:true,imageUrl:champions[0].imageUrl,thumbnailUrl:champions[0].imageUrl}
      ]}});
      if (route.request().method() === 'POST') {
        assert.equal(route.request().headers()['x-remote-pick-request'], '1');
        posts.push(url.pathname);
        let result = {success: true};
        switch (url.pathname) {
          case '/api/skin': {
            const body = route.request().postDataJSON(); assert.equal(body.draftId,state.draftId); assert.equal(body.championId,1);
            if (failSkin) result = {success:false,message:'Skin change not confirmed.'};
            else { skinSelected = body.skinId; state.selectedSkinId = skinSelected; } break;
          }
          case '/api/swap': {
            const body = route.request().postDataJSON(); assert.equal(body.draftId,state.draftId);
            const swap = state.swaps.find(s => s.kind === body.kind && s.id === body.id && s.cellId === body.cellId);
            assert.ok(swap, 'A real contract must be sent');
            if (body.action === 'request') swap.state = 'SENT'; else state.swaps = state.swaps.filter(s => s !== swap);
            break;
          }
          case '/api/leave': assert.equal(route.request().postDataJSON().draftId,state.draftId); result = {success:false,message:'Dodge could not be confirmed.'}; break;
          case '/api/change-queue': state = {...state, queueId: route.request().postDataJSON().queueId}; break;
          case '/api/bench-swap': state = {...state, selectedChampionId: route.request().postDataJSON().championId, benchChampionIds: []}; break;
          case '/api/start-queue': state = {...lobby, phase: 'Matchmaking', canStartLobby: false, phaseElapsedSeconds: 93}; break;
          case '/api/accept-match': state = {...draft}; break;
          case '/api/hover':
          case '/api/ban-hover': {
            const id = route.request().postDataJSON().championId;
            if (state.canBan) { state.hoveredBanChampionId = id; state.banFallbackArmed = !state.isTimerInfinite; }
            else state.hoveredChampionId = id;
            result = {...result, confirmedChampionId: id, autoBanArmed: !!state.banFallbackArmed};
            break;
          }
          case '/api/pick': state = {...state, canPick: false, canSelectCard: false, actionType: '', selectedChampionId: route.request().postDataJSON().championId}; break;
          case '/api/ban': state = {...draft}; break;
          default: throw new Error('Unexpected action ' + url.pathname);
        }
        return route.fulfill({json: result});
      }
      const snapshot = {...state, liveSampleTickMs: ++tick};
      if (url.pathname === '/api/state' && delayFull) {
        const hold = delayFull; delayFull = null;
        await hold();
      }
      return route.fulfill({json: snapshot});
    });
    const byId = id => page.locator('#' + id);
    const paint = async () => { await page.evaluate(() => loadState()); };
    await page.goto('https://remote-pick.test/');
    await page.getByRole('button', {name: 'Start queue'}).waitFor();
    assert.equal(await byId('layout').isVisible(), false);
    assert.equal(await byId('timerBox').isVisible(), false);
    assert.equal(await byId('partyMembers').getByRole('listitem').count(), 3);
    await page.getByText('<friend>', {exact: true}).waitFor();
    assert.equal(await byId('queueSelect').locator('option[value="9876"]').isDisabled(), true);
    for (const [width,height] of [[390,844],[667,375],[932,430]]) {
      await page.setViewportSize({width,height});
      const layout = await page.evaluate(() => ({overflow: document.documentElement.scrollHeight - innerHeight,
        x: document.documentElement.scrollWidth - innerWidth, footer: document.querySelector('#roomControls').getBoundingClientRect().bottom <= innerHeight}));
      assert.ok(layout.overflow <= 1 && layout.x <= 1 && layout.footer, 'Lobby controls must remain reachable: '+JSON.stringify(layout));
    }
    await byId('queueSelect').selectOption('420'); await byId('changeQueue').click(); await byId('acceptConfirm').click();
    await page.waitForFunction(() => state.queueId === 420);
    state = {...lobby, canChangeQueue: false}; await paint();
    assert.equal(await byId('queueSelect').isDisabled(), true);
    state = {...lobby}; await paint();
    await byId('roomAction').click();
    await page.waitForFunction(() => document.querySelector('#timer').textContent.startsWith('1:3'));
    state = {...lobby, phase: 'ReadyCheck', phaseLabel: 'Match found', canStartLobby: false, canAcceptMatch: true};
    await paint();
    await page.getByRole('button', {name: 'Accept match'}).click();
    await byId('positionFilter').selectOption('mid');
    assert.equal(await byId('champions').locator('[data-select-id]').count(), 1);
    await byId('positionFilter').selectOption('support');
    assert.equal(await byId('champions').locator('[data-select-id]').count(), 1);
    await byId('positionFilter').selectOption('');
    await page.getByRole('button', {name: 'Preview Annie'}).click();
    await page.waitForFunction(() => document.querySelector('#hoverName').textContent === 'Annie');
    assert.equal(await page.getByRole('button', {name: /^Hover/}).count(), 0);
    assert.equal(await byId('champions').locator('[data-select-id="2"]').isDisabled(), true);

    for (const [width, height] of [[390, 844], [932, 430], [667, 375]]) {
      await page.setViewportSize({width, height});
      const geometry = await page.evaluate(() => {
        const grid = document.querySelector('#champions'), nav = document.querySelector('nav');
        const card = grid.firstElementChild, box = card.getBoundingClientRect();
        const name = card.querySelector('.champion-name').getBoundingClientRect();
        const cta = document.querySelector('#commitChampion').getBoundingClientRect();
        return {scrolls: grid.scrollHeight > grid.clientHeight, gridHeight: grid.clientHeight,
          labelFits: name.bottom <= box.bottom + 1, navInView: nav.getBoundingClientRect().bottom <= innerHeight,
          ctaInView: cta.height >= 44 && cta.bottom <= innerHeight, pageOverflow: document.documentElement.scrollHeight - innerHeight,
          horizontalOverflow: document.documentElement.scrollWidth - innerWidth};
      });
      assert.ok(geometry.scrolls && geometry.gridHeight >= 95 && geometry.labelFits && geometry.navInView && geometry.ctaInView &&
        geometry.pageOverflow <= 1 && geometry.horizontalOverflow <= 1, `${width}×${height}: ${JSON.stringify(geometry)}`);
      await page.getByRole('button', {name: 'Picks', exact: true}).click();
      assert.ok(await byId('allyPanel').isVisible() && !await byId('championView').isVisible());
      await page.getByRole('button', {name: 'Loadout', exact: true}).click();
      assert.ok(await byId('desktopLoadout').isVisible() && !await byId('allyPanel').isVisible());
      await page.getByRole('button', {name: 'Champions', exact: true}).click();
    }
    await page.getByRole('button', {name: 'Lock in Annie', exact: true}).click();
    await byId('acceptConfirm').click();
    await byId('commitChampion').waitFor({state: 'hidden'});
    state = {...draft, canPick: false, canBan: true, actionType: 'ban', actionId: '9', isTimerInfinite: true};
    await paint();
    await page.getByRole('button', {name: 'Set ban Annie'}).click();
    await page.getByText('Confirm ban manually', {exact: true}).waitFor();
    assert.equal(await byId('timer').textContent(), '∞');
    await page.setViewportSize({width: 1400, height: 850});
    await page.getByRole('button', {name: 'Ban Annie', exact: true}).click();
    await byId('acceptConfirm').click();
    await page.waitForFunction(() => document.querySelector('#phase').textContent === 'Your pick');

    let releaseFull, readStarted;
    const started = new Promise(resolve => { readStarted = resolve; });
    const gate = new Promise(resolve => { releaseFull = resolve; });
    delayFull = () => { readStarted(); return gate; };
    const slow = page.evaluate(() => loadState());
    await started;
    try {
      state = {...lobby, phase: 'Matchmaking', canStartLobby: false, phaseElapsedSeconds: 101, timerSampleAgeMs: 3000};
      await page.evaluate(() => loadTimer());
    } finally { releaseFull(); }
    await slow;
    await page.waitForFunction(() => document.querySelector('#phase').textContent === 'Finding match');
    assert.ok(['1:44','1:45'].includes(await byId('timer').textContent()), 'Slow full snapshot must not rewind the clock');
    assert.deepEqual(posts, ['/api/change-queue', '/api/start-queue', '/api/accept-match', '/api/hover', '/api/pick', '/api/ban-hover', '/api/ban']);
    state = {...draft, isRandomChampionMode: true, canSelectCard: true, cardChampionIds: [1,3], benchEnabled: true, benchChampionIds: [4], canReroll: false, champions: champions.slice(0,4).map(c => ({...c,isDisabled: c.id === 2}))};
    await paint();
    assert.equal(await byId('positionFilter').isDisabled(), true);
    assert.equal(await byId('champions').getByText('Card',{exact:true}).count(), 2);
    assert.equal(await byId('champions').getByText('Bench',{exact:true}).count(), 1);
    assert.equal(await byId('commitChampion').isVisible(), false);
    assert.equal(await byId('rerollButton').isVisible(), false);
    assert.equal(await page.getByRole('button', {name:'Preview Annie'}).count(), 0);
    const before = posts.length;
    await page.getByRole('button', {name:'Choose Champion 3',exact:true}).click();
    await page.waitForFunction(() => state.selectedChampionId === 3);
    assert.deepEqual(posts.slice(before), ['/api/pick']);
    await page.getByRole('button', {name:'Take Champion 4',exact:true}).click();
    await page.waitForFunction(() => state.selectedChampionId === 4);
    assert.deepEqual(posts.slice(before), ['/api/pick','/api/bench-swap']);
    assert.equal(await page.getByRole('button', {name:'Take Champion 4',exact:true}).count(), 0);
    fs.mkdirSync('build',{recursive:true});
    for (const [name,data] of [['lobby',lobby],['aram',state]]) {
      state = {...data}; await page.setViewportSize({width:390,height:844}); await paint();
      await page.screenshot({path: `build/remote-${name}-preview.png`});
    }
    state = {...draft,canPick:false,actionType:'',selectedChampionId:1,selectedSkinId:1000,canSelectSkin:true,
      allySlots:[{cellId:0,isLocalPlayer:true,assignedPosition:'MIDDLE'},{cellId:1,playerName:'Friend',assignedPosition:'TOP'}],
      swaps:[{id:'51',kind:'position',cellId:1,state:'RECEIVED'},{id:'52',kind:'pick-order',cellId:1,state:'AVAILABLE'}]};
    await paint();
    assert.equal(await byId('swapAlerts').isVisible(),true);
    await byId('swapAlerts').getByRole('button',{name:'Accept · role · Friend',exact:true}).click();
    await page.waitForFunction(() => !state.swaps.some(s => s.kind === 'position'));
    await page.getByRole('button',{name:'Picks',exact:true}).click();
    await page.getByRole('button',{name:'Swap pick order · pick order · Friend',exact:true}).click();
    await byId('swapAlerts').getByRole('button',{name:'Cancel · pick order · Friend',exact:true}).waitFor();
    await byId('swapAlerts').getByRole('button',{name:'Cancel · pick order · Friend',exact:true}).click();
    await page.waitForFunction(() => state.swaps.length === 0);
    await page.getByRole('button',{name:'Loadout',exact:true}).click();
    await page.getByRole('button',{name:'Choose skin',exact:true}).click();
    await page.getByRole('button',{name:'Preview Gothic Annie',exact:true}).click();
    assert.equal(await byId('skinCaption').textContent(),'Gothic Annie');
    assert.equal(await byId('skinSummary').locator('strong').textContent(),'Annie','Preview is not the actual client selection');
    for (const [width,height] of [[390,844],[932,430],[667,375]]) {
      await page.setViewportSize({width,height});
      const box = await page.evaluate(() => { const dialog = document.querySelector('.skin-modal').getBoundingClientRect(), apply = document.querySelector('#applySkin').getBoundingClientRect();
        return {top:dialog.top,bottom:dialog.bottom,applyBottom:apply.bottom,applyHeight:apply.height,x:document.documentElement.scrollWidth-innerWidth}; });
      assert.ok(box.top >= 0 && box.bottom <= height + 1 && box.applyBottom <= height && box.applyHeight >= 44 && box.x <= 1, 'Skin gallery fits '+JSON.stringify(box));
    }
    failSkin = true; await byId('applySkin').click();
    await page.getByText('Skin change not confirmed.',{exact:true}).waitFor();
    await page.waitForFunction(() => !actionPending);
    assert.equal(await byId('skinSummary').locator('strong').textContent(),'Annie');
    failSkin = false; await byId('applySkin').click();
    await page.waitForFunction(() => skinGallery?.selectedSkinId === 1001);
    assert.equal(await byId('skinSummary').locator('strong').textContent(),'Gothic Annie');
    assert.equal(await byId('skinStatus').textContent(),'Selected in client');
    await page.emulateMedia({reducedMotion:'reduce'});
    const duration = await page.locator('.skin-art').first().evaluate(img => getComputedStyle(img).transitionDuration);
    assert.equal(duration,'0s'); await page.emulateMedia({reducedMotion:'no-preference'});
    await page.setViewportSize({width:390,height:844});
    await page.screenshot({path:'build/remote-skins-preview.png'});
    await byId('closeSkinModal').click();
    await page.getByRole('button',{name:'Dodge',exact:true}).click(); await byId('acceptConfirm').click();
    await page.getByText('Dodge could not be confirmed.',{exact:true}).waitFor();
    assert.equal(await byId('layout').isVisible(),true,'A failed dodge must not invent a return to lobby');
    await page.waitForFunction(() => !actionPending);
    const leaves = posts.filter(p => p === '/api/leave').length;
    await page.getByRole('button',{name:'Dodge',exact:true}).click();
    state = {...state,draftId:'different-match'}; await paint();
    await byId('acceptConfirm').click();
    assert.equal(posts.filter(p => p === '/api/leave').length,leaves,'Old confirmation must not dodge another draft');
    state = {...lobby}; await paint();
    await byId('queueSelect').selectOption('2400'); await byId('changeQueue').click(); await byId('acceptConfirm').click();
    await page.waitForFunction(() => state.queueId === 2400);
    assert.deepEqual(posts.slice(-7),['/api/swap','/api/swap','/api/swap','/api/skin','/api/skin','/api/leave','/api/change-queue']);
    assert.deepEqual(errors, []);
    console.log('Remote Pick: party roster, queue authority, ARAM cards and bench, lobby to draft, portrait and landscape, delayed clocks, skins with confirmed selection, role/order swaps and failed/stale dodge confirmed.');
  } finally { await browser.close(); }
}
run().catch(err => { console.error(err); process.exitCode = 1; });
