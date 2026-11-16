// Isolated Chromium verification. All /api calls are intercepted; no real account or database is used.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdir, writeFile } from 'node:fs/promises';
import { existsSync } from 'node:fs';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const origin = process.env.WOODFLOW_TEST_BASE_URL ?? 'https://localhost:4300';
assert.equal(new URL(origin).protocol, 'https:');
const browser = process.env.BROWSER_PATH ?? (process.platform === 'win32'
  ? 'C:/Program Files/Google/Chrome/Application/chrome.exe' : '/usr/bin/chromium');
if (!existsSync(browser)) throw new Error('Set BROWSER_PATH to a local Chromium browser.');
const profile = resolve('.angular/browser-auth-profile');
const output = resolve('.angular/verification');
await mkdir(output, { recursive: true });
const child = spawn(browser, [
  '--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--remote-debugging-port=0', '--user-data-dir=' + profile, '--window-size=1440,900', 'about:blank',
], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });

class Cdp {
  constructor(url) {
    this.next = 0; this.pending = new Map(); this.handlers = new Map();
    this.socket = new WebSocket(url);
    this.ready = new Promise((resolve, reject) => {
      this.socket.addEventListener('open', resolve, { once: true });
      this.socket.addEventListener('error', reject, { once: true });
    });
    this.socket.addEventListener('message', ({ data }) => {
      const result = JSON.parse(String(data));
      if (result.id) {
        const entry = this.pending.get(result.id);
        this.pending.delete(result.id);
        if (result.error) entry?.reject(new Error(result.error.message));
        else entry?.resolve(result.result);
      } else for (const handler of this.handlers.get(result.method) ?? []) handler(result.params);
    });
  }
  on(name, handler) { this.handlers.set(name, [...(this.handlers.get(name) ?? []), handler]); }
  async send(method, params = {}) {
    await this.ready;
    const id = ++this.next;
    const result = new Promise((resolve, reject) => this.pending.set(id, { resolve, reject }));
    this.socket.send(JSON.stringify({ id, method, params }));
    return result;
  }
  close() { this.socket.close(); }
}
let cdp;
let root;
try {
  const endpoint = await new Promise((resolve, reject) => {
    let text = '';
    const timeout = setTimeout(() => reject(new Error('Browser startup timed out.')), 20_000);
    child.once('error', reject);
    child.stderr.on('data', chunk => {
      text += String(chunk);
      const match = text.match(/DevTools listening on (ws:\/\/[^\s]+)/);
      if (match) { clearTimeout(timeout); resolve(match[1]); }
    });
  });
  root = new Cdp(endpoint);
  const { targetId } = await root.send('Target.createTarget', { url: 'about:blank' });
  const port = new URL(endpoint).port;
  const targets = await (await fetch('http://127.0.0.1:' + port + '/json/list')).json();
  cdp = new Cdp(targets.find(target => target.id === targetId).webSocketDebuggerUrl);
  await cdp.send('Page.enable');
  await cdp.send('Runtime.enable');
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
  await cdp.send('Fetch.enable', { patterns: [{ urlPattern: '*/api/*' }] });

  const account = { userId: 10, username: 'test_owner', fullName: 'Test Owner', role: 'OWNER' };
  let signedIn = false;
  let unavailable = false;
  let loginStatus = 200;
  let loginPosts = 0;
  let setupPosts = 0;
  let holdSession = true;
  const heldSessions = [];
  const violations = [];
  const runtimeErrors = [];
  cdp.on('Runtime.exceptionThrown', ({ exceptionDetails }) =>
    runtimeErrors.push(exceptionDetails.exception?.description ?? exceptionDetails.text));
  cdp.on('Fetch.requestPaused', async ({ requestId, request }) => {
    try {
      const path = new URL(request.url).pathname;
      if (path === '/api/auth/me' && holdSession) {
        heldSessions.push(requestId);
        return;
      }
      let status = 200;
      let body = '';
      const headers = [{ name: 'Content-Type', value: 'application/json' }];
      const token = signedIn ? 'test-user-csrf' : 'test-anonymous-csrf';
      if (request.method === 'POST') {
        const actual = Object.entries(request.headers).find(([key]) => key.toLowerCase() === 'x-xsrf-token')?.[1];
        if (actual !== token) violations.push('Incorrect CSRF header: ' + path);
      }
      if (path === '/api/permissions/me') {
        status = signedIn ? 200 : 401;
        if (signedIn) body = JSON.stringify({ role: account.role, grantedKeys: ['dashboard.view'] });
      } else if (path === '/api/permissions/check/dashboard.view') {
        status = signedIn ? 204 : 401;
      } else if (path === '/api/auth/csrf') {
        status = 204;
        headers.push({ name: 'Set-Cookie', value: 'XSRF-TOKEN=' + token + '; Path=/; Secure; SameSite=Strict' });
      } else if (path === '/api/auth/me') {
        status = unavailable ? 503 : signedIn ? 200 : 401;
        if (status === 200) body = JSON.stringify(account);
      } else if (path === '/api/auth/login') {
        loginPosts++;
        await delay(200);
        status = loginStatus;
        if (status === 200) {
          signedIn = true;
          body = JSON.stringify(account);
          headers.push({ name: 'Set-Cookie', value: '__Host-TestAuth=mock; Path=/; Secure; HttpOnly; SameSite=Strict' });
        }
      } else if (path === '/api/auth/logout') {
        signedIn = false;
        status = 204;
        headers.push({ name: 'Set-Cookie', value: '__Host-TestAuth=; Max-Age=0; Path=/; Secure; HttpOnly; SameSite=Strict' });
      } else if (path === '/api/setup/initialize-owner') {
        setupPosts++;
        await delay(200);
        status = 204; // Setup deliberately does not establish a session.
      } else { status = 404; }
      await cdp.send('Fetch.fulfillRequest', {
        requestId, responseCode: status, responseHeaders: headers, body: Buffer.from(body).toString('base64'),
      });
    } catch { violations.push('Mock request handling failed'); }
  });

  async function evaluate(expression) {
    const result = await cdp.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (result.exceptionDetails) throw new Error(result.exceptionDetails.exception?.description ?? 'Browser evaluation failed.');
    return result.result?.value;
  }
  async function until(expression) {
    for (let i = 0; i < 150; i++) {
      if (await evaluate(expression)) return;
      await delay(100);
    }
    throw new Error('Browser condition timed out: ' + expression);
  }
  async function navigate(path) {
    await cdp.send('Page.navigate', { url: origin + path });
    await until('document.readyState === "complete" && !!document.querySelector("app-root")');
  }
  async function fill(id, value) {
    await evaluate('(() => {const el=document.getElementById(' + JSON.stringify(id) + ');el.value=' +
      JSON.stringify(value) + ';el.dispatchEvent(new Event("input",{bubbles:true}));el.dispatchEvent(new Event("blur"));})()');
  }
  async function snapshot(name) {
    await evaluate('Promise.all(document.getAnimations().filter(a=>a.animationName?.includes("view-transition")).map(a=>a.finished.catch(()=>{})))');
    await evaluate('(async()=>{await document.fonts.ready;await Promise.all([...document.images].map(i=>i.decode()));})()');
    if (name !== 'splash-desktop') await delay(100);
    assert.equal(await evaluate('document.documentElement.scrollWidth <= innerWidth'), true, name + ' horizontal overflow');
    const images = await evaluate('[...document.images].filter(i=>i.src.includes("/assets/auth/")).map(i=>({src:i.getAttribute("src"),ok:i.complete&&i.naturalWidth>0,w:i.width,h:i.height}))');
    assert.ok(images.length > 0 && images.every(image => image.ok && image.w > 0 && image.h > 0), 'Missing visible asset: ' + JSON.stringify(images));
    const { data } = await cdp.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    await writeFile(resolve(output, name + '.png'), Buffer.from(data, 'base64'));
  }

  await navigate('/splash');
  await until('!!document.querySelector(".startup-tagline")');
  await snapshot('splash-desktop');
  await delay(4_500);
  assert.equal(await evaluate('location.pathname'), '/splash', 'Splash must wait for the user');
  await evaluate('document.querySelector(".startup-start").click()');
  await until('location.pathname === "/loading" && !!document.querySelector("[role=progressbar]")');
  const initialProgress = await evaluate('Number(document.querySelector("[role=progressbar]").getAttribute("aria-valuenow"))');
  await delay(600);
  const laterProgress = await evaluate('Number(document.querySelector("[role=progressbar]").getAttribute("aria-valuenow"))');
  assert.ok(laterProgress > initialProgress, 'Startup progress should increase');
  assert.ok(laterProgress < 100, 'Progress must not finish before the session check');
  await snapshot('loading-desktop');
  holdSession = false;
  for (const requestId of heldSessions) await cdp.send('Fetch.fulfillRequest', { requestId, responseCode: 401, body: '' });
  await until('location.pathname === "/login" && !!document.getElementById("login-username")');
  await navigate('/dashboard');
  await until('location.pathname === "/login" && !!document.getElementById("login-username")');
  assert.equal(await evaluate('[...document.links].some(a => a.pathname === "/initial-setup")'), false);
  await snapshot('login-desktop');
  await evaluate('document.querySelector("form").requestSubmit()');
  await until('!!document.querySelector("[aria-invalid=true]")');
  assert.equal(loginPosts, 0);
  await fill('login-username', 'test_owner');
  await fill('login-password', 'Mock password 42!');
  await evaluate('document.querySelector(".wf-reveal").click()');
  assert.equal(await evaluate('document.getElementById("login-password").type'), 'text');
  await evaluate('document.querySelector(".wf-reveal").click()');

  loginStatus = 401;
  await evaluate('document.querySelector("form").requestSubmit()');
  await until('document.querySelector("[role=alert]")?.textContent.includes("Unable to sign in")');
  loginStatus = 429;
  await evaluate('document.querySelector("form").requestSubmit()');
  await until('document.querySelector("[role=alert]")?.textContent.includes("Wait a minute")');
  loginStatus = 200;
  const previousLoginPosts = loginPosts;
  await evaluate('document.querySelector("form").requestSubmit();document.querySelector("form").requestSubmit()');
  await until('location.pathname === "/dashboard" && !!document.querySelector("app-dashboard")');
  assert.equal(loginPosts, previousLoginPosts + 1);
  await navigate('/dashboard');
  await until('!!document.querySelector("app-dashboard")');
  await evaluate('[...document.querySelectorAll("button")].find(b=>/log\\s*out/i.test(b.textContent)).click()');
  await until('location.pathname === "/login" && !!document.getElementById("login-username")');

  await navigate('/initial-setup');
  await until('!!document.getElementById("setup-full-name")');
  await snapshot('setup-desktop');
  await fill('setup-full-name', 'Test Owner');
  await fill('setup-username', 'new_owner');
  await fill('setup-password', 'short');
  await fill('setup-confirm', 'different');
  await fill('setup-code', 'bad-code');
  await evaluate('document.querySelector("form").requestSubmit()');
  await until('document.body.textContent.includes("Passwords must match")');
  assert.equal(setupPosts, 0);
  await fill('setup-password', 'A sufficiently long mock password');
  await fill('setup-confirm', 'A sufficiently long mock password');
  await fill('setup-code', 'A'.repeat(64));
  await evaluate('document.querySelector("form").requestSubmit();document.querySelector("form").requestSubmit()');
  await until('location.pathname === "/login" && document.body.textContent.includes("Your owner account is ready")');
  assert.equal(setupPosts, 1);
  assert.equal(signedIn, false);
  assert.equal(await evaluate('localStorage.length + sessionStorage.length'), 0);

  unavailable = true;
  await navigate('/loading');
  await until('document.body.textContent.includes("temporarily unavailable")');
  await snapshot('loading-error');
  unavailable = false;
  await evaluate('[...document.querySelectorAll("button")].find(b=>b.textContent.includes("Try again")).click()');
  await until('location.pathname === "/login" && !!document.getElementById("login-username")');
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 768, height: 1024, deviceScaleFactor: 1, mobile: false });
  await snapshot('login-tablet');
  await navigate('/initial-setup');
  await until('!!document.getElementById("setup-full-name")');
  await snapshot('setup-tablet');
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 844, deviceScaleFactor: 1, mobile: true });
  await snapshot('setup-mobile');
  for (const device of [
    { width: 320, height: 568, name: 'small-phone' },
    { width: 390, height: 844, name: 'phone' },
    { width: 667, height: 375, name: 'phone-landscape' },
    { width: 844, height: 390, name: 'wide-phone-landscape' },
  ]) {
    await cdp.send('Emulation.setDeviceMetricsOverride', {
      width: device.width, height: device.height, deviceScaleFactor: 1, mobile: true,
    });
    await navigate('/splash');
    await until('!!document.querySelector(".startup-start")');
    await snapshot('splash-' + device.name);
    await evaluate('document.querySelector(".startup-start").scrollIntoView({block:"center"})');
    assert.equal(await evaluate('(()=>{const r=document.querySelector(".startup-start").getBoundingClientRect();return r.height>=48&&r.top>=0&&r.bottom<=innerHeight})()'), true, 'Start button must remain reachable');

    heldSessions.length = 0;
    holdSession = true;
    await navigate('/loading');
    await until('!!document.querySelector("[role=progressbar]")');
    await snapshot('loading-' + device.name);
    assert.equal(await evaluate('document.querySelector(".startup-footer").getBoundingClientRect().top>=document.querySelector(".startup-main").getBoundingClientRect().bottom'), true, 'Loading footer must not overlap content');
    holdSession = false;
    for (const requestId of heldSessions) await cdp.send('Fetch.fulfillRequest', { requestId, responseCode: 401, body: '' });
    await until('location.pathname === "/login" && !!document.getElementById("login-password")');
    await snapshot('login-' + device.name);
    assert.equal(await evaluate('parseFloat(getComputedStyle(document.getElementById("login-password")).fontSize)>=16'), true);
    assert.equal(await evaluate('(()=>{const r=document.querySelector(".wf-reveal").getBoundingClientRect();return r.width>=44&&r.height>=44})()'), true, 'Password toggle must have a touch target');
    assert.equal(await evaluate('document.querySelector(".wf-craft").getBoundingClientRect().top>=document.querySelector(".wf-auth-content").getBoundingClientRect().bottom'), true, 'Auth caption must not overlap the form');

    await navigate('/initial-setup');
    await until('!!document.getElementById("setup-password")');
    await snapshot('setup-' + device.name);
    await evaluate('document.querySelector("button[type=submit]").scrollIntoView({block:"center"})');
    assert.equal(await evaluate('(()=>{const r=document.querySelector("button[type=submit]").getBoundingClientRect();return r.height>=48&&r.top>=0&&r.bottom<=innerHeight})()'), true, 'Setup submit button must remain reachable');
  }
  // Simulate the smaller viewport available when a phone keyboard is open.
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 390, height: 430, deviceScaleFactor: 1, mobile: true });
  await evaluate('document.getElementById("setup-password").focus();document.getElementById("setup-password").scrollIntoView({block:"center"})');
  assert.equal(await evaluate('(()=>{const r=document.getElementById("setup-password").getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight})()'), true);
  assert.deepEqual(violations, []);
  assert.deepEqual(runtimeErrors, []);
  console.log('Browser checks passed: guards, login/errors/rate limits, real Angular XSRF headers, logout, setup validation/no auto-login, duplicate prevention, retry, responsive assets. APIs were mocked; no real authentication or database writes.');
} finally {
  cdp?.close();
  if (root) {
    try { await root.send('Browser.close'); } catch { }
    root.close();
  }
  child.kill();
}
