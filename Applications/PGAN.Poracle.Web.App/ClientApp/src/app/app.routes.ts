import { Routes } from '@angular/router';

import { adminGuard } from './core/guards/admin.guard';
import { authGuard } from './core/guards/auth.guard';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    loadComponent: () => import('./modules/auth/login.component').then(m => m.LoginComponent),
    path: 'login',
  },
  {
    loadComponent: () => import('./modules/auth/callback.component').then(m => m.CallbackComponent),
    path: 'auth/callback',
  },
  {
    loadComponent: () => import('./modules/auth/callback.component').then(m => m.CallbackComponent),
    path: 'auth/discord/callback',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/dashboard/dashboard.component').then(m => m.DashboardComponent),
    path: 'dashboard',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/quick-picks/quick-pick-list.component').then(m => m.QuickPickListComponent),
    path: 'quick-picks',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/pokemon/pokemon-list.component').then(m => m.PokemonListComponent),
    path: 'pokemon',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/raids/raid-list.component').then(m => m.RaidListComponent),
    path: 'raids',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/quests/quest-list.component').then(m => m.QuestListComponent),
    path: 'quests',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/invasions/invasion-list.component').then(m => m.InvasionListComponent),
    path: 'invasions',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/lures/lure-list.component').then(m => m.LureListComponent),
    path: 'lures',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/nests/nest-list.component').then(m => m.NestListComponent),
    path: 'nests',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/gyms/gym-list.component').then(m => m.GymListComponent),
    path: 'gyms',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/areas/area-list.component').then(m => m.AreaListComponent),
    path: 'areas',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/geofences/geofence-list.component').then(m => m.GeofenceListComponent),
    path: 'geofences',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/profiles/profile-list.component').then(m => m.ProfileListComponent),
    path: 'profiles',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/cleaning/cleaning.component').then(m => m.CleaningComponent),
    path: 'cleaning',
  },
  {
    path: 'admin',
    pathMatch: 'full',
    redirectTo: 'admin/users',
  },
  {
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./modules/admin/admin-users.component').then(m => m.AdminUsersComponent),
    path: 'admin/users',
  },
  {
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./modules/admin/admin-webhooks.component').then(m => m.AdminWebhooksComponent),
    path: 'admin/webhooks',
  },
  {
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./modules/admin/admin-settings.component').then(m => m.AdminSettingsComponent),
    path: 'admin/settings',
  },
  {
    canActivate: [authGuard, adminGuard],
    loadComponent: () =>
      import('./modules/admin/geofence-submissions/geofence-submissions.component').then(m => m.GeofenceSubmissionsComponent),
    path: 'admin/geofence-submissions',
  },
  {
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./modules/admin/poracle-servers/poracle-servers.component').then(m => m.PoracleServersComponent),
    path: 'admin/poracle-servers',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/admin/my-webhooks.component').then(m => m.MyWebhooksComponent),
    path: 'my-webhooks',
  },
  { path: '**', redirectTo: 'dashboard' },
];
