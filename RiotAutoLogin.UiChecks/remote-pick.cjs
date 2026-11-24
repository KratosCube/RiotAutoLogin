const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { chromium } = require('playwright-core');

const html = fs.readFileSync(path.join(__dirname, '../RiotAutoLogin/Services/RemotePickPage.html'), 'utf8');
const champions = Array.from({length: 90}, (_, i) => ({
  id: i + 1, name: i === 0 ? 'Annie' : i === 1 ? 'Ahri' : `Champion ${i + 1}`,
  imageUrl: 'data:image/gif;base64,R0lGODlhAQABAAD/ACwAAAAAAQABAAACADs=', isDisabled: i === 1
}));
const lobby = {phase: 'Lobby', phaseLabel: 'Lobby', queueName: 'Normal Draft', canStartLobby: true, partySize: 3,
  isInChampSelect: false, canLeave: true, phaseElapsedSeconds: -1, timeLeftInPhaseMs: -1};
const draft = {...lobby, phase: 'ChampSelect', phaseLabel: 'Pick Phase', isInChampSelect: true, canStartLobby: false,
  champions, canPick: true, actionType: 'pick', actionId: '42', timeLeftInPhaseMs: 21000,
  allySlots: [{cellId: 0, isLocalPlayer: true}], enemySlots: [{cellId: 5}]};

async function run() {
  const browser = await chromium.launch({channel: 'chrome', headless: true});
  try {
    const page = await browser.newPage({viewport: {width: 390, height: 844}});
    page.setDefaultTimeout(6500);
    const errors = [], posts = [];
    page.on('pageerror', e => errors.push(e.message));
    let state = {...lobby}, tick = 0, delayFull = null;
    await page.route('https://remote-pick.test/**', async route => {
      const url = new URL(route.request().url());
      if (url.pathname === '/') return route.fulfill({contentType: 'text/html', body: html});
      if (route.request().method() === 'POST') {
        assert.equal(route.request().headers()['x-remote-pick-request'], '1');
        posts.push(url.pathname);
        let result = {success: true};
        switch (url.pathname) {
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
          case '/api/pick': state = {...state, canPick: false, actionType: '', selectedChampionId: 1}; break;
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
    await byId('roomAction').click();
    await page.waitForFunction(() => document.querySelector('#timer').textContent.startsWith('1:3'));
    state = {...lobby, phase: 'ReadyCheck', phaseLabel: 'Match found', canStartLobby: false, canAcceptMatch: true};
    await paint();
    await page.getByRole('button', {name: 'Accept match'}).click();
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
    assert.deepEqual(posts, ['/api/start-queue', '/api/accept-match', '/api/hover', '/api/pick', '/api/ban-hover', '/api/ban']);
    assert.deepEqual(errors, []);
    console.log('Remote Pick: lobby to draft, portrait and landscape, delayed clock samples, pick and ban confirmed.');
  } finally { await browser.close(); }
}
run().catch(err => { console.error(err); process.exitCode = 1; });
