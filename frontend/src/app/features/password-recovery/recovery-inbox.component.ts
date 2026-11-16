import { DatePipe } from '@angular/common';
import { afterNextRender, Component, DestroyRef, ElementRef, HostListener, Injector, OnDestroy, ViewChild, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Router, RouterLink } from '@angular/router';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Subscription, finalize } from 'rxjs';
import { UserFeedbackDialogComponent } from '../users/shared/user-feedback-dialog.component';
import { RecoveryPage, RecoveryRequest, RecoveryService, ResetCode, recoveryError } from './recovery.service';

@Component({
  selector: 'app-recovery-inbox', standalone: true, imports: [DatePipe, RouterLink, ReactiveFormsModule, UserFeedbackDialogComponent],
  templateUrl: './recovery-inbox.component.html',
  styleUrls: ['../users/users.component.scss', '../users/users-atmosphere.scss', './recovery-inbox.component.scss', './recovery-dialogs.scss'],
})
export class RecoveryInboxComponent implements OnDestroy {
  readonly api = inject(RecoveryService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly injector = inject(Injector);
  @ViewChild(UserFeedbackDialogComponent) private dialogs!: UserFeedbackDialogComponent;
  @ViewChild('requestDialog') private requestDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('codeDialog') private codeDialog!: ElementRef<HTMLDialogElement>;
  @ViewChild('resolveDialog') private resolveDialog!: ElementRef<HTMLDialogElement>;
  readonly resolveUsername = new FormControl('', { nonNullable: true, validators: [Validators.required, Validators.maxLength(50), Validators.pattern(/\S/)] });
  readonly resolveVerified = signal(false);
  readonly resolveError = signal('');
  readonly result = signal<RecoveryPage | null>(null);
  readonly selected = signal<RecoveryRequest | null>(null);
  readonly issued = signal<ResetCode | null>(null);
  readonly issuedFor = signal('');
  readonly copied = signal(false);
  readonly copyError = signal('');
  readonly loading = signal(false);
  readonly detailLoading = signal(false);
  readonly saving = signal(false);
  readonly error = signal('');
  readonly detailError = signal('');
  readonly verified = signal(false);
  readonly statuses = [
    { key: 'pending', label: 'Pending', detail: 'Waiting for your review' },
    { key: 'completed', label: 'Completed', detail: 'Code issued or request closed' },
    { key: 'denied', label: 'Denied', detail: 'Requests you declined' },
  ];
  status = 'pending'; page = 1;
  categoryLabel(request: RecoveryRequest) { return request.status === 'PENDING' ? 'Pending' : request.status === 'DENIED' ? 'Denied' : 'Completed'; }
  outcome(request: RecoveryRequest) { return ({ PENDING: 'Awaiting identity check', APPROVED: 'Code issued · awaiting password reset', COMPLETED: 'Password successfully reset', DENIED: 'No code was issued', EXPIRED: 'Request or code expired' })[request.status]; }
  private listRequest?: Subscription;
  private detailRequest?: Subscription;
  private destroyed = false;
  constructor() { this.load(); }
  pages() { return Math.max(1, Math.ceil((this.result()?.total ?? 0) / 10)); }
  load(refreshCount = true) {
    if (this.saving() || this.issued()) return;
    this.listRequest?.unsubscribe(); this.loading.set(true); this.error.set('');
    if (refreshCount) this.api.refreshPendingCount();
    this.listRequest = this.api.inbox(this.status, this.page).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.loading.set(false)))
      .subscribe({ next: result => {
        if (!result.items.length && this.page > 1) { this.page--; this.load(false); return; }
        this.result.set(result);
      }, error: e => { this.result.set(null); this.fail(e); } });
  }
  filter(value: string) { this.status = value; this.page = 1; this.load(); }
  paginate(step: number) { this.page += step; this.load(); }
  view(request: RecoveryRequest) {
    if (this.saving() || this.detailLoading() || this.issued()) return;
    this.detailRequest?.unsubscribe(); this.detailLoading.set(true); this.detailError.set(''); this.error.set('');
    this.detailRequest = this.api.details(request.requestId).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.detailLoading.set(false)))
      .subscribe({ next: details => {
        this.selected.set(details); this.verified.set(false);
        afterNextRender(() => { this.requestDialog.nativeElement.showModal(); this.requestDialog.nativeElement.querySelector<HTMLElement>('h2')?.focus({ preventScroll: true }); this.requestDialog.nativeElement.scrollTop = 0; }, { injector: this.injector });
      }, error: e => this.fail(e) });
  }
  closeDetails(event?: Event) {
    event?.preventDefault();
    if (this.saving()) return;
    this.requestDialog.nativeElement.close(); this.selected.set(null); this.verified.set(false); this.detailError.set('');
  }
  async approve() {
    const request = this.selected();
    if (!request || request.status !== 'PENDING' || !this.verified() || this.saving() || this.dialogs.opened()) return;
    if (!await this.dialogs.confirm({ title: 'Approve password recovery?',
      message: `You have verified ${request.fullName}'s identity outside WoodFlow.`,
      detail: 'A single-use code will appear once. Give it privately to this person. It expires in 10 minutes.', confirmLabel: 'Approve & issue code' })) return;
    if (this.destroyed) return;
    this.saving.set(true); this.detailError.set('');
    this.api.approve(request.requestId).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false))).subscribe({
      next: code => {
        this.saving.set(false); this.closeDetails(); this.status = 'completed'; this.page = 1; this.load(false);
        if (document.hidden) { this.error.set('Approval completed while this page was hidden. The code was discarded for privacy. It cannot be retrieved; wait for expiry before requesting another.'); return; }
        this.issued.set(code); this.issuedFor.set(`${request.fullName} (${request.username})`); this.copied.set(false); this.copyError.set('');
        afterNextRender(() => {
          if (!this.issued()) return;
          this.codeDialog.nativeElement.showModal(); this.codeDialog.nativeElement.querySelector<HTMLElement>('h2')?.focus({ preventScroll: true }); this.codeDialog.nativeElement.scrollTop = 0;
        }, { injector: this.injector });
      }, error: e => this.fail(e, true),
    });
  }
  async deny(request = this.selected()) {
    if (!request || request.status !== 'PENDING' || this.saving() || this.dialogs.opened()) return;
    if (!await this.dialogs.confirm({ title: 'Deny this recovery request?', tone: 'danger',
      message: `No reset code will be issued for ${request.fullName} (${request.username}).`,
      detail: 'Their password stays unchanged. They may submit a new request later if assistance is still needed.', confirmLabel: 'Deny request' })) return;
    if (this.destroyed) return;
    this.saving.set(true); this.error.set(''); this.detailError.set('');
    this.api.deny(request.requestId).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false))).subscribe({
      next: () => {
        this.saving.set(false); this.closeDetails(); this.load(false);
        void this.dialogs.notify({ title: 'Request denied', message: 'The recovery request has been denied. No password or account access was changed.' });
      }, error: e => this.fail(e, !!this.selected()),
    });
  }
  async copyCode() {
    const issued = this.issued(); if (!issued) return;
    try {
      await navigator.clipboard.writeText(issued.code);
      if (this.issued() === issued) { this.copied.set(true); this.copyError.set(''); }
    } catch { this.copyError.set('Clipboard access is unavailable. Select the code and copy it manually, then share it privately.'); }
  }
  closeCode(event?: Event) {
    event?.preventDefault(); this.issued.set(null); this.issuedFor.set(''); this.copied.set(false); this.copyError.set('');
    this.codeDialog?.nativeElement.close();
  }
  finishCode(event?: Event) {
    const hadCode = !!this.issued(); this.closeCode(event);
    if (hadCode && !this.destroyed) void this.dialogs.notify({ title: 'Your recovery task is complete',
      message: 'The request is approved and the reset code has been issued. You will find it under Completed.',
      detail: 'The requester must still use the code to set their new password before it expires. The code cannot be displayed again.' });
  }
  openResolution() {
    if (this.saving() || this.issued()) return;
    this.resolveUsername.reset(); this.resolveVerified.set(false); this.resolveError.set('');
    this.resolveDialog.nativeElement.showModal();
  }
  closeResolution(event?: Event) {
    event?.preventDefault(); if (this.saving()) return;
    this.resolveDialog.nativeElement.close(); this.resolveUsername.reset(); this.resolveVerified.set(false);
  }
  async resolvePending(resolution: 'REASSIGN_TO_ME' | 'DENY') {
    if (this.saving() || this.dialogs.opened() || !this.resolveVerified()) return;
    this.resolveUsername.markAsTouched(); if (this.resolveUsername.invalid) return;
    const username = this.resolveUsername.value.trim();
    if (!await this.dialogs.confirm({ title: resolution === 'DENY' ? 'Close the pending request?' : 'Take over this pending request?',
      message: `You have verified ${username}'s identity outside WoodFlow.`,
      detail: 'Only a pending request can be resolved. Approved codes and passwords will not be changed.',
      confirmLabel: resolution === 'DENY' ? 'Close pending request' : 'Assign to me' })) return;
    if (this.destroyed) return;
    this.saving.set(true); this.resolveError.set('');
    this.api.resolvePending(username, resolution).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.saving.set(false)))
      .subscribe({ next: () => {
        this.saving.set(false); this.closeResolution(); this.status = resolution === 'DENY' ? 'denied' : 'pending'; this.page = 1; this.load(false);
        void this.dialogs.notify({ title: 'Pending request resolved', message: resolution === 'DENY'
          ? 'The pending request has been closed. No code was issued.' : 'The request is now assigned to you. Open it to review and approve recovery.',
          detail: 'Existing passwords and any previously approved recovery codes are unchanged.' });
      }, error: e => {
        if (e.status === 401 || e.status === 403) { this.resolveDialog.nativeElement.close(); this.fail(e); return; }
        this.resolveError.set('Unable to resolve this request. Check the exact username. Only another active user’s pending, unexpired request can be resolved.');
      } });
  }
  @HostListener('window:pagehide') hideCode() { this.closeCode(); }
  @HostListener('document:visibilitychange') visibilityChanged() { if (document.hidden) this.closeCode(); }
  private fail(error: any, inDetails = false) {
    if (error.status === 401 || error.status === 403) {
      this.closeCode(); this.requestDialog?.nativeElement.close(); this.selected.set(null);
      void this.router.navigateByUrl(error.status === 401 ? '/login' : '/dashboard'); return;
    }
    (inDetails ? this.detailError : this.error).set(recoveryError(error));
    this.api.refreshPendingCount();
  }
  ngOnDestroy() { this.destroyed = true; this.closeCode(); this.selected.set(null); }
}
