import { Component, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { authErrorMessage } from '../../core/auth/auth-errors';
import { PermissionsService } from '../../core/auth/permissions.service';

@Component({
  selector: 'app-shell',
  standalone: true,
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  styleUrl: './shell.component.scss',
  template: `
    <header>
      <div class="brand"><span class="brand-mark">W</span><div><h1>Wimalasiri Furniture</h1><p>Furniture ERP</p></div></div>
      <p class="menu-label">MAIN MENU</p>
      <nav aria-label="Main navigation">
        @if (permissions.has('dashboard.view')) {
          <a routerLink="/dashboard" routerLinkActive="active" ariaCurrentWhenActive="page"><img src="/assets/navigation/dashboard.svg" alt="" />Dashboard</a>
        }
        @for (item of plannedModules; track item.icon) {
          @if (!item.ownerOnly || auth.user()?.role === 'OWNER') {
            @if (item.icon === 'users') {
              <a routerLink="/users" routerLinkActive="active" ariaCurrentWhenActive="page"><img src="/assets/navigation/users.svg" alt="" />{{ item.label }}</a>
            } @else if (item.icon === 'inventory' && permissions.has('inventory.view')) {
              <a routerLink="/inventory" routerLinkActive="active" ariaCurrentWhenActive="page"><img src="/assets/navigation/inventory.svg" alt="" />{{ item.label }}</a>
            } @else {
            <button class="menu-item" type="button" disabled [title]="item.label + ' is not available yet'"><img [src]="'/assets/navigation/' + item.icon + '.svg'" alt="" />{{ item.label }}</button>
            }
          }
        }
      </nav>
      <div class="sidebar-footer"><p class="account-name">{{ auth.user()?.fullName }}</p>
        <button class="logout" type="button" (click)="logout()" [disabled]="pending()"><img src="/assets/navigation/logout.svg" alt="" />{{ pending() ? 'Signing out…' : 'Logout' }}</button>
        <p class="showroom">WoodFlow · Kottawa Showroom</p>
      </div>
      @if (error()) { <p role="alert">{{ error() }}</p> }
    </header>
    <main><router-outlet /></main>
  `,
})
export class ShellComponent {
  readonly plannedModules = [
    { label: 'Inventory', icon: 'inventory', ownerOnly: false },
    { label: 'Sales & POS', icon: 'sales', ownerOnly: false },
    { label: 'Custom Orders', icon: 'orders', ownerOnly: false },
    { label: 'Customers', icon: 'customers', ownerOnly: false },
    { label: 'Production', icon: 'production', ownerOnly: false },
    { label: 'Finance', icon: 'finance', ownerOnly: false },
    { label: 'Employees', icon: 'employees', ownerOnly: false },
    { label: 'Users & Roles', icon: 'users', ownerOnly: true },
    { label: 'Settings', icon: 'settings', ownerOnly: true },
  ];
  readonly auth = inject(AuthService);
  readonly permissions = inject(PermissionsService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly pending = signal(false);
  readonly error = signal('');

  constructor() {
    this.permissions.refresh().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({ error: () => {} });
  }

  logout(): void {
    if (this.pending()) return;
    this.pending.set(true);
    this.error.set('');
    this.auth.logout().pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.pending.set(false)),
    ).subscribe({
      next: () => void this.router.navigateByUrl('/login', { replaceUrl: true }),
      error: error => this.error.set(authErrorMessage(error, 'logout')),
    });
  }
}
