import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of } from 'rxjs';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.checkSession().pipe(
    map(user => user ? true : router.createUrlTree(['/login'])),
    // An unavailable backend is not proof of an anonymous session.
    catchError(() => of(router.createUrlTree(['/loading'], { queryParams: { retry: '1' } }))),
  );
};

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  const router = inject(Router);
  return auth.checkSession().pipe(
    map(user => user ? router.createUrlTree(['/dashboard']) : true),
    catchError(() => of(true)),
  );
};

export const ownerGuard: CanActivateFn = () => {
  const router = inject(Router);
  return inject(AuthService).checkSession().pipe(
    map(user => !user ? router.createUrlTree(['/login']) : user.role === 'OWNER'
      ? true : router.createUrlTree(['/dashboard'])),
    catchError(() => of(router.createUrlTree(['/loading'], { queryParams: { retry: '1' } }))),
  );
};
