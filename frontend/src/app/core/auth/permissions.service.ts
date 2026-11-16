import { HttpClient } from '@angular/common/http';
import { Injectable, inject, signal } from '@angular/core';
import { tap, timeout } from 'rxjs';
import { API_URL } from '../api-url.token';
import { AuthService } from './auth.service';

export interface EffectivePermissions { role: string; grantedKeys: string[]; }
@Injectable({ providedIn: 'root' })
export class PermissionsService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly url = `${inject(API_URL)}/permissions`;
  private readonly current = signal<EffectivePermissions | null>(null);
  has(key: string) { return this.auth.user()?.role === 'OWNER' || (this.auth.user()?.role === 'ASSISTANT' && this.current()?.role === 'ASSISTANT' && !!this.current()?.grantedKeys.includes(key)); }
  refresh() {
    this.current.set(null);
    return this.http.get<EffectivePermissions>(`${this.url}/me`).pipe(timeout(10000), tap(result => this.current.set(result)));
  }
  check(key: string) { return this.http.get<void>(`${this.url}/check/${encodeURIComponent(key)}`).pipe(timeout(10000)); }
}
