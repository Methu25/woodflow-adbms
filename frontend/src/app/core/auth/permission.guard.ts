import { HttpErrorResponse } from '@angular/common/http';
import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { catchError, map, of, switchMap } from 'rxjs';
import { PermissionsService } from './permissions.service';

export const permissionGuard: CanActivateFn = route => {
  const service = inject(PermissionsService);
  const router = inject(Router);
  const key = route.data['permission'] as string;
  return service.refresh().pipe(
    switchMap(() => service.check(key)), map(() => true),
    catchError(error => of(error instanceof HttpErrorResponse && error.status === 401
      ? router.createUrlTree(['/login']) : router.createUrlTree(['/access-restricted'], {
        queryParams: error instanceof HttpErrorResponse && error.status === 403 ? {} : { unavailable: '1' },
      }))),
  );
};
