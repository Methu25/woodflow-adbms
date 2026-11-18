import { Component, DestroyRef, OnDestroy, ViewChild, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthLayoutComponent } from '../../layouts/auth/auth-layout.component';
import { PasswordFieldComponent } from '../../shared/password-field/password-field.component';
import { UserFeedbackDialogComponent } from '../users/shared/user-feedback-dialog.component';
import { RecoveryContact, RecoveryService, recoveryError } from './recovery.service';

@Component({
  selector: 'app-recovery-public', standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthLayoutComponent, PasswordFieldComponent, UserFeedbackDialogComponent],
  templateUrl: './recovery-public.component.html', styleUrl: './recovery-public.component.scss',
})
export class RecoveryPublicComponent implements OnDestroy {
  private readonly api = inject(RecoveryService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);
  private destroyed = false;
  readonly resetting = !!inject(ActivatedRoute).snapshot.data['resetting'];
  readonly pending = signal(false);
  readonly contactsLoading = signal(false);
  readonly contactsError = signal('');
  readonly contacts = signal<RecoveryContact[]>([]);
  readonly error = signal('');
  readonly done = signal(false);
  @ViewChild(UserFeedbackDialogComponent) private dialogs!: UserFeedbackDialogComponent;
  readonly requestForm = this.fb.group({
    username: ['', [Validators.required, Validators.maxLength(50), Validators.pattern(/\S/)]],
    ownerId: [0, Validators.min(1)],
  });
  readonly resetForm = this.fb.group({
    code: ['', [Validators.required, Validators.pattern(/^[A-Fa-f0-9]{64}$/)]],
    newPassword: ['', [Validators.required, Validators.minLength(8), Validators.maxLength(128), Validators.pattern(/\S/)]],
    confirmPassword: ['', Validators.required],
  });
  constructor() { if (!this.resetting) this.loadContacts(); }
  loadContacts() {
    if (this.contactsLoading()) return;
    this.contactsLoading.set(true); this.contactsError.set('');
    this.api.owners().pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.contactsLoading.set(false))).subscribe({
      next: contacts => this.contacts.set(contacts), error: e => this.contactsError.set(recoveryError(e)),
    });
  }
  invalid(name: 'username' | 'ownerId') { const c = this.requestForm.controls[name]; return c.touched && c.invalid; }
  resetError(name: 'code' | 'newPassword' | 'confirmPassword') {
    const c = this.resetForm.controls[name];
    if (this.pending() || !c.touched) return '';
    if (name === 'confirmPassword' && c.value !== this.resetForm.controls.newPassword.value) return 'The passwords must match.';
    if (!c.invalid) return '';
    return name === 'code' ? 'Enter the complete 64-character code supplied by your owner.'
      : name === 'newPassword' ? 'Use 8–128 characters; spaces alone are not a password.' : 'Confirm your new password.';
  }
  strength() {
    const value = this.resetForm.controls.newPassword.value;
    return value.length >= 16 ? 'Long passphrase' : value.length >= 12 ? 'Good length' : value.length >= 8 ? 'Minimum length met' : 'Use at least 8 characters';
  }
  submitRequest() {
    if (this.pending() || this.done() || this.contactsLoading() || !this.contacts().length) return;
    this.requestForm.markAllAsTouched(); if (this.requestForm.invalid) return;
    this.pending.set(true); this.error.set('');
    const { username, ownerId } = this.requestForm.getRawValue();
    this.api.submit(username.trim(), Number(ownerId)).pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.pending.set(false))).subscribe({
      next: () => {
        this.requestForm.reset(); this.done.set(true);
        void this.dialogs.notify({ title: 'Recovery request submitted',
          message: 'Your password recovery request has been submitted. Please wait for the selected owner to contact you with further instructions.',
          detail: 'For privacy, this confirmation does not confirm that an account exists. Your current password remains unchanged.' });
      }, error: e => this.error.set(recoveryError(e)),
    });
  }
  submitReset() {
    if (this.pending() || this.done()) return;
    this.resetForm.markAllAsTouched();
    if (this.resetForm.invalid || this.resetForm.controls.newPassword.value !== this.resetForm.controls.confirmPassword.value) return;
    this.pending.set(true); this.error.set('');
    const values = this.resetForm.getRawValue();
    const request = this.api.complete(values.code, values.newPassword, values.confirmPassword);
    this.resetForm.reset(); // Remove secrets and any revealed password before the request finishes.
    request.pipe(takeUntilDestroyed(this.destroyRef), finalize(() => this.pending.set(false))).subscribe({
      next: async () => {
        this.done.set(true);
        await this.dialogs.notify({ title: 'Password updated', message: 'Your password has been reset successfully. You can now log in with your new password.',
          detail: 'Previous sessions have been signed out. Select OK to return to login.' });
        if (!this.destroyed) void this.router.navigateByUrl('/login', { replaceUrl: true });
      }, error: e => this.error.set(recoveryError(e, true)),
    });
  }
  ngOnDestroy() { this.destroyed = true; this.requestForm.reset(); this.resetForm.reset(); }
}
