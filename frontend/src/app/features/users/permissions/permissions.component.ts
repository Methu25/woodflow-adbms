import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ViewChild, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, Subscription } from 'rxjs';
import { PermissionAdminService, PermissionDefinition, PermissionMatrix, PermissionModule } from './permissions.service';
import { UserFeedbackDialogComponent } from '../shared/user-feedback-dialog.component';

@Component({
  selector: 'app-permissions', standalone: true, imports: [RouterLink, UserFeedbackDialogComponent], templateUrl: './permissions.component.html',
  styleUrls: ['../users.component.scss', '../users-atmosphere.scss', './permissions.component.scss'],
})
export class PermissionsComponent {
  private readonly api = inject(PermissionAdminService);
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private loadRequest?: Subscription;
  private saveRequest?: Subscription;
  private readonly destroyRef = inject(DestroyRef);
  @ViewChild(UserFeedbackDialogComponent) private dialogs!: UserFeedbackDialogComponent;
  readonly matrix = signal<PermissionMatrix | null>(null);
  readonly grants = signal<Set<string>>(new Set());
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly success = signal('');
  readonly conflict = signal(false);
  readonly actions = ['view', 'create', 'edit', 'delete'];
  constructor() { this.route.paramMap.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => {
    this.dialogs?.finish(false);
    this.saveRequest?.unsubscribe(); this.saving.set(false); this.load(true);
  }); }
  get userId() { const value = this.route.snapshot.paramMap.get('userId'); return value ? Number(value) : undefined; }
  readonlyAccount() { return this.matrix()?.user?.status === 'DEACTIVATED'; }
  async load(routeChanged = false) {
    if (!routeChanged && (this.saving() || this.loading() || this.dialogs?.opened())) return;
    if (!routeChanged && this.dirty() && !await this.dialogs.confirm({
      title: 'Reload the latest permissions?', tone: 'warning', confirmLabel: 'Reload settings', cancelLabel: 'Keep editing',
      message: 'You have unsaved changes in this matrix.', detail: 'Reloading discards your edits and shows the latest saved settings. No saved permissions will be changed.',
    })) return;
    this.loadRequest?.unsubscribe();
    if (routeChanged) this.matrix.set(null);
    this.loading.set(true); this.error.set(''); this.success.set('');
    this.loadRequest = this.api.load(this.userId).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false))).subscribe({
      next: result => { this.matrix.set(result); this.grants.set(new Set(result.grantedKeys)); this.conflict.set(false); },
      error: e => this.fail(e),
    });
  }
  definition(module: PermissionModule, action: string) { return module.permissions.find(p => p.action === action); }
  toggle(module: PermissionModule, permission: PermissionDefinition, enabled: boolean) {
    if (this.saving() || this.conflict() || this.readonlyAccount()) return;
    const next = new Set(this.grants());
    if (enabled) { next.add(permission.key); next.add(`${module.key}.view`); }
    else {
      next.delete(permission.key);
      if (permission.action === 'view') module.permissions.forEach(p => next.delete(p.key));
    }
    this.grants.set(next); this.success.set('');
  }
  dirty() { const saved = this.matrix()?.grantedKeys; return !!saved && (saved.length !== this.grants().size || saved.some(k => !this.grants().has(k))); }
  async reset() {
    if (this.saving() || !this.dirty() || this.dialogs.opened()) return;
    if (!await this.dialogs.confirm({ title: 'Discard your unsaved changes?', tone: 'warning', confirmLabel: 'Discard changes', cancelLabel: 'Keep editing', message: 'Your current edits have not been saved.', detail: 'The existing saved permissions will stay unchanged.' })) return;
    this.grants.set(new Set(this.matrix()?.grantedKeys ?? [])); this.success.set('Unsaved changes discarded.');
    void this.dialogs.notify({ title: 'Changes discarded', message: 'You are back to the saved permission settings.', detail: 'No account access was changed.' });
  }
  async save(useRoleDefaults = false) {
    const matrix = this.matrix();
    if (!matrix || (!this.dirty() && !useRoleDefaults) || this.saving() || this.conflict() || this.readonlyAccount() || this.dialogs.opened()) return;
    const scope = matrix.user ? `${matrix.user.fullName} (${matrix.user.username}) only` : 'all assistants using the role defaults';
    if (!await this.dialogs.confirm({ title: useRoleDefaults ? 'Restore shared role defaults?' : 'Save these permissions?',
      message: `This change applies to ${scope}.`,
      detail: useRoleDefaults ? 'This removes their individual override. Future role-default changes will apply to this assistant too.' : matrix.user ? 'Only this assistant’s access will change, on their next protected request.' : 'All assistants using role defaults will receive these changes. Individual overrides remain unchanged.',
      confirmLabel: useRoleDefaults ? 'Restore defaults' : 'Save permissions',
    })) return;
    this.saving.set(true); this.error.set(''); this.success.set('');
    const request = matrix.user ? this.api.saveUser(matrix, [...this.grants()], useRoleDefaults) : this.api.save([...this.grants()], matrix.version);
    this.saveRequest = request.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false))).subscribe({
      next: result => {
        this.matrix.set({ ...matrix, ...result }); this.grants.set(new Set(result.grantedKeys));
        this.success.set(`${useRoleDefaults ? 'Role defaults restored.' : 'Permissions saved.'} Existing sessions use the new permissions on their next protected request.`);
        void this.dialogs.notify({ title: useRoleDefaults ? 'Role defaults restored' : matrix.user ? 'Assistant access updated' : 'Shared permissions updated',
          message: useRoleDefaults ? `${matrix.user!.fullName} now uses the shared ASSISTANT defaults.` : matrix.user ? `The new access settings for ${matrix.user.fullName} have been saved.` : 'The shared ASSISTANT access settings have been saved.',
          detail: matrix.user ? 'This assistant will use the new permissions on their next protected request.' : 'The changes apply to assistants using role defaults. Individual overrides have not changed.' });
      }, error: e => {
        this.fail(e);
        if (e.status !== 401 && e.status !== 403) void this.dialogs.notify({ title: 'Permissions couldn’t be saved', tone: 'danger', message: this.error(), detail: 'Select OK to review your selection. Reload the latest settings if another owner or an interrupted connection may have changed them.' }, 'error');
      },
    });
  }
  private fail(error: unknown) {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) { void this.router.navigateByUrl('/login'); return; }
      if (error.status === 403) { void this.router.navigateByUrl('/access-restricted'); return; }
      if (error.status === 409) { this.conflict.set(true); this.error.set('Another OWNER changed these permissions. Reload the latest settings before saving.'); return; }
      if (error.status === 400) { this.error.set('Check your selection. Create, Edit and Delete require View access.'); return; }
      if (error.status === 404) { this.error.set('Assistant account not found. Choose an ASSISTANT from Users & Roles.'); return; }
    }
    this.error.set('Permissions are temporarily unavailable. Please try again shortly.');
  }
}
