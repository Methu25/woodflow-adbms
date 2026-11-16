import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap, timeout } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { API_URL } from '../../../core/api-url.token';
import { ManagedUser } from '../users.service';

export interface PermissionDefinition { key: string; action: string; }
export interface PermissionModule { key: string; label: string; description: string; permissions: PermissionDefinition[]; }
export interface PermissionSettings { grantedKeys: string[]; version: number; }
export interface PermissionMatrix extends PermissionSettings {
  modules: PermissionModule[]; user?: ManagedUser; inheritsRole?: boolean; roleVersion?: number;
}
@Injectable({ providedIn: 'root' })
export class PermissionAdminService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly url = `${inject(API_URL)}/permissions/assistant`;
  private userUrl(userId: number) { return this.url.replace('/assistant', `/users/${userId}`); }
  load(userId?: number) { return this.http.get<PermissionMatrix>(userId ? this.userUrl(userId) : this.url).pipe(timeout(15000)); }
  save(grantedKeys: string[], expectedVersion: number) {
    return this.auth.refreshCsrf().pipe(switchMap(() => this.http.put<PermissionSettings>(this.url, {
      grantedKeys, expectedVersion, confirmed: true,
    })), timeout(15000));
  }
  saveUser(matrix: PermissionMatrix, grantedKeys: string[], useRoleDefaults = false) {
    return this.auth.refreshCsrf().pipe(switchMap(() => this.http.put<Omit<PermissionMatrix, 'modules'>>(this.userUrl(matrix.user!.userId), {
      grantedKeys: useRoleDefaults ? [] : grantedKeys, expectedVersion: matrix.version,
      expectedRoleVersion: matrix.roleVersion, expectedAccountVersion: matrix.user!.version,
      useRoleDefaults, confirmed: true,
    })), timeout(15000));
  }
}
