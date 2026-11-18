import { Component, DestroyRef, inject, OnDestroy, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { AbstractControl, NonNullableFormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { authErrorMessage } from '../../../core/auth/auth-errors';
import { AuthLayoutComponent } from '../../../layouts/auth/auth-layout.component';
import { PasswordFieldComponent } from '../../../shared/password-field/password-field.component';

export function passwordsMatch(control: AbstractControl): ValidationErrors | null {
  return control.get('password')?.value === control.get('confirmPassword')?.value
    ? null : { passwordMismatch: true };
}

@Component({
  selector: 'app-initial-setup',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthLayoutComponent, PasswordFieldComponent],
  templateUrl: './initial-setup.component.html',
})
export class InitialSetupComponent implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly pending = signal(false);
  readonly error = signal('');
  readonly submitted = signal(false);
  readonly form = this.fb.group({
    fullName: ['', [Validators.required, Validators.maxLength(100), Validators.pattern(/\S/)]],
    username: ['', [Validators.required, Validators.pattern(/^[A-Za-z0-9_.-]{3,50}$/)]],
    password: ['', [Validators.required, Validators.minLength(12), Validators.maxLength(128)]],
    confirmPassword: ['', [Validators.required, Validators.maxLength(128)]],
    setupCode: ['', [Validators.required, Validators.pattern(/^[A-Fa-f0-9]{64}$/)]],
  }, { validators: passwordsMatch });

  fieldError(name: keyof typeof this.form.controls): string {
    const control = this.form.controls[name];
    if (!(control.touched || this.submitted())) return '';
    if (name === 'confirmPassword' && this.form.hasError('passwordMismatch')) return 'Passwords must match.';
    if (!control.invalid) return '';
    return {
      fullName: 'Enter your full name (up to 100 characters).',
      username: 'Use 3–50 letters, numbers, underscores, dots or hyphens.',
      password: 'Use a password between 12 and 128 characters.',
      confirmPassword: 'Confirm your password.',
      setupCode: 'Enter the 64-character one-time code supplied by your administrator.',
    }[name];
  }

  submit(): void {
    if (this.pending()) return;
    this.submitted.set(true);
    this.error.set('');
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.pending.set(true);
    const values = this.form.getRawValue();
    this.auth.initializeOwner({ ...values, fullName: values.fullName.trim() }).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.pending.set(false)),
    ).subscribe({
      next: () => {
        this.form.reset();
        void this.router.navigate(['/login'], { replaceUrl: true, state: { setupComplete: true } });
      },
      error: error => this.error.set(authErrorMessage(error, 'setup')),
    });
  }

  ngOnDestroy(): void { this.form.reset(); }
}
