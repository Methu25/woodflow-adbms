import '@angular/compiler';
import { strict as assert } from 'node:assert';
import { test } from 'node:test';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injector, runInInjectionContext } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom, Observable, Subscriber } from 'rxjs';
import { API_URL } from '../src/app/core/api-url.token';
import { AuthService } from '../src/app/core/auth/auth.service';
import { authGuard, guestGuard } from '../src/app/core/auth/auth.guards';
import { authErrorMessage } from '../src/app/core/auth/auth-errors';

const user = { userId: 7, username: 'test-owner', fullName: 'Test Owner', role: 'OWNER' };
const credential = { username: 'test-owner', password: 'Test-only password 42!' };

function fixture() {
  const queue: { method: string; url: string; body: unknown; subscriber: Subscriber<unknown> }[] = [];
  const request = (method: string, url: string, body?: unknown) =>
    new Observable(subscriber => { queue.push({ method, url, body, subscriber }); });
  const http = { get: (url: string) => request('GET', url), post: (url: string, body: unknown) => request('POST', url, body) };
  const router = { createUrlTree: (path: string[], extras?: unknown) => ({ path: path[0], extras }) };
  const injector = Injector.create({ providers: [
    { provide: HttpClient, useValue: http }, { provide: API_URL, useValue: '/api' },
    { provide: Router, useValue: router },
  ] });
  const auth = runInInjectionContext(injector, () => new AuthService());
  const guardInjector = Injector.create({ providers: [
    { provide: AuthService, useValue: auth }, { provide: Router, useValue: router },
  ] });
  function take(method: string, url: string) {
    const next = queue.shift();
    assert.ok(next, 'Expected a request');
    assert.equal(next.method, method);
    assert.equal(next.url, url);
    return next;
  }
  function reply(method: string, url: string, value?: unknown) {
    const next = take(method, url);
    next.subscriber.next(value);
    next.subscriber.complete();
  }
  const fail = (method: string, url: string, status: number) =>
    take(method, url).subscriber.error(new HttpErrorResponse({ status }));
  const guard = (guest = false) => runInInjectionContext(guardInjector,
    () => firstValueFrom((guest ? guestGuard : authGuard)({} as never, {} as never) as Observable<unknown>));
  return { auth, queue, take, reply, fail, guard };
}

test('login obtains CSRF before POST and refreshes after identity changes', async () => {
  const f = fixture();
  const result = firstValueFrom(f.auth.login(credential));
  f.reply('GET', '/api/auth/csrf');
  const post = f.take('POST', '/api/auth/login');
  assert.deepEqual(post.body, credential);
  post.subscriber.next(user);
  post.subscriber.complete();
  assert.deepEqual(f.auth.user(), user);
  f.reply('GET', '/api/auth/csrf');
  assert.deepEqual(await result, user);
  assert.equal(f.queue.length, 0);
});

test('post-login CSRF failure does not misreport successful login', async () => {
  const f = fixture();
  const result = firstValueFrom(f.auth.login(credential));
  f.reply('GET', '/api/auth/csrf');
  f.reply('POST', '/api/auth/login', user);
  f.fail('GET', '/api/auth/csrf', 503);
  assert.deepEqual(await result, user);
});

test('failed CSRF initialization prevents credential submission', async () => {
  const f = fixture();
  const result = firstValueFrom(f.auth.login(credential));
  const rejected = assert.rejects(result);
  f.fail('GET', '/api/auth/csrf', 400);
  await rejected;
  assert.equal(f.queue.length, 0);
  assert.equal(f.auth.user(), null);
});

test('setup submits only after CSRF and never authenticates the new owner', async () => {
  const f = fixture();
  const details = { ...credential, fullName: 'Test Owner', confirmPassword: credential.password, setupCode: 'A'.repeat(64) };
  const result = firstValueFrom(f.auth.initializeOwner(details));
  f.reply('GET', '/api/auth/csrf');
  const post = f.take('POST', '/api/setup/initialize-owner');
  assert.deepEqual(post.body, details);
  post.subscriber.next(undefined);
  post.subscriber.complete();
  await result;
  assert.equal(f.auth.user(), null);
  assert.equal(f.queue.length, 0);
});

test('logout obtains CSRF, clears memory state, and refreshes anonymous CSRF', async () => {
  const f = fixture();
  const session = firstValueFrom(f.auth.checkSession());
  f.reply('GET', '/api/auth/me', user);
  await session;
  const result = firstValueFrom(f.auth.logout());
  f.reply('GET', '/api/auth/csrf');
  f.reply('POST', '/api/auth/logout');
  assert.equal(f.auth.user(), null);
  f.reply('GET', '/api/auth/csrf');
  await result;
});

test('concurrent session checks share a request; later navigation revalidates', async () => {
  const f = fixture();
  const first = firstValueFrom(f.auth.checkSession());
  const second = firstValueFrom(f.auth.checkSession());
  assert.equal(f.queue.length, 1);
  f.reply('GET', '/api/auth/me', user);
  assert.deepEqual(await first, user);
  assert.deepEqual(await second, user);
  const third = firstValueFrom(f.auth.checkSession());
  f.fail('GET', '/api/auth/me', 401);
  assert.equal(await third, null);
  assert.equal(f.auth.user(), null);
});

test('protected routes reject anonymous sessions without opening setup', async () => {
  const f = fixture();
  const result = f.guard();
  f.fail('GET', '/api/auth/me', 401);
  assert.equal((await result as { path: string }).path, '/login');
});

test('protected routes allow a verified authenticated session', async () => {
  const f = fixture();
  const result = f.guard();
  f.reply('GET', '/api/auth/me', user);
  assert.equal(await result, true);
});

test('outages route to a retry state instead of treating installation as new', async () => {
  const f = fixture();
  const result = f.guard();
  f.fail('GET', '/api/auth/me', 503);
  assert.deepEqual(await result, { path: '/loading', extras: { queryParams: { retry: '1' } } });
});

test('verified users visiting login are sent to dashboard', async () => {
  const f = fixture();
  const result = f.guard(true);
  f.reply('GET', '/api/auth/me', user);
  assert.equal((await result as { path: string }).path, '/dashboard');
});

test('errors use safe frontend messages rather than database/backend details', () => {
  for (const status of [400, 401, 403, 429, 503, 0]) {
    const error = new HttpErrorResponse({ status, error: { message: 'private database detail' } });
    for (const action of ['login', 'setup', 'session', 'logout'] as const) {
      const text = authErrorMessage(error, action);
      assert.ok(text.length > 20);
      assert.ok(!text.includes('private database detail'));
    }
  }
});
