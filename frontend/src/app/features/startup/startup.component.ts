import { afterNextRender, Component, computed, DestroyRef, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { finalize, forkJoin, last, take, tap, timer } from 'rxjs';
import { AuthService } from '../../core/auth/auth.service';
import { authErrorMessage } from '../../core/auth/auth-errors';

const MIN_LOADING_DURATION_MS = 3_000;
const DISPLAY_PROGRESS_STEPS = 30;

@Component({
  selector: 'app-startup',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './startup.component.html',
  styleUrl: './startup.component.scss',
})
export class StartupComponent {
  private readonly router = inject(Router);
  private readonly route = inject(ActivatedRoute);
  private readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  readonly loading = !!this.route.snapshot.data['loading'];
  readonly pending = signal(false);
  readonly error = signal('');
  private readonly displayProgress = signal(0);
  private readonly sessionChecked = signal(false);
  // Startup readiness: display phase (90%) plus completed session check (10%).
  // This is not a network download percentage.
  readonly progress = computed(() => this.displayProgress() + (this.sessionChecked() ? 10 : 0));
  readonly status = computed(() => this.progress() === 100 ? 'Opening your workspace…'
    : this.sessionChecked() ? 'Preparing your workspace…' : 'Checking your workspace session…');

  constructor() {
    afterNextRender(() => {
      if (!this.loading) return;
      if (this.route.snapshot.queryParamMap.has('retry')) {
        this.error.set('Your session could not be verified. Check the connection and try again.');
      } else {
        this.check();
      }
    });
  }

  check(): void {
    if (this.pending()) return;
    this.error.set('');
    this.displayProgress.set(0);
    this.sessionChecked.set(false);
    this.pending.set(true);
    // Check immediately; navigate only after both the check and display time finish.
    forkJoin({
      user: this.auth.checkSession().pipe(tap(() => this.sessionChecked.set(true))),
      minimumDisplay: timer(0, MIN_LOADING_DURATION_MS / DISPLAY_PROGRESS_STEPS).pipe(
        take(DISPLAY_PROGRESS_STEPS + 1),
        tap(step => this.displayProgress.set(Math.round(step * 90 / DISPLAY_PROGRESS_STEPS))),
        last(),
      ),
    }).pipe(
      takeUntilDestroyed(this.destroyRef),
      finalize(() => this.pending.set(false)),
    ).subscribe({
      next: ({ user }) => void this.router.navigateByUrl(user ? '/dashboard' : '/login', { replaceUrl: true }),
      error: error => this.error.set(authErrorMessage(error, 'session')),
    });
  }
}
