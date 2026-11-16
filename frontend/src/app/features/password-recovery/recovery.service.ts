import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Subscription, switchMap, tap, timeout } from 'rxjs';
import { API_URL } from '../../core/api-url.token';
import { AuthService } from '../../core/auth/auth.service';

export type RecoveryStatus = 'PENDING' | 'APPROVED' | 'COMPLETED' | 'DENIED' | 'EXPIRED';
export interface RecoveryContact { ownerId: number; fullName: string; }
export interface RecoveryRequest {
  requestId: string; requesterUserId: number; fullName: string; username: string; role: 'OWNER' | 'ASSISTANT';
  status: RecoveryStatus; createdAt: string; updatedAt: string; expiresAt: string;
  processedByOwnerId: number | null; processedAt: string | null; consumedAt: string | null;
}
export interface RecoveryPage { items: RecoveryRequest[]; total: number; page: number; pageSize: number; }
export interface ResetCode { code: string; expiresAt: string; }

@Injectable({ providedIn: 'root' })
export class RecoveryService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly url = `${inject(API_URL)}/password-recovery`;
  private readonly count = signal<{ ownerId: number; value: number } | null>(null);
  private countRequest?: Subscription;
  readonly pendingCount = computed(() => this.auth.user()?.role === 'OWNER' &&
    this.count()?.ownerId === this.auth.user()?.userId ? this.count()!.value : null);

  owners() { return this.http.get<RecoveryContact[]>(`${this.url}/owners`).pipe(timeout(15000)); }
  submit(username: string, ownerId: number) { return this.post<{ message: string }>('requests', { username, ownerId }); }
  complete(code: string, newPassword: string, confirmPassword: string) {
    return this.post<void>('complete', { code, newPassword, confirmPassword });
  }
  inbox(status: string, page: number) {
    return this.http.get<RecoveryPage>(`${this.url}/requests`, {
      params: { category: status, page, pageSize: 10 },
    }).pipe(timeout(15000));
  }
  details(id: string) { return this.http.get<RecoveryRequest>(`${this.url}/requests/${id}`).pipe(timeout(15000)); }
  approve(id: string) {
    // The code stays in this response and the one-time view, never a shared signal/cache/storage.
    return this.post<ResetCode>(`requests/${id}/approve`, { identityVerified: true })
      .pipe(tap(() => this.refreshPendingCount()));
  }
  deny(id: string) { return this.post<void>(`requests/${id}/deny`, {}).pipe(tap(() => this.refreshPendingCount())); }
  resolvePending(username: string, resolution: 'REASSIGN_TO_ME' | 'DENY') {
    return this.post<void>('resolve-pending', { username, resolution, identityVerified: true, confirmed: true })
      .pipe(tap(() => this.refreshPendingCount()));
  }
  refreshPendingCount() {
    this.countRequest?.unsubscribe();
    const user = this.auth.user();
    if (user?.role !== 'OWNER') { this.count.set(null); return; }
    this.countRequest = this.http.get<{ pendingCount: number }>(`${this.url}/pending-count`).pipe(timeout(15000))
      .subscribe({ next: result => this.count.set({ ownerId: user.userId, value: result.pendingCount }),
        error: () => this.count.set(null) });
  }
  private post<T>(path: string, body: object) {
    return this.auth.refreshCsrf().pipe(switchMap(() => this.http.post<T>(`${this.url}/${path}`, body)), timeout(15000));
  }
}

export function recoveryError(error: unknown, resetting = false): string {
  if (error instanceof HttpErrorResponse) {
    if (error.status === 429) return 'Too many attempts. Please wait a few minutes before trying again.';
    if (error.status === 400) return resetting
      ? 'Unable to reset your password. The code may be invalid, expired or already used. Check your details or ask your owner for a new recovery request.'
      : 'We could not submit this request. Check the form and try again.';
    if (error.status === 401) return 'Your session has ended. Please sign in again.';
    if (error.status === 403) return 'Only the assigned OWNER can handle this request.';
    if (error.status === 404) return 'This request is unavailable or is assigned to another owner.';
    if (error.status === 409) return 'This request has changed or expired. Refresh to see its current status.';
  }
  return 'Password recovery is temporarily unavailable. If you already submitted, check with your owner before retrying.';
}
