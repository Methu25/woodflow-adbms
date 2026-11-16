import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { Observable, catchError, finalize, map, of, shareReplay, switchMap, tap, throwError, timeout } from 'rxjs';
import { API_URL } from '../api-url.token';
import { Account, LoginRequest, OwnerSetupRequest } from './auth.models';

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly http = inject(HttpClient);
  private readonly api = inject(API_URL);
  private readonly account = signal<Account | null>(null);
  private sessionRequest?: Observable<Account | null>;
  readonly user = this.account.asReadonly();

  // Always ask the backend on navigation; a browser flag never establishes a session.
  // Concurrent guards share one request, but results are not permanently cached.
  checkSession(): Observable<Account | null> {
    if (!this.sessionRequest) {
      this.sessionRequest = this.http.get<Account>(`${this.api}/auth/me`).pipe(
        timeout(10_000),
        tap(user => this.account.set(user)),
        catchError(error => {
          this.account.set(null);
          return error instanceof HttpErrorResponse && error.status === 401
            ? of(null) : throwError(() => error);
        }),
        finalize(() => { this.sessionRequest = undefined; }),
        shareReplay({ bufferSize: 1, refCount: false }),
      );
    }
    return this.sessionRequest;
  }

  refreshCsrf(): Observable<void> {
    return this.http.get<void>(`${this.api}/auth/csrf`).pipe(timeout(10_000));
  }

  login(request: LoginRequest): Observable<Account> {
    return this.refreshCsrf().pipe(
      switchMap(() => this.http.post<Account>(`${this.api}/auth/login`, request).pipe(timeout(10_000))),
      tap(user => this.account.set(user)),
      // A successful sign-in must not be presented as failed if this refresh fails.
      // Every subsequent mutation obtains a fresh token again before submitting.
      switchMap(user => this.refreshCsrf().pipe(map(() => user), catchError(() => of(user)))),
    );
  }

  initializeOwner(request: OwnerSetupRequest): Observable<void> {
    return this.refreshCsrf().pipe(
      switchMap(() => this.http.post<void>(`${this.api}/setup/initialize-owner`, request).pipe(timeout(10_000))),
    );
  }

  logout(): Observable<void> {
    return this.refreshCsrf().pipe(
      switchMap(() => this.http.post<void>(`${this.api}/auth/logout`, {}).pipe(timeout(10_000))),
      catchError(error => error instanceof HttpErrorResponse && error.status === 401
        ? of(undefined) : throwError(() => error)),
      tap(() => this.account.set(null)),
      switchMap(() => this.refreshCsrf().pipe(catchError(() => of(undefined)))),
    );
  }
}
