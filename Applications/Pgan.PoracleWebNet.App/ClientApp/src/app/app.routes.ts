import { Routes } from '@angular/router';

import { adminGuard } from './core/guards/admin.guard';
import { authGuard } from './core/guards/auth.guard';
import { disabledFeatureGuard } from './core/guards/disabled-feature.guard';
import { unsavedChangesGuard } from './core/guards/unsaved-changes.guard';

// Each `title` is an i18n key; PageTitleStrategy translates it and adds the site title.
export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
  {
    loadComponent: () => import('./modules/auth/login.component').then(m => m.LoginComponent),
    path: 'login',
    title: 'AUTH.SIGN_IN',
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
    loadComponent: () => import('./modules/auth/callback.component').then(m => m.CallbackComponent),
    path: 'auth/oidc/callback',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/dashboard/dashboard.component').then(m => m.DashboardComponent),
    path: 'dashboard',
    title: 'NAV.DASHBOARD',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_profiles')],
    loadComponent: () => import('./modules/profiles-overview/profile-overview.component').then(m => m.ProfileOverviewComponent),
    path: 'profiles',
    title: 'NAV.PROFILES',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/quick-picks/quick-pick-list.component').then(m => m.QuickPickListComponent),
    path: 'quick-picks',
    title: 'NAV.QUICK_PICKS',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_mons')],
    loadComponent: () => import('./modules/pokemon/pokemon-list.component').then(m => m.PokemonListComponent),
    path: 'pokemon',
    title: 'NAV.POKEMON',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_raids')],
    loadComponent: () => import('./modules/raids/raid-list.component').then(m => m.RaidListComponent),
    path: 'raids',
    title: 'NAV.RAIDS',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_quests')],
    loadComponent: () => import('./modules/quests/quest-list.component').then(m => m.QuestListComponent),
    path: 'quests',
    title: 'NAV.QUESTS',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_invasions')],
    loadComponent: () => import('./modules/invasions/invasion-list.component').then(m => m.InvasionListComponent),
    path: 'invasions',
    title: 'NAV.INVASIONS',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_lures')],
    loadComponent: () => import('./modules/lures/lure-list.component').then(m => m.LureListComponent),
    path: 'lures',
    title: 'NAV.LURES',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_nests')],
    loadComponent: () => import('./modules/nests/nest-list.component').then(m => m.NestListComponent),
    path: 'nests',
    title: 'NAV.NESTS',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_gyms')],
    loadComponent: () => import('./modules/gyms/gym-list.component').then(m => m.GymListComponent),
    path: 'gyms',
    title: 'NAV.GYMS',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_showcase')],
    loadComponent: () => import('./modules/pokestop-events/pokestop-event-list.component').then(m => m.PokestopEventListComponent),
    path: 'pokestop-events',
    title: 'NAV.POKESTOP_EVENTS',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_fort_changes')],
    loadComponent: () => import('./modules/fort-changes/fort-change-list.component').then(m => m.FortChangeListComponent),
    path: 'fort-changes',
    title: 'NAV.FORT_CHANGES',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_maxbattles')],
    loadComponent: () => import('./modules/max-battles/max-battle-list.component').then(m => m.MaxBattleListComponent),
    path: 'max-battles',
    title: 'NAV.MAX_BATTLES',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_areas')],
    loadComponent: () => import('./modules/areas/area-list.component').then(m => m.AreaListComponent),
    path: 'areas',
    title: 'NAV.AREAS',
  },
  {
    canActivate: [authGuard, disabledFeatureGuard('disable_user_geofences')],
    loadComponent: () => import('./modules/geofences/geofence-list.component').then(m => m.GeofenceListComponent),
    path: 'geofences',
    title: 'NAV.MY_GEOFENCES',
  },
  {
    // Places moved into the Areas page, beside the pin it belongs with. Kept as a redirect because
    // bookmarks and anything still pointing here should land somewhere useful rather than 404.
    path: 'places',
    pathMatch: 'full',
    redirectTo: 'areas',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/cleaning/cleaning.component').then(m => m.CleaningComponent),
    path: 'cleaning',
    title: 'NAV.CLEANING',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/help/help.component').then(m => m.HelpComponent),
    path: 'help',
    title: 'NAV.HELP',
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
    title: 'NAV.USERS',
  },
  {
    canActivate: [authGuard, adminGuard],
    loadComponent: () => import('./modules/admin/admin-webhooks.component').then(m => m.AdminWebhooksComponent),
    path: 'admin/webhooks',
    title: 'NAV.WEBHOOKS',
  },
  {
    canActivate: [authGuard, adminGuard],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./modules/admin/admin-areas.component').then(m => m.AdminAreasComponent),
    path: 'admin/areas',
    title: 'NAV.ADMIN_AREAS',
  },
  {
    canActivate: [authGuard, adminGuard],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./modules/admin/admin-settings.component').then(m => m.AdminSettingsComponent),
    path: 'admin/settings',
    title: 'NAV.SETTINGS',
  },
  {
    canActivate: [authGuard, adminGuard, disabledFeatureGuard('disable_user_geofences')],
    loadComponent: () =>
      import('./modules/admin/geofence-submissions/geofence-submissions.component').then(m => m.GeofenceSubmissionsComponent),
    path: 'admin/geofence-submissions',
    title: 'NAV.USER_GEOFENCES',
  },
  {
    canActivate: [authGuard],
    loadComponent: () => import('./modules/admin/my-webhooks.component').then(m => m.MyWebhooksComponent),
    path: 'my-webhooks',
    title: 'NAV.MY_WEBHOOKS',
  },
  { path: '**', redirectTo: 'dashboard' },
];
