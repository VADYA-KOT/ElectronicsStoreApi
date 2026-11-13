import { inject } from '@angular/core';
import {
  HttpInterceptorFn,
  HttpErrorResponse
} from '@angular/common/http';

import { catchError, throwError } from 'rxjs';

import { AuthService } from '../services/auth.service';
import { API_BASE_URL } from '../config/api.config';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const authService = inject(AuthService);

  const token = authService.getAccessToken();

  const apiBaseUrl = API_BASE_URL.replace(/\/+$/, '');

  const isApiRequest = request.url.startsWith(`${apiBaseUrl}/`);

  // Только регистрация и вход не требуют JWT.
  const isPublicAuthRequest =
    request.url === `${apiBaseUrl}/api/auth/register` ||
    request.url === `${apiBaseUrl}/api/auth/login`;

  if (!token || !isApiRequest || isPublicAuthRequest) {
    return next(request);
  }

  const authenticatedRequest = request.clone({
    setHeaders: {
      Authorization: `Bearer ${token}`
    }
  });

  return next(authenticatedRequest).pipe(
    catchError((error: unknown) => {
      if (
        error instanceof HttpErrorResponse &&
        error.status === 401
      ) {
        authService.logout();
      }

      return throwError(() => error);
    })
  );
};