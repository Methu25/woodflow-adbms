import { HttpErrorResponse } from '@angular/common/http';

export function authErrorMessage(error: unknown, action: 'login' | 'setup' | 'session' | 'logout'): string {
  const status = error instanceof HttpErrorResponse ? error.status : 0;
  switch (status) {
    case 400: return action === 'setup'
      ? 'Unable to create the owner account. Check your details and ask your administrator to verify the setup code.'
      : 'Your request could not be verified. Please try again to obtain a fresh security token.';
    case 401: return action === 'login'
      ? 'Unable to sign in. Check your username and password.'
      : 'Your session has ended. Please sign in again.';
    case 403: return 'Access is not permitted. Contact the business owner for assistance.';
    case 429: return action === 'setup'
      ? 'Too many setup attempts. Wait 15 minutes before trying again.'
      : 'Too many attempts. Wait a minute before trying again.';
    case 503: return 'The workspace is temporarily unavailable. Please try again shortly.';
    default: return 'Unable to reach the workspace. Check that the HTTPS backend is running, then try again.';
  }
}
