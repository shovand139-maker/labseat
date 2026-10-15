import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { AuthService } from './auth.service';

const signedIn: CanActivateFn = () => inject(AuthService).token() !== null || inject(Router).parseUrl('/sign-in');
const instructor: CanActivateFn = () => inject(AuthService).isInstructor() || inject(Router).parseUrl('/events');

export const routes: Routes = [
  { path: 'sign-in', loadComponent: () => import('./sign-in.component').then((m) => m.SignInComponent) },
  { path: 'events', canActivate: [signedIn], loadComponent: () => import('./events.component').then((m) => m.EventsComponent) },
  {
    path: 'events/:eventId/roster',
    canActivate: [signedIn, instructor],
    loadComponent: () => import('./roster.component').then((m) => m.RosterComponent),
  },
  { path: 'my-bookings', canActivate: [signedIn], loadComponent: () => import('./my-bookings.component').then((m) => m.MyBookingsComponent) },
  { path: '', pathMatch: 'full', redirectTo: 'events' },
  { path: '**', redirectTo: 'events' },
];
