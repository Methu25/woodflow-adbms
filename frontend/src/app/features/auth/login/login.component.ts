import { Component, DestroyRef, inject, OnDestroy, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { finalize, switchMap } from 'rxjs';
import { AuthService } from '../../../core/auth/auth.service';
import { authErrorMessage } from '../../../core/auth/auth-errors';
import { AuthLayoutComponent } from '../../../layouts/auth/auth-layout.component';
import { PasswordFieldComponent } from '../../../shared/password-field/password-field.component';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, AuthLayoutComponent, PasswordFieldComponent],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent implements OnDestroy {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  private readonly fb = inject(NonNullableFormBuilder);
  readonly pending = signal(false);
  readonly error = signal('');
  readonly submitted = signal(false);
  readonly setupComplete = !!this.router.getCurrentNavigation()?.extras.state?.['setupComplete'];
  readonly form = this.fb.group({
    username: ['', [Validators.required, Validators.maxLength(50), Validators.pattern(/\S/)]],
    password: ['', [Validators.required, Validators.maxLength(128)]],
  });

  fieldError(name: 'username' | 'password'): string {
    const control = this.form.controls[name];
    if (!control.invalid || !(control.touched || this.submitted())) return '';
    return name === 'username' ? 'Enter your username (up to 50 characters).' : 'Enter your password (up to 128 characters).';
  }

  submit(): void {
    if (this.pending()) return;
    this.submitted.set(true);
    this.error.set('');
    if (this.form.invalid) { this.form.markAllAsTouched(); return; }
    this.pending.set(true);
    const values = this.form.getRawValue();
    this.auth.login({ username: values.username.trim(), password: values.password }).pipe(
      // Keep the valid form and loading state intact while route guards finish.
      switchMap(() => this.router.navigateByUrl('/dashboard', { replaceUrl: true })),
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.pending.set(false)),
    ).subscribe({
      next: navigated => {
        if (!navigated) this.error.set('Unable to open the workspace. Please try again.');
      },
      error: error => this.error.set(authErrorMessage(error, 'login')),
    });
  }

  ngOnDestroy(): void { this.form.reset(); }
}
