import { Routes } from '@angular/router';

import { adminGuard } from './core/guards/admin.guard';
import { authGuard } from './core/guards/auth.guard';
import { disabledFeatureGuard } from './core/guards/disabled-feature.guard';

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
    loadComponent: () => import('./modules/profiles-overview/profile-overview.component').then(m => m.ProfileOverviewComponent),
    path: 'profiles',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/quick-picks/quick-pick-list.component').then(m => m.QuickPickListComponent),
    path: 'quick-picks',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_mons')],
    loadComponent: () => import('./modules/pokemon/pokemon-list.component').then(m => m.PokemonListComponent),
    path: 'pokemon',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_raids')],
    loadComponent: () => import('./modules/raids/raid-list.component').then(m => m.RaidListComponent),
    path: 'raids',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_quests')],
    loadComponent: () => import('./modules/quests/quest-list.component').then(m => m.QuestListComponent),
    path: 'quests',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_invasions')],
    loadComponent: () => import('./modules/invasions/invasion-list.component').then(m => m.InvasionListComponent),
    path: 'invasions',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_lures')],
    loadComponent: () => import('./modules/lures/lure-list.component').then(m => m.LureListComponent),
    path: 'lures',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_nests')],
    loadComponent: () => import('./modules/nests/nest-list.component').then(m => m.NestListComponent),
    path: 'nests',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_gyms')],
    loadComponent: () => import('./modules/gyms/gym-list.component').then(m => m.GymListComponent),
    path: 'gyms',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_fort_changes')],
    loadComponent: () => import('./modules/fort-changes/fort-change-list.component').then(m => m.FortChangeListComponent),
    path: 'fort-changes',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_maxbattles')],
    loadComponent: () => import('./modules/max-battles/max-battle-list.component').then(m => m.MaxBattleListComponent),
    path: 'max-battles',
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
    loadComponent: () => import('./modules/cleaning/cleaning.component').then(m => m.CleaningComponent),
    path: 'cleaning',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/help/help.component').then(m => m.HelpComponent),
    path: 'help',
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
    canActivate: [authGuard],
    loadComponent: () => import('./modules/admin/my-webhooks.component').then(m => m.MyWebhooksComponent),
    path: 'my-webhooks',
  },
  { path: '**', redirectTo: 'dashboard' },
];
