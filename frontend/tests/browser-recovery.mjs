// Production UI integration checks. ALL /api requests are intercepted; no real recovery/account writes.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { existsSync } from 'node:fs';
import { mkdir, writeFile } from 'node:fs/promises';
import { resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

const origin = process.env.WOODFLOW_TEST_BASE_URL ?? 'https://localhost:4300';
assert.equal(new URL(origin).protocol, 'https:');
const browser = process.env.BROWSER_PATH ?? 'C:/Program Files/Google/Chrome/Application/chrome.exe';
assert.ok(existsSync(browser), 'Set BROWSER_PATH to a local Chromium browser.');
const output = resolve('.angular/verification');
await mkdir(output, { recursive: true });
const child = spawn(browser, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--remote-debugging-port=0', '--user-data-dir=' + resolve('.angular/browser-recovery-profile'),
  '--window-size=1440,900', 'about:blank'], { windowsHide: true, stdio: ['ignore', 'ignore', 'pipe'] });
class Cdp {
  next = 0; pending = new Map(); handlers = new Map();
  constructor(url) {
    this.socket = new WebSocket(url);
    this.ready = new Promise((resolve, reject) => {
      this.socket.addEventListener('open', resolve, { once: true }); this.socket.addEventListener('error', reject, { once: true });
    });
    this.socket.addEventListener('message', ({ data }) => {
      const result = JSON.parse(String(data));
      if (result.id) {
        const entry = this.pending.get(result.id); this.pending.delete(result.id);
        if (result.error) entry?.reject(new Error(result.error.message)); else entry?.resolve(result.result);
      } else for (const handler of this.handlers.get(result.method) ?? []) handler(result.params);
    });
  }
  on(name, handler) { this.handlers.set(name, [...(this.handlers.get(name) ?? []), handler]); }
  async send(method, params = {}) {
    await this.ready; const id = ++this.next;
    const result = new Promise((resolve, reject) => this.pending.set(id, { resolve, reject }));
    this.socket.send(JSON.stringify({ id, method, params })); return result;
  }
  close() { this.socket.close(); }
}
let root, cdp;
try {
  const endpoint = await new Promise((resolve, reject) => {
    let text = ''; const timer = setTimeout(() => reject(new Error('Browser startup timed out.')), 20000);
    child.once('error', reject); child.stderr.on('data', chunk => {
      text += String(chunk); const match = text.match(/DevTools listening on (ws:\/\/[^\s]+)/);
      if (match) { clearTimeout(timer); resolve(match[1]); }
    });
  });
  root = new Cdp(endpoint);
  const { targetId } = await root.send('Target.createTarget', { url: 'about:blank' });
  const targets = await (await fetch('http://127.0.0.1:' + new URL(endpoint).port + '/json/list')).json();
  cdp = new Cdp(targets.find(t => t.id === targetId).webSocketDebuggerUrl);
  await cdp.send('Page.enable'); await cdp.send('Runtime.enable');
  await cdp.send('Emulation.setDeviceMetricsOverride', { width: 1440, height: 900, deviceScaleFactor: 1, mobile: false });
  await cdp.send('Fetch.enable', { patterns: [{ urlPattern: '*/api/*' }] });
  let signedIn = false, role = 'OWNER', submitCount = 0, approveCount = 0, denyCount = 0, completeCount = 0;
  let submitStatus = 202, resetStatus = 0, currentPassword = 'Mock old password 42!', sessionInvalidations = 0;
  const violations = [], runtimeErrors = [];
  const code = 'B'.repeat(64); // Deterministic fake test value, never a real setup/reset authorization.
  const account = () => ({ userId: 10, username: 'test_owner', fullName: 'Test Owner', role });
  const items = ['Assistant', 'Other Owner'].map((fullName, index) => ({
    requestId: `11111111-1111-1111-1111-11111111111${index}`, requesterUserId: 11 + index,
    fullName, username: index ? 'other_owner' : 'assistant', role: index ? 'OWNER' : 'ASSISTANT', status: 'PENDING',
    createdAt: new Date().toISOString(), updatedAt: new Date().toISOString(), expiresAt: new Date(Date.now()+3600000).toISOString(),
    processedByOwnerId: null, processedAt: null, consumedAt: null, selectedOwnerId: 10,
  }));
  cdp.on('Runtime.exceptionThrown', ({ exceptionDetails }) => runtimeErrors.push(exceptionDetails.exception?.description ?? exceptionDetails.text));
  cdp.on('Fetch.requestPaused', async ({ requestId, request }) => {
    try {
      const url = new URL(request.url), path = url.pathname;
      let status = 200, body;
      const headers = [{ name: 'Content-Type', value: 'application/json' }];
      const token = signedIn ? 'test-user-csrf' : 'test-public-csrf';
      if (request.method === 'POST') {
        const xsrf = Object.entries(request.headers).find(([key]) => key.toLowerCase() === 'x-xsrf-token')?.[1];
        if (xsrf !== token) violations.push('Missing or stale XSRF header at ' + path);
      }
      const data = request.postData ? JSON.parse(request.postData) : {};
      const item = items.find(i => path.includes(i.requestId));
      if (path === '/api/auth/csrf') {
        status = 204; headers.push({ name: 'Set-Cookie', value: 'XSRF-TOKEN='+token+'; Path=/; Secure; SameSite=Strict' });
      } else if (path === '/api/auth/me') { status = signedIn ? 200 : 401; body = signedIn ? account() : undefined;
      } else if (path === '/api/auth/login') {
        status = data.password === currentPassword ? 200 : 401;
        if (status === 200) { signedIn = true; body = account(); }
      } else if (path === '/api/auth/logout') { signedIn = false; status = 204;
      } else if (path === '/api/permissions/me') { body = { role, grantedKeys: ['dashboard.view'] };
      } else if (path === '/api/permissions/check/dashboard.view') { status = signedIn ? 204 : 401;
      } else if (path === '/api/users/counts') { body = { activeOwners: 2, activeAssistants: 1, suspended: 0, deactivated: 0 };
      } else if (path === '/api/users') { body = { items: [], total: 0, page: 1, pageSize: 10 };
      } else if (path === '/api/password-recovery/owners') { body = [{ ownerId: 10, fullName: 'Test Owner' }, { ownerId: 12, fullName: 'Other Owner' }];
      } else if (path === '/api/password-recovery/requests' && request.method === 'POST') {
        submitCount++; await delay(200); status = submitStatus;
        body = { message: 'If the request is eligible, the selected owner will receive it.' };
      } else if (path === '/api/password-recovery/complete') {
        completeCount++; await delay(200);
        status = resetStatus || (data.code === code && items[0].status === 'APPROVED' ? 204 : 400);
        if (status === 204) { items[0].status = 'COMPLETED'; currentPassword = data.newPassword; signedIn = false; sessionInvalidations++; }
      } else if (path.startsWith('/api/password-recovery/')) {
        if (!signedIn || role !== 'OWNER') { status = signedIn ? 403 : 401;
        } else if (path.endsWith('/pending-count')) { body = { pendingCount: items.filter(i => i.status === 'PENDING' && i.selectedOwnerId === 10).length };
        } else if (path === '/api/password-recovery/requests') {
          const category = url.searchParams.get('category');
          const filtered = items.filter(i => i.selectedOwnerId === 10 && (category === 'pending' ? i.status === 'PENDING' : category === 'denied' ? i.status === 'DENIED' : ['APPROVED','COMPLETED','EXPIRED'].includes(i.status)));
          body = { items: filtered, total: filtered.length, page: 1, pageSize: 10 };
        } else if (path.endsWith('/resolve-pending')) {
          const pending = items.find(i => i.username === data.username && i.status === 'PENDING');
          if (!pending || !data.identityVerified || !data.confirmed) status = 409;
          else { pending.selectedOwnerId = 10; if(data.resolution === 'DENY') pending.status = 'DENIED'; status = 204; }
        } else if (!item) { status = 404;
        } else if (path.endsWith('/approve')) {
          approveCount++; await delay(200);
          if (item.status !== 'PENDING' || data.identityVerified !== true) status = 409;
          else { item.status = 'APPROVED'; item.processedAt = new Date().toISOString(); item.expiresAt = new Date(Date.now()+600000).toISOString(); body = { code, expiresAt: item.expiresAt }; }
        } else if (path.endsWith('/deny')) {
          denyCount++; await delay(200);
          if (item.status !== 'PENDING') status = 409; else { item.status = 'DENIED'; status = 204; }
        } else body = item;
      } else { violations.push('Unexpected API '+path); status = 503; }
      await cdp.send('Fetch.fulfillRequest', { requestId, responseCode: status, responseHeaders: headers,
        body: Buffer.from(body === undefined ? '' : JSON.stringify(body)).toString('base64') });
    } catch { violations.push('Mock request handling failed'); }
  });
  async function evaluate(expression) {
    const r = await cdp.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description ?? 'Evaluation failed.');
    return r.result?.value;
  }
  async function until(expression) {
    for (let i=0; i<150; i++) { if (await evaluate(expression)) return; await delay(100); }
    throw new Error('Timed out: '+expression);
  }
  async function navigate(path) { await cdp.send('Page.navigate', { url: origin+path }); await until('document.readyState==="complete" && !!document.querySelector("app-root")'); }
  async function click(text, scope = 'document') {
    await evaluate(`(()=>{const b=[...${scope}.querySelectorAll('button,a')].find(b=>b.textContent.trim()===${JSON.stringify(text)});if(!b)throw Error('Missing button');b.click();})()`);
  }
  async function fill(id, value) {
    await evaluate(`(()=>{const e=document.getElementById(${JSON.stringify(id)});e.value=${JSON.stringify(value)};e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('blur'));})()`);
  }
  async function filter(status) { const label = status === 'PENDING' ? 'Pending' : status === 'DENIED' ? 'Denied' : 'Completed'; await evaluate(`[...document.querySelectorAll('.status-tab')].find(b=>b.querySelector('strong').textContent===${JSON.stringify(label)}).click()`); await delay(300); }
  // The existing shared dialog uses data-initial-focus; use visible text for its controls.
  async function confirm(text) { await until('!!document.querySelector("app-user-feedback-dialog dialog[open]")'); await click(text, 'document.querySelector("app-user-feedback-dialog")'); }
  async function snapshot(name) {
    await evaluate('(async()=>{await document.fonts.ready;await Promise.all([...document.images].map(i=>i.decode()));})()');
    assert.equal(await evaluate('document.documentElement.scrollWidth<=innerWidth'), true, name+' outer overflow');
    assert.equal(await evaluate('[...document.images].every(i=>i.complete&&i.naturalWidth>0)'), true, 'Local assets loaded');
    const { data } = await cdp.send('Page.captureScreenshot', { format: 'png', captureBeyondViewport: false });
    await writeFile(resolve(output,name+'.png'),Buffer.from(data,'base64'));
  }
  async function submitForm() { await evaluate('document.querySelector("form").requestSubmit();document.querySelector("form").requestSubmit()'); }
  async function resetFields(token, password = 'Mock new password 73!') { await fill('reset-code',token); await fill('reset-password',password); await fill('reset-confirm',password); }

  await navigate('/login'); await until('!!document.getElementById("login-username")');
  await click('Forgot Password?'); await until('document.querySelectorAll("#recovery-owner option").length===3');
  await snapshot('recovery-forgot-desktop');
  await submitForm(); assert.equal(submitCount,0);
  await fill('recovery-username','unknown-test-user');
  await evaluate('(()=>{const e=document.getElementById("recovery-owner");e.selectedIndex=1;e.dispatchEvent(new Event("change",{bubbles:true}));})()');
  submitStatus=429; await submitForm(); await until('document.body.textContent.includes("Too many attempts")');
  submitStatus=202; await submitForm(); await until('document.body.textContent.includes("Recovery request submitted")');
  assert.equal(submitCount,2,'Duplicate submissions blocked'); await confirm('OK');
  assert.equal(await evaluate('document.querySelector("form")===null'),true);
  await click('Set a new password'); await until('!!document.getElementById("reset-code")');
  await resetFields(code,'short'); await submitForm(); assert.equal(completeCount,0);
  await resetFields('0'.repeat(64)); await fill('reset-confirm','different'); await submitForm(); assert.equal(completeCount,0);
  await resetFields('0'.repeat(64)); await submitForm(); await until('document.body.textContent.includes("invalid, expired or already used")');
  assert.equal(completeCount,1); assert.equal(await evaluate('document.getElementById("reset-password").value'), '');
  resetStatus=429; await resetFields(code); await submitForm(); await until('document.body.textContent.includes("Too many attempts")');
  resetStatus=503; await resetFields(code); await submitForm(); await until('document.body.textContent.includes("temporarily unavailable")'); resetStatus=0;

  signedIn=true; role='ASSISTANT'; await navigate('/users/password-recovery');
  await until('location.pathname==="/dashboard" && !!document.querySelector("app-dashboard")');
  role='OWNER'; await navigate('/users'); await until('document.querySelector(".pending-badge")?.textContent.trim()==="2"');
  assert.equal(await evaluate('[...document.querySelectorAll(".tool-button")].filter(b=>b.textContent.includes("Password Reset Requests")).length'),1);
  await evaluate('[...document.querySelectorAll(".tool-button")].find(b=>b.textContent.includes("Password Reset Requests")).click()');
  await until('document.querySelectorAll("tbody tr").length===2'); await snapshot('recovery-inbox-desktop');
  assert.equal(await evaluate('document.querySelector(".status-tab.active strong").textContent'),'Pending');
  assert.equal(await evaluate('document.querySelectorAll(".status-tab").length'),3);
  await click('Review request'); await until('!!document.querySelector(".request-dialog[open]")');
  await snapshot('recovery-review-desktop');
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await snapshot('recovery-review-phone');
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  assert.equal(await evaluate('document.querySelector(".decision-buttons .primary").disabled'),true);
  await evaluate('document.querySelector(".verification input").click()'); await click('Approve & Issue Code'); await confirm('Cancel');
  assert.equal(approveCount,0);
  await click('Approve & Issue Code'); await confirm('Approve & issue code');
  await until('!!document.querySelector(".code-dialog[open]")'); assert.equal(approveCount,1);
  await evaluate('window.mockCopyCalls=0;Object.defineProperty(navigator,"clipboard",{configurable:true,value:{writeText:async()=>{window.mockCopyCalls++}}})');
  await click('Copy Code'); await until('document.querySelector(".copy-code")?.textContent.trim()==="Copied"');
  assert.equal(await evaluate('window.mockCopyCalls'),1);
  await snapshot('recovery-code-desktop');
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:390,height:844,deviceScaleFactor:1,mobile:true});
  await snapshot('recovery-code-phone');
  await evaluate('document.querySelector(".close-code").scrollIntoView({block:"center"})');
  assert.equal(await evaluate('(()=>{const r=document.querySelector(".close-code").getBoundingClientRect();return r.top>=0&&r.bottom<=innerHeight&&r.height>=44})()'),true);
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:1440,height:900,deviceScaleFactor:1,mobile:false});
  assert.equal(await evaluate('localStorage.length+sessionStorage.length'),0);
  assert.equal(await evaluate('location.search+location.hash'),'');
  await click('Done & Close','document.querySelector(".code-dialog")'); await confirm('OK');
  assert.equal(await evaluate('document.querySelector(".status-tab.active strong").textContent'),'Completed');
  assert.equal(await evaluate('!!document.querySelector(".reset-code")'),false);
  await filter('APPROVED'); await click('View details'); await until('!!document.querySelector(".request-dialog[open]")');
  assert.equal(await evaluate('!!document.querySelector(".reset-code")'),false);
  assert.equal(await evaluate('!!document.querySelector(".request-dialog .decision-buttons")'),false);
  await click('Back to requests','document.querySelector(".request-dialog")');
  await filter('PENDING'); await click('Deny'); await confirm('Cancel'); assert.equal(denyCount,0);
  await click('Deny'); await confirm('Deny request'); await until('document.body.textContent.includes("Request denied")');
  await confirm('OK'); assert.equal(denyCount,1);
  items.push({...items[1],requestId:'22222222-2222-2222-2222-222222222222',requesterUserId:13,username:'blocked_user',fullName:'Blocked Assistant',status:'PENDING',selectedOwnerId:12});
  await click('Resolve a blocked request'); await until('!!document.getElementById("resolve-username")');
  await fill('resolve-username','blocked_user');
  const resolutionScope = 'document.querySelector("dialog[aria-labelledby=resolve-title]")';
  assert.equal(await evaluate(`[...${resolutionScope}.querySelectorAll('button')].find(b=>b.textContent.trim()==='Assign to me').disabled`),true);
  await evaluate(`${resolutionScope}.querySelector('input[type=checkbox]').click()`);
  await click('Assign to me',resolutionScope); await confirm('Assign to me');
  await until('document.body.textContent.includes("Pending request resolved")'); await confirm('OK');
  await until('document.querySelector("tbody")?.textContent.includes("blocked_user")');
  assert.equal(items.find(i=>i.username==='blocked_user').selectedOwnerId,10);
  assert.equal(items[0].status,'APPROVED','Recovery resolution must not overwrite approved codes');
  await click('Deny'); await confirm('Deny request'); await until('document.body.textContent.includes("Request denied")'); await confirm('OK');
  await navigate('/users'); await until('document.querySelector(".pending-badge")?.textContent.trim()==="0"');

  // Simulated reset invalidates the mock session; real persistence/revocation belongs to backend integration tests.
  await navigate('/reset-password'); await until('!!document.getElementById("reset-code")');
  await resetFields(code); await submitForm(); await until('document.body.textContent.includes("Password updated")');
  assert.equal(sessionInvalidations,1); assert.equal(signedIn,false);
  assert.equal(await evaluate('[...document.querySelectorAll("input")].every(i=>!i.value)'),true);
  await confirm('OK'); await until('location.pathname==="/login" && !!document.getElementById("login-username")');
  await fill('login-username','test_owner'); await fill('login-password','Mock old password 42!'); await submitForm();
  await until('document.body.textContent.includes("Unable to sign in")');
  await fill('login-password','Mock new password 73!'); await submitForm();
  await until('location.pathname==="/dashboard" && !!document.querySelector("app-dashboard")');

  for (const size of [{width:768,height:1024},{width:390,height:844},{width:320,height:568}]) {
    await cdp.send('Emulation.setDeviceMetricsOverride',{...size,deviceScaleFactor:1,mobile:size.width<700});
    signedIn=false;
    await navigate('/forgot-password'); await until('document.querySelectorAll("#recovery-owner option").length===3');
    await snapshot('recovery-forgot-'+size.width);
    await navigate('/reset-password'); await until('!!document.getElementById("reset-code")'); await snapshot('recovery-reset-'+size.width);
    signedIn=true; role='OWNER';
    await navigate('/users/password-recovery'); await until('document.querySelectorAll(".status-tab").length===3 && !document.body.textContent.includes("Loading your recovery")');
    await filter(''); await snapshot('recovery-inbox-'+size.width);
  }
  assert.deepEqual(violations,[]); assert.deepEqual(runtimeErrors,[]);
  console.log('Recovery browser checks passed: navigation, generic submission, validation/rate limits, OWNER guard, badge, inbox, approval/denial confirmation, one-time display/copy, reset/login, XSRF and tablet/mobile assets. All APIs mocked; no database writes.');
} finally {
  cdp?.close();
  if(root){try{await root.send('Browser.close');}catch{}root.close();}
  child.kill();
}
