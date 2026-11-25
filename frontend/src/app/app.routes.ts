import { Routes } from '@angular/router';
import { authGuard, guestGuard, ownerGuard } from './core/auth/auth.guards';
import { permissionGuard } from './core/auth/permission.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'splash' },
  { path: 'splash', title: 'WoodFlow', loadComponent: () => import('./features/startup/startup.component').then(m => m.StartupComponent) },
  { path: 'loading', title: 'Opening WoodFlow', data: { loading: true }, loadComponent: () => import('./features/startup/startup.component').then(m => m.StartupComponent) },
  { path: 'login', title: 'Sign In | WoodFlow', canActivate: [guestGuard], loadComponent: () => import('./features/auth/login/login.component').then(m => m.LoginComponent) },
  { path: 'forgot-password', title: 'Password Help | WoodFlow', loadComponent: () => import('./features/password-recovery/recovery-public.component').then(m => m.RecoveryPublicComponent) },
  { path: 'reset-password', title: 'Set New Password | WoodFlow', data: { resetting: true }, loadComponent: () => import('./features/password-recovery/recovery-public.component').then(m => m.RecoveryPublicComponent) },
  { path: 'initial-setup', title: 'First Owner Setup | WoodFlow', canActivate: [guestGuard], loadComponent: () => import('./features/auth/initial-setup/initial-setup.component').then(m => m.InitialSetupComponent) },
  {
    path: '',
    canActivateChild: [authGuard],
    loadComponent: () => import('./layouts/shell/shell.component').then(m => m.ShellComponent),
    children: [
      {
        path: 'users/password-recovery', title: 'Password Reset Requests | WoodFlow', canActivate: [ownerGuard],
        loadComponent: () => import('./features/password-recovery/recovery-inbox.component').then(m => m.RecoveryInboxComponent),
      },
      {
        path: 'users/:userId/permissions', title: 'Assistant Access | WoodFlow', canActivate: [ownerGuard],
        loadComponent: () => import('./features/users/permissions/permissions.component').then(m => m.PermissionsComponent),
      },
      {
        path: 'users/permissions', title: 'Assistant Permissions | WoodFlow', canActivate: [ownerGuard],
        loadComponent: () => import('./features/users/permissions/permissions.component').then(m => m.PermissionsComponent),
      },
      {
        path: 'access-restricted', title: 'Access Restricted | WoodFlow',
        loadComponent: () => import('./features/users/permissions/access-restricted.component').then(m => m.AccessRestrictedComponent),
      },
      {
        path: 'users', title: 'Users & Roles | WoodFlow', canActivate: [ownerGuard],
        loadComponent: () => import('./features/users/users.component').then(m => m.UsersComponent),
      },
      {
        path: 'dashboard',
        canActivate: [permissionGuard], data: { permission: 'dashboard.view' },
        title: 'Dashboard | WoodFlow',
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent),
      },
      {
        path: 'inventory/add',
        canActivate: [permissionGuard], data: { permission: 'inventory.create' },
        title: 'Add Furniture Item | WoodFlow',
        loadComponent: () => import('./features/inventory/furniture-item-editor.component').then(m => m.FurnitureItemEditorComponent),
      },
      {
        path: 'inventory/edit/:id',
        canActivate: [permissionGuard], data: { permission: 'inventory.edit' },
        title: 'Edit Furniture Item | WoodFlow',
        loadComponent: () => import('./features/inventory/furniture-item-editor.component').then(m => m.FurnitureItemEditorComponent),
      },
      {
        path: 'inventory/movements',
        canActivate: [permissionGuard], data: { permission: 'inventory.view' },
        title: 'Stock Movement Ledger | WoodFlow',
        loadComponent: () => import('./features/inventory/stock-movement-ledger.component').then(m => m.StockMovementLedgerComponent),
      },
      {
        path: 'inventory/audit',
        canActivate: [permissionGuard], data: { permission: 'inventory.view' },
        title: 'Stock Audit & Adjustment | WoodFlow',
        loadComponent: () => import('./features/inventory/stock-audit-adjustment.component').then(m => m.StockAuditAdjustmentComponent),
      },
      {
        path: 'inventory',
        canActivate: [permissionGuard], data: { permission: 'inventory.view' },
        title: 'Finished Furniture Inventory | WoodFlow',
        loadComponent: () => import('./features/inventory/showroom-catalog.component').then(m => m.ShowroomCatalogComponent),
      },
    ],
  },
  { path: '**', redirectTo: 'dashboard' },
];
