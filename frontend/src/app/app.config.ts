import { ApplicationConfig, provideZoneChangeDetection } from '@angular/core';
import { provideRouter, withViewTransitions } from '@angular/router';
import { provideHttpClient, withXsrfConfiguration } from '@angular/common/http';

import { routes } from './app.routes';

export const appConfig: ApplicationConfig = {
  providers: [
    provideZoneChangeDetection({ eventCoalescing: true }),
    provideRouter(routes, withViewTransitions({
      skipInitialTransition: true,
      onViewTransitionCreated: ({ transition, from, to }) => {
        const previous = from.routeConfig?.path ?? from.firstChild?.routeConfig?.path;
        const next = to.routeConfig?.path ?? to.firstChild?.routeConfig?.path;
        if (previous !== 'loading' || next !== 'login'
          || window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
          // Skipping intentionally rejects ready; handle that expected browser abort.
          void transition.ready.catch(() => undefined);
          transition.skipTransition();
        }
      },
    })),
    provideHttpClient(withXsrfConfiguration({ cookieName: 'XSRF-TOKEN', headerName: 'X-XSRF-TOKEN' })),
  ]
};
