import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { Problem } from './models';
import { NoticeService } from './notice.service';

/** Adds the bearer token to API calls only, never to other origins. */
export const authInterceptor: HttpInterceptorFn = (req, next) => {
  const token = inject(AuthService).token();
  if (!token || !req.url.startsWith('/api/')) {
    return next(req);
  }
  return next(req.clone({ setHeaders: { Authorization: `Bearer ${token}` } }));
};

/** Turns RFC 7807 problem responses into a readable notice; 401 ends the session. */
export const problemInterceptor: HttpInterceptorFn = (req, next) => {
  const notices = inject(NoticeService);
  const auth = inject(AuthService);
  return next(req).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse) {
        if (error.status === 401 && req.url.startsWith('/api/')) {
          auth.signOut();
        } else if (error.status !== 422) {
          notices.error(describeProblem(error));
        }
      }
      return throwError(() => error);
    }),
  );
};

export function describeProblem(error: HttpErrorResponse): string {
  const problem = error.error as Partial<Problem> | null;
  if (problem && typeof problem === 'object' && problem.title) {
    return problem.detail ? `${problem.title}: ${problem.detail}` : problem.title;
  }
  return error.status === 0 ? 'The API is unreachable. Is it running on port 5080?' : `Request failed (${error.status})`;
}
