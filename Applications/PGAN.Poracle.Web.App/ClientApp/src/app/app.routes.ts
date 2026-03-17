import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { adminGuard } from './core/guards/admin.guard';

export const routes: Routes = [
  { path: '', redirectTo: 'dashboard', pathMatch: 'full' },
  {
    path: 'login',
    loadComponent: () =>
      import('./modules/auth/login.component').then((m) => m.LoginComponent),
  },
  {
    path: 'auth/callback',
    loadComponent: () =>
      import('./modules/auth/callback.component').then((m) => m.CallbackComponent),
  },
  {
    path: 'auth/discord/callback',
    loadComponent: () =>
      import('./modules/auth/callback.component').then((m) => m.CallbackComponent),
  },
  {
    path: 'dashboard',
    loadComponent: () =>
      import('./modules/dashboard/dashboard.component').then((m) => m.DashboardComponent),
    canActivate: [authGuard],
  },
  {
    path: 'pokemon',
    loadComponent: () =>
      import('./modules/pokemon/pokemon-list.component').then((m) => m.PokemonListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'raids',
    loadComponent: () =>
      import('./modules/raids/raid-list.component').then((m) => m.RaidListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'quests',
    loadComponent: () =>
      import('./modules/quests/quest-list.component').then((m) => m.QuestListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'invasions',
    loadComponent: () =>
      import('./modules/invasions/invasion-list.component').then((m) => m.InvasionListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'lures',
    loadComponent: () =>
      import('./modules/lures/lure-list.component').then((m) => m.LureListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'nests',
    loadComponent: () =>
      import('./modules/nests/nest-list.component').then((m) => m.NestListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'gyms',
    loadComponent: () =>
      import('./modules/gyms/gym-list.component').then((m) => m.GymListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'areas',
    loadComponent: () =>
      import('./modules/areas/area-list.component').then((m) => m.AreaListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'profiles',
    loadComponent: () =>
      import('./modules/profiles/profile-list.component').then((m) => m.ProfileListComponent),
    canActivate: [authGuard],
  },
  {
    path: 'cleaning',
    loadComponent: () =>
      import('./modules/cleaning/cleaning.component').then((m) => m.CleaningComponent),
    canActivate: [authGuard],
  },
  {
    path: 'admin',
    redirectTo: 'admin/users',
    pathMatch: 'full',
  },
  {
    path: 'admin/users',
    loadComponent: () =>
      import('./modules/admin/admin-users.component').then((m) => m.AdminUsersComponent),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/webhooks',
    loadComponent: () =>
      import('./modules/admin/admin-webhooks.component').then((m) => m.AdminWebhooksComponent),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'admin/settings',
    loadComponent: () =>
      import('./modules/admin/admin-settings.component').then((m) => m.AdminSettingsComponent),
    canActivate: [authGuard, adminGuard],
  },
  {
    path: 'my-webhooks',
    loadComponent: () =>
      import('./modules/admin/my-webhooks.component').then((m) => m.MyWebhooksComponent),
    canActivate: [authGuard],
  },
  { path: '**', redirectTo: 'dashboard' },
];
