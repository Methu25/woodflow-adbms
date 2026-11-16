import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { switchMap, timeout } from 'rxjs';
import { API_URL } from '../../core/api-url.token';
import { AuthService } from '../../core/auth/auth.service';

export type Role = 'OWNER' | 'ASSISTANT';
export type AccountStatus = 'ACTIVE' | 'SUSPENDED' | 'DEACTIVATED';
export type Category = 'owners' | 'assistants' | 'suspended' | 'deactivated';
export interface ManagedUser {
  userId: number; fullName: string; username: string; role: Role;
  status: AccountStatus; createdAt: string | null; version: number;
}
export interface Counts { activeOwners: number; activeAssistants: number; suspended: number; deactivated: number; }
export interface UserPage { items: ManagedUser[]; total: number; page: number; pageSize: number; }
export interface CreateUser { fullName: string; username: string; password: string; role: Role; }

@Injectable({ providedIn: 'root' })
export class UsersService {
  private readonly http = inject(HttpClient);
  private readonly auth = inject(AuthService);
  private readonly url = `${inject(API_URL)}/users`;
  counts() { return this.http.get<Counts>(`${this.url}/counts`).pipe(timeout(15000)); }
  list(category: Category, search: string, page: number) {
    return this.http.get<UserPage>(this.url, { params: { category, search, page, pageSize: 10 } }).pipe(timeout(15000));
  }
  details(id: number) { return this.http.get<ManagedUser>(`${this.url}/${id}`).pipe(timeout(15000)); }
  create(request: CreateUser) {
    return this.auth.refreshCsrf().pipe(switchMap(() => this.http.post<ManagedUser>(this.url, request)), timeout(15000));
  }
  change(user: ManagedUser, status: AccountStatus, role: Role) {
    return this.auth.refreshCsrf().pipe(switchMap(() => this.http.patch<ManagedUser>(`${this.url}/${user.userId}`, {
      expectedStatus: user.status, expectedRole: user.role, expectedVersion: user.version,
      status, role, confirmed: true,
    })), timeout(15000));
  }
}
