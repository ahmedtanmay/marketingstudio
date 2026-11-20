import { Routes } from '@angular/router';
import { Permission } from './core/api/api.types';
import { authGuard, guestGuard, permissionGuard } from './core/auth/auth.guards';

export const routes: Routes = [
  // Signed-out pages
  { path: 'login', canActivate: [guestGuard], title: 'Sign in', loadComponent: () => import('./features/auth/login').then((m) => m.Login) },
  { path: 'signup', canActivate: [guestGuard], title: 'Start your free trial', loadComponent: () => import('./features/auth/signup').then((m) => m.Signup) },
  { path: 'forgot-password', title: 'Reset password', loadComponent: () => import('./features/auth/password-pages').then((m) => m.ForgotPassword) },
  { path: 'reset-password', title: 'Choose a new password', loadComponent: () => import('./features/auth/password-pages').then((m) => m.ResetPassword) },
  // Work whether or not the user is signed in (links from emails)
  { path: 'verify-email', title: 'Verify email', loadComponent: () => import('./features/auth/link-pages').then((m) => m.VerifyEmail) },
  { path: 'accept-invite', title: 'Join your team', loadComponent: () => import('./features/auth/link-pages').then((m) => m.AcceptInvite) },

  // Signed-in app
  {
    path: '',
    canActivate: [authGuard],
    loadComponent: () => import('./layout/shell').then((m) => m.Shell),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'dashboard', title: 'Dashboard', loadComponent: () => import('./features/dashboard/dashboard').then((m) => m.Dashboard) },
      {
        path: 'settings/users',
        title: 'Team',
        canActivate: [permissionGuard(Permission.UsersManage)],
        loadComponent: () => import('./features/settings/users/users-page').then((m) => m.UsersPage),
      },
      { path: 'forbidden', title: 'No access', loadComponent: () => import('./features/errors/error-pages').then((m) => m.Forbidden) },
    ],
  },

  { path: '**', title: 'Not found', loadComponent: () => import('./features/errors/error-pages').then((m) => m.NotFound) },
];
