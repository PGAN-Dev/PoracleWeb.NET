/* eslint-disable @angular-eslint/component-class-suffix */
import { Component, inject, signal, computed, effect, HostListener, OnInit } from '@angular/core';
import { MatBadgeModule } from '@angular/material/badge';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatIconModule } from '@angular/material/icon';
import { MatListModule } from '@angular/material/list';
import { MatMenuModule } from '@angular/material/menu';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';

import { DashboardCounts } from './core/models';
import { AuthService } from './core/services/auth.service';
import { DashboardService } from './core/services/dashboard.service';
import { SettingsService } from './core/services/settings.service';
import { LanguageSelectorComponent } from './shared/components/language-selector/language-selector.component';

interface NavItem {
  adminOnly?: boolean;
  countKey?: keyof DashboardCounts;
  delegateOnly?: boolean;
  /** pweb_settings key that disables this item when set to 'True' */
  disableKey?: string;
  group: 'alarms' | 'settings' | 'admin' | 'webhooks' | 'support';
  icon: string;
  iconColor?: string;
  label: string;
  route: string;
}

@Component({
  imports: [
    RouterOutlet,
    RouterLink,
    RouterLinkActive,
    MatToolbarModule,
    MatSidenavModule,
    MatListModule,
    MatIconModule,
    MatButtonModule,
    MatMenuModule,
    MatDividerModule,
    MatBadgeModule,
    MatTooltipModule,
    LanguageSelectorComponent,
  ],
  selector: 'app-root',
  styleUrl: './app.scss',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly ACCENT_COLORS: Record<string, { primary: string; start: string; end: string; light: string }> = {
    raids: { end: '#b71c1c', light: 'rgba(244, 67, 54, 0.1)', primary: '#f44336', start: '#c62828' },
    instinct: { end: '#f57f17', light: 'rgba(255, 193, 7, 0.1)', primary: '#ffc107', start: '#f9a825' },
    mystic: { end: '#0d47a1', light: 'rgba(33, 150, 243, 0.1)', primary: '#2196f3', start: '#1565c0' },
    pokemon: { end: '#1b5e20', light: 'rgba(76, 175, 80, 0.1)', primary: '#4caf50', start: '#2e7d32' },
    valor: { end: '#b71c1c', light: 'rgba(244, 67, 54, 0.1)', primary: '#f44336', start: '#d32f2f' },
  };

  private readonly dashboardService = inject(DashboardService);
  private readonly settingsService = inject(SettingsService);

  protected readonly siteTitle = computed(() => this.settingsService.siteSettings()['custom_title'] || 'DM Alerts');

  private readonly titleEffect = effect(() => {
    document.title = this.siteTitle();
  });

  protected readonly accentTheme = signal(localStorage.getItem('poracle-accent') || '');

  protected readonly auth = inject(AuthService);

  protected readonly navItems: NavItem[] = [
    { group: 'alarms', icon: 'dashboard', iconColor: '#1976d2', label: 'Dashboard', route: '/dashboard' },
    { group: 'alarms', icon: 'bolt', iconColor: '#ff6f00', label: 'Quick Picks', route: '/quick-picks' },
    {
      countKey: 'pokemon',
      disableKey: 'disable_mons',
      group: 'alarms',
      icon: 'catching_pokemon',
      iconColor: '#4caf50',
      label: 'Pokemon',
      route: '/pokemon',
    },
    {
      countKey: 'raids',
      disableKey: 'disable_raids',
      group: 'alarms',
      icon: 'shield',
      iconColor: '#f44336',
      label: 'Raids',
      route: '/raids',
    },
    {
      countKey: 'maxBattles',
      disableKey: 'disable_maxbattles',
      group: 'alarms',
      icon: 'flash_on',
      iconColor: '#d500f9',
      label: 'Max Battles',
      route: '/max-battles',
    },
    {
      countKey: 'quests',
      disableKey: 'disable_quests',
      group: 'alarms',
      icon: 'assignment',
      iconColor: '#9c27b0',
      label: 'Quests',
      route: '/quests',
    },
    {
      countKey: 'invasions',
      disableKey: 'disable_invasions',
      group: 'alarms',
      icon: 'warning',
      iconColor: '#607d8b',
      label: 'Invasions',
      route: '/invasions',
    },
    {
      countKey: 'lures',
      disableKey: 'disable_lures',
      group: 'alarms',
      icon: 'place',
      iconColor: '#e91e63',
      label: 'Lures',
      route: '/lures',
    },
    {
      countKey: 'nests',
      disableKey: 'disable_nests',
      group: 'alarms',
      icon: 'park',
      iconColor: '#8bc34a',
      label: 'Nests',
      route: '/nests',
    },
    {
      countKey: 'gyms',
      disableKey: 'disable_gyms',
      group: 'alarms',
      icon: 'fitness_center',
      iconColor: '#00bcd4',
      label: 'Gyms',
      route: '/gyms',
    },
    {
      countKey: 'fortChanges',
      disableKey: 'disable_fort_changes',
      group: 'alarms',
      icon: 'domain',
      iconColor: '#795548',
      label: 'Fort Changes',
      route: '/fort-changes',
    },
    { disableKey: 'disable_areas', group: 'settings', icon: 'map', iconColor: '#ff9800', label: 'Areas', route: '/areas' },
    { group: 'settings', icon: 'draw', iconColor: '#2196f3', label: 'My Geofences', route: '/geofences' },
    { disableKey: 'disable_profiles', group: 'settings', icon: 'person', iconColor: '#7b1fa2', label: 'Profiles', route: '/profiles' },
    { group: 'settings', icon: 'cleaning_services', iconColor: '#795548', label: 'Cleaning', route: '/cleaning' },
    { group: 'support', icon: 'help', iconColor: '#673ab7', label: 'Help', route: '/help' },
    { adminOnly: true, group: 'admin', icon: 'people', iconColor: '#455a64', label: 'Users', route: '/admin/users' },
    { adminOnly: true, group: 'admin', icon: 'webhook', iconColor: '#00897b', label: 'Webhooks', route: '/admin/webhooks' },
    { adminOnly: true, group: 'admin', icon: 'settings', iconColor: '#546e7a', label: 'Settings', route: '/admin/settings' },
    {
      adminOnly: true,
      group: 'admin',
      icon: 'rate_review',
      iconColor: '#ff9800',
      label: 'User Geofences',
      route: '/admin/geofence-submissions',
    },
    { adminOnly: true, group: 'admin', icon: 'dns', iconColor: '#607d8b', label: 'Poracle Servers', route: '/admin/poracle-servers' },
    { delegateOnly: true, group: 'webhooks', icon: 'webhook', iconColor: '#00897b', label: 'My Webhooks', route: '/my-webhooks' },
  ];

  protected readonly adminNavItems = computed(() =>
    this.navItems.filter(item => item.group === 'admin' && (!item.adminOnly || this.auth.isAdmin())),
  );

  protected readonly alarmNavItems = computed(() =>
    this.navItems.filter(
      item =>
        item.group === 'alarms' &&
        (!item.adminOnly || this.auth.isAdmin()) &&
        (this.auth.isAdmin() || !this.isFeatureDisabled(item.disableKey)),
    ),
  );

  protected readonly counts = signal<DashboardCounts | null>(null);

  protected readonly customNavLink = computed(() => {
    const s = this.settingsService.siteSettings();
    const url = s['custom_page_url'];
    const label = s['custom_page_name'];
    if (!url || !label) return null;
    const rawIcon = s['custom_page_icon'] || '';
    // FontAwesome classes (e.g. "fas fa-map") aren't Material icons — use fallback
    const icon = rawIcon && !rawIcon.includes('fa-') ? rawIcon : 'launch';
    return { icon, label, url };
  });

  protected readonly darkMode = signal(localStorage.getItem('poracle-theme') === 'dark');

  protected readonly headerLogoUrl = computed(() => this.settingsService.siteSettings()['header_logo_url'] || '');

  protected readonly hideHeaderLogo = computed(() => this.settingsService.isDisabled('hide_header_logo'));

  protected readonly isMobile = signal(window.innerWidth < 768);

  protected readonly settingsNavItems = computed(() =>
    this.navItems.filter(
      item =>
        item.group === 'settings' &&
        (!item.adminOnly || this.auth.isAdmin()) &&
        (this.auth.isAdmin() || !this.isFeatureDisabled(item.disableKey)),
    ),
  );

  protected readonly showShortcutHelp = signal(false);

  protected readonly sidenavCollapsed = signal(localStorage.getItem('poracle-sidenav-collapsed') === 'true');

  protected readonly sidenavOpened = signal(!this.isMobile());

  protected readonly supportNavItems = computed(() => this.navItems.filter(item => item.group === 'support'));

  protected readonly toolbarGradient = computed(() => {
    const accent = this.accentTheme();
    const colors = this.ACCENT_COLORS[accent];
    if (colors) {
      return `linear-gradient(135deg, ${colors.start} 0%, ${colors.end} 100%)`;
    }
    return this.darkMode() ? 'linear-gradient(135deg, #0d47a1 0%, #1a237e 100%)' : 'linear-gradient(135deg, #1565c0 0%, #0d47a1 100%)';
  });

  protected readonly webhookNavItems = computed(() =>
    this.navItems.filter(item => item.group === 'webhooks' && (!item.delegateOnly || this.auth.hasManagedWebhooks())),
  );

  constructor() {
    this.applyTheme();
    this.applyAccentTheme();
  }

  getCount(item: NavItem): number {
    if (!item.countKey || !this.counts()) return 0;
    return this.counts()![item.countKey] ?? 0;
  }

  loadCounts(): void {
    this.dashboardService.getCounts().subscribe({
      error: () => {}, // silently fail for badge counts
      next: c => this.counts.set(c),
    });
  }

  logout(): void {
    this.auth.logout();
  }

  ngOnInit(): void {
    this.settingsService.loadOnce().subscribe();
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    const tag = (event.target as HTMLElement)?.tagName;
    if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return;
    if (document.querySelector('.cdk-overlay-pane')) {
      if (event.key === 'Escape') {
        this.showShortcutHelp.set(false);
      }
      return;
    }

    switch (event.key) {
      case '?':
        this.showShortcutHelp.update(v => !v);
        break;
      case 'Escape':
        if (this.showShortcutHelp()) {
          this.showShortcutHelp.set(false);
        } else if (this.isMobile() && this.sidenavOpened()) {
          this.sidenavOpened.set(false);
        }
        break;
      case '[':
        if (!this.isMobile()) {
          this.sidenavCollapsed.set(true);
          localStorage.setItem('poracle-sidenav-collapsed', 'true');
        }
        break;
      case ']':
        if (!this.isMobile()) {
          this.sidenavCollapsed.set(false);
          localStorage.setItem('poracle-sidenav-collapsed', 'false');
        }
        break;
    }
  }

  onNavClick(): void {
    if (this.isMobile()) {
      this.sidenavOpened.set(false);
    }
  }

  @HostListener('window:resize')
  onResize(): void {
    const mobile = window.innerWidth < 768;
    this.isMobile.set(mobile);
    if (mobile) {
      this.sidenavOpened.set(false);
    }
  }

  setAccentTheme(theme: string): void {
    this.accentTheme.set(theme);
    localStorage.setItem('poracle-accent', theme);
    this.applyAccentTheme();
  }

  stopImpersonating(): void {
    this.auth.stopImpersonating();
    this.loadCounts();
  }

  toggleAlerts(): void {
    this.auth.toggleAlerts().subscribe({
      next: () => this.auth.loadCurrentUser(),
    });
  }

  toggleSidenav(): void {
    this.sidenavOpened.update(v => !v);
  }

  toggleSidenavCollapse(): void {
    this.sidenavCollapsed.update(v => !v);
    localStorage.setItem('poracle-sidenav-collapsed', String(this.sidenavCollapsed()));
  }

  toggleTheme(): void {
    this.darkMode.update(v => !v);
    this.applyTheme();
  }

  private applyAccentTheme(): void {
    const accent = this.accentTheme();
    const colors = this.ACCENT_COLORS[accent];
    const body = document.body;

    if (colors) {
      body.style.setProperty('--accent-primary', colors.primary);
      body.style.setProperty('--accent-gradient-start', colors.start);
      body.style.setProperty('--accent-gradient-end', colors.end);
      body.style.setProperty('--accent-light', colors.light);
    } else {
      body.style.removeProperty('--accent-primary');
      body.style.removeProperty('--accent-gradient-start');
      body.style.removeProperty('--accent-gradient-end');
      body.style.removeProperty('--accent-light');
    }
  }

  private applyTheme(): void {
    const scheme = this.darkMode() ? 'dark' : 'light';
    document.body.style.colorScheme = scheme;
    document.body.classList.toggle('dark-theme', this.darkMode());
    document.body.classList.toggle('light-theme', !this.darkMode());
    localStorage.setItem('poracle-theme', scheme);
  }

  private isFeatureDisabled(key?: string): boolean {
    if (!key) return false;
    return this.settingsService.isDisabled(key);
  }
}
