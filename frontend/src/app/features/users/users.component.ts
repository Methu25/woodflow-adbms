import { DatePipe } from '@angular/common';
import { HttpErrorResponse } from '@angular/common/http';
import { Component, DestroyRef, ElementRef, ViewChild, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { Subscription, finalize } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { AccountStatus, Category, Counts, ManagedUser, Role, UserPage, UsersService } from './users.service';
import { PermissionAdminService, PermissionMatrix, PermissionModule } from './permissions/permissions.service';
import { UserFeedback, UserFeedbackDialogComponent } from './shared/user-feedback-dialog.component';
import { RecoveryService } from '../password-recovery/recovery.service';

@Component({
  selector: 'app-users', standalone: true, imports: [DatePipe, ReactiveFormsModule, RouterLink, UserFeedbackDialogComponent],
  templateUrl: './users.component.html', styleUrls: ['./users.component.scss', './users-editor.scss', './users-atmosphere.scss', './users-tools.scss'],
})
export class UsersComponent {
  private readonly api = inject(UsersService);
  private readonly permissionsApi = inject(PermissionAdminService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly auth = inject(AuthService);
  readonly recovery = inject(RecoveryService);
  @ViewChild('detailsDialog') private dialog?: ElementRef<HTMLDialogElement>;
  @ViewChild(UserFeedbackDialogComponent) private dialogs!: UserFeedbackDialogComponent;
  readonly category = signal<Category>('owners');
  readonly creating = signal(false);
  readonly counts = signal<Counts | null>(null);
  readonly result = signal<UserPage | null>(null);
  readonly selected = signal<ManagedUser | null>(null);
  readonly loading = signal(false);
  readonly saving = signal(false);
  readonly detailLoading = signal(false);
  readonly selectedPermissions = signal<PermissionMatrix | null>(null);
  readonly permissionsLoading = signal(false);
  readonly permissionError = signal('');
  private permissionRequest?: Subscription;
  readonly error = signal('');
  readonly success = signal('');
  readonly categories: { key: Category; label: string; count: keyof Counts }[] = [
    { key: 'owners', label: 'Active Owners', count: 'activeOwners' },
    { key: 'assistants', label: 'Active Assistants', count: 'activeAssistants' },
    { key: 'suspended', label: 'Suspended Accounts', count: 'suspended' },
    { key: 'deactivated', label: 'Deactivated Accounts', count: 'deactivated' },
  ];
  readonly form = inject(FormBuilder).nonNullable.group({
    fullName: ['', [Validators.required, Validators.maxLength(100), Validators.pattern(/.*\S.*/)]],
    username: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9_.-]{3,50}$/)]],
    password: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(128), Validators.pattern(/[\s\S]*\S[\s\S]*/)]],
    role: ['ASSISTANT' as Role, Validators.required],
  });
  readonly roleControl = inject(FormBuilder).nonNullable.control<Role>('ASSISTANT');
  search = '';
  private page = 1;
  private listRequest?: Subscription;
  private countRequest?: Subscription;
  @ViewChild('feedback') private feedback?: ElementRef<HTMLElement>;

  constructor() { this.refreshCounts(); this.load(); this.recovery.refreshPendingCount(); }
  title() { return this.categories.find(c => c.key === this.category())!.label; }
  pages() { const r = this.result(); return r ? Math.max(1, Math.ceil(r.total / r.pageSize)) : 1; }
  own(user: ManagedUser) { return user.userId === this.auth.user()?.userId; }
  initials(name: string) { return name.trim().split(/\s+/).slice(0, 2).map(part => part[0]).join('').toUpperCase(); }
  invalid(name: 'fullName' | 'username' | 'password') {
    return !this.saving() && this.form.controls[name].touched && this.form.controls[name].invalid;
  }
  select(category: Category) {
    if (this.saving()) return;
    this.creating.set(false); this.form.controls.password.reset(); this.category.set(category);
    this.page = 1; this.search = ''; this.success.set(''); this.load();
  }
  createView() {
    if (this.saving()) return;
    this.creating.set(true); this.error.set(''); this.success.set(''); this.form.reset();
  }
  searchUsers(value: string) { this.search = value; this.page = 1; this.load(); }
  paginate(step: number) { this.page += step; this.load(); }
  load() {
    this.listRequest?.unsubscribe(); this.loading.set(true); this.error.set(''); this.result.set(null);
    this.listRequest = this.api.list(this.category(), this.search, this.page).pipe(
      takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)),
    ).subscribe({ next: r => { this.result.set(r); this.page = r.page; }, error: e => this.fail(e) });
  }
  refreshCounts() {
    this.countRequest?.unsubscribe();
    this.countRequest = this.api.counts().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: c => this.counts.set(c),
      error: e => { this.counts.set(null); this.fail(e); this.revealFeedback(); },
    });
  }
  async create() {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.saving() || this.dialogs.opened()) return;
    if (this.form.controls.role.value === 'OWNER' && !await this.dialogs.confirm({
      title: 'Create an OWNER account?', tone: 'info', confirmLabel: 'Create OWNER',
      message: 'This account will have full access to WoodFlow, including managing users, roles and permissions.',
      detail: 'Choose OWNER only for a trusted business owner or administrator. For daily business work, choose ASSISTANT instead.',
    })) return;
    this.saving.set(true); this.error.set(''); this.success.set('');
    const request = this.form.getRawValue();
    // Never retain the submitted password in component state after submission.
    this.form.controls.password.reset();
    this.api.create(request).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false))).subscribe({
      next: user => {
        this.form.reset(); this.creating.set(false); this.category.set(user.role === 'OWNER' ? 'owners' : 'assistants');
        this.search = ''; this.page = 1; this.load(); this.refreshCounts(); this.success.set('User created successfully.');
        this.showResult({ title: 'Account created', message: `${user.fullName}'s account is ready.`,
          detail: user.role === 'OWNER' ? 'This OWNER account has full administrative access. They can now sign in with their account credentials.' : 'This ASSISTANT can now sign in. Their access follows the role defaults unless you set individual permissions.' });
      }, error: e => { this.fail(e); this.showMutationError(e); },
    });
  }
  view(user: ManagedUser) {
    if (this.detailLoading()) return;
    this.detailLoading.set(true); this.error.set('');
    this.api.details(user.userId).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.detailLoading.set(false))).subscribe({
      next: details => {
        this.selected.set(details); this.roleControl.setValue(details.role); this.dialog?.nativeElement.showModal();
        this.loadPermissionDetails(details);
      },
      error: e => this.fail(e),
    });
  }
  async change(user: ManagedUser, status: AccountStatus, role: Role = user.role) {
    if (this.saving() || this.dialogs.opened()) return;
    const roleChange = role !== user.role;
    const tone = roleChange ? 'info' : status === 'DEACTIVATED' ? 'danger' : status === 'SUSPENDED' ? 'warning' : 'info';
    const title = roleChange ? `Change role to ${role}?` : status === 'SUSPENDED' ? 'Suspend this account?' : status === 'DEACTIVATED' ? 'Permanently deactivate this account?' : 'Reactivate this account?';
    const detail = roleChange ? role === 'OWNER'
      ? 'OWNER gives full administrative access, including users and permissions. Existing sessions will be signed out.'
      : 'ASSISTANT has only assigned business permissions and cannot manage accounts. Existing sessions will be signed out.'
      : status === 'SUSPENDED' ? 'Access will be paused and existing sessions signed out. You can reactivate this account later.'
      : status === 'DEACTIVATED' ? 'This cannot be undone through WoodFlow. The account cannot sign in again; business history will be retained.'
      : 'This person can sign in again with their assigned role and permissions. Previous sessions stay signed out.';
    if (!await this.dialogs.confirm({ title, tone, message: `You are updating ${user.fullName} (${user.username}).`, detail,
      confirmLabel: roleChange ? 'Change role' : status === 'DEACTIVATED' ? 'Deactivate permanently' : status === 'SUSPENDED' ? 'Suspend account' : 'Reactivate account' })) return;
    this.saving.set(true); this.error.set(''); this.success.set('');
    this.api.change(user, status, role).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.closeDetails(); this.load(); this.refreshCounts(); this.success.set('Account updated successfully.'); this.revealFeedback();
        this.showResult({ tone, title: roleChange ? 'Role updated' : status === 'SUSPENDED' ? 'Account suspended' : status === 'DEACTIVATED' ? 'Account permanently deactivated' : 'Account reactivated',
          message: roleChange ? `${user.fullName} is now an ${role}.` : status === 'SUSPENDED' ? `${user.fullName}'s access is temporarily paused.` : status === 'DEACTIVATED' ? `${user.fullName} can no longer sign in.` : `${user.fullName} can sign in again.`,
          detail: roleChange ? `${role === 'OWNER' ? 'They now have full administrative access.' : 'Their access is limited to assigned business permissions.'} They will need to sign in again.` : status === 'SUSPENDED' ? 'You can reactivate this account whenever access should be restored. Existing sessions have been signed out.' : status === 'DEACTIVATED' ? 'This change is permanent. Existing business records are kept safely for history.' : 'Their assigned role and permissions are unchanged. They can start a new sign-in session.' });
      },
      error: e => {
        // Keep failures visible in the open account panel instead of hiding them above the viewport.
        this.fail(e); this.revealFeedback();
        this.showMutationError(e);
        if (e.status === 409) this.refreshCounts();
      },
    });
  }
  loadPermissionDetails(user: ManagedUser) {
    this.permissionRequest?.unsubscribe(); this.selectedPermissions.set(null); this.permissionError.set('');
    if (user.role !== 'ASSISTANT') { this.permissionsLoading.set(false); return; }
    this.permissionsLoading.set(true);
    this.permissionRequest = this.permissionsApi.load(user.userId).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.permissionsLoading.set(false))).subscribe({
      next: result => this.selectedPermissions.set(result),
      error: e => {
        if (e.status === 401 || e.status === 403) { this.closeDetails(); this.fail(e); return; }
        this.permissionError.set('Unable to load current access. Retry, or refresh the account if its role changed.');
      },
    });
  }
  allowedActions(matrix: PermissionMatrix, module: PermissionModule) {
    return module.permissions.filter(p => matrix.grantedKeys.includes(p.key)).map(p => p.action).join(', ') || 'No access';
  }
  openPermissions(user: ManagedUser) { this.closeDetails(); void this.router.navigate(['/users', user.userId, 'permissions']); }
  closeDetails() {
    this.permissionRequest?.unsubscribe(); this.selectedPermissions.set(null); this.permissionError.set('');
    this.dialog?.nativeElement.close(); this.selected.set(null);
  }
  private revealFeedback() {
    if (this.dialogs?.opened()) return;
    requestAnimationFrame(() => {
      const element = this.dialog?.nativeElement.open
        ? this.dialog.nativeElement.querySelector<HTMLElement>('.save-feedback') : this.feedback?.nativeElement;
      if (element?.isConnected) { element.scrollIntoView({ block: 'nearest' }); element.focus({ preventScroll: true }); }
    });
  }
  private showResult(options: UserFeedback) { void this.dialogs.notify(options).then(() => this.revealFeedback()); }
  private showMutationError(error: HttpErrorResponse) {
    if (error.status === 401 || error.status === 403) return;
    void this.dialogs.notify({ title: 'We couldn’t complete that change', tone: 'danger',
      message: this.error(), detail: 'Select OK to review the account. If the connection was interrupted, refresh the latest details before trying again.' }, 'error').then(() => this.revealFeedback());
  }
  private fail(error: unknown) {
    if (error instanceof HttpErrorResponse) {
      if (error.status === 401) { void this.router.navigateByUrl('/login'); return; }
      if (error.status === 403) { void this.router.navigateByUrl('/dashboard'); return; }
      if ([400, 404, 409].includes(error.status)) {
        this.error.set(error.error?.message || 'The account could not be updated. Check your input and refresh.'); return;
      }
      if (error.status === 429) { this.error.set('Too many requests. Please wait and try again.'); return; }
    }
    this.error.set('User management is temporarily unavailable. Check the backend and required migration, then retry.');
  }
}
