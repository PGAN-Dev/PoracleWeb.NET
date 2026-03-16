import { Component, inject, signal, computed, HostListener, OnInit } from '@angular/core';
import { RouterOutlet, RouterLink, RouterLinkActive } from '@angular/router';
import { MatToolbarModule } from '@angular/material/toolbar';
import { MatSidenavModule } from '@angular/material/sidenav';
import { MatListModule } from '@angular/material/list';
import { MatIconModule } from '@angular/material/icon';
import { MatButtonModule } from '@angular/material/button';
import { MatMenuModule } from '@angular/material/menu';
import { MatDividerModule } from '@angular/material/divider';
import { MatBadgeModule } from '@angular/material/badge';
import { MatTooltipModule } from '@angular/material/tooltip';
import { AuthService } from './core/services/auth.service';
import { DashboardService } from './core/services/dashboard.service';
import { DashboardCounts } from './core/models';
import { LanguageSelectorComponent } from './shared/components/language-selector/language-selector.component';

interface NavItem {
  label: string;
  icon: string;
  route: string;
  adminOnly?: boolean;
  group: 'alarms' | 'settings' | 'admin';
  countKey?: keyof DashboardCounts;
}

@Component({
  selector: 'app-root',
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
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App implements OnInit {
  protected readonly auth = inject(AuthService);
  private readonly dashboardService = inject(DashboardService);

  protected readonly isMobile = signal(window.innerWidth < 768);
  protected readonly sidenavOpened = signal(!this.isMobile());
  protected readonly darkMode = signal(localStorage.getItem('poracle-theme') === 'dark');
  protected readonly counts = signal<DashboardCounts | null>(null);

  constructor() {
    this.applyTheme();
  }

  ngOnInit(): void {
    // Load alarm counts for nav badges when user is logged in
    if (this.auth.isAuthenticated()) {
      this.loadCounts();
    }
  }

  loadCounts(): void {
    this.dashboardService.getCounts().subscribe({
      next: (c) => this.counts.set(c),
      error: () => {}, // silently fail for badge counts
    });
  }

  protected readonly navItems: NavItem[] = [
    { label: 'Dashboard', icon: 'dashboard', route: '/dashboard', group: 'alarms' },
    { label: 'Pokemon', icon: 'catching_pokemon', route: '/pokemon', group: 'alarms', countKey: 'pokemon' },
    { label: 'Raids', icon: 'shield', route: '/raids', group: 'alarms', countKey: 'raids' },
    { label: 'Quests', icon: 'assignment', route: '/quests', group: 'alarms', countKey: 'quests' },
    { label: 'Invasions', icon: 'warning', route: '/invasions', group: 'alarms', countKey: 'invasions' },
    { label: 'Lures', icon: 'place', route: '/lures', group: 'alarms', countKey: 'lures' },
    { label: 'Nests', icon: 'park', route: '/nests', group: 'alarms', countKey: 'nests' },
    { label: 'Gyms', icon: 'fitness_center', route: '/gyms', group: 'alarms', countKey: 'gyms' },
    { label: 'Areas', icon: 'map', route: '/areas', group: 'settings' },
    { label: 'Profiles', icon: 'person', route: '/profiles', group: 'settings' },
    { label: 'Cleaning', icon: 'cleaning_services', route: '/cleaning', group: 'settings' },
    { label: 'Admin', icon: 'admin_panel_settings', route: '/admin', adminOnly: true, group: 'admin' },
  ];

  protected readonly alarmNavItems = computed(() =>
    this.navItems.filter((item) => item.group === 'alarms' && (!item.adminOnly || this.auth.isAdmin())),
  );

  protected readonly settingsNavItems = computed(() =>
    this.navItems.filter((item) => item.group === 'settings' && (!item.adminOnly || this.auth.isAdmin())),
  );

  protected readonly adminNavItems = computed(() =>
    this.navItems.filter((item) => item.group === 'admin' && (!item.adminOnly || this.auth.isAdmin())),
  );

  getCount(item: NavItem): number {
    if (!item.countKey || !this.counts()) return 0;
    return this.counts()![item.countKey] ?? 0;
  }

  @HostListener('window:resize')
  onResize(): void {
    const mobile = window.innerWidth < 768;
    this.isMobile.set(mobile);
    if (mobile) {
      this.sidenavOpened.set(false);
    }
  }

  toggleSidenav(): void {
    this.sidenavOpened.update((v) => !v);
  }

  toggleTheme(): void {
    this.darkMode.update((v) => !v);
    this.applyTheme();
  }

  private applyTheme(): void {
    const scheme = this.darkMode() ? 'dark' : 'light';
    document.body.style.colorScheme = scheme;
    document.body.classList.toggle('dark-theme', this.darkMode());
    document.body.classList.toggle('light-theme', !this.darkMode());
    localStorage.setItem('poracle-theme', scheme);
  }

  onNavClick(): void {
    if (this.isMobile()) {
      this.sidenavOpened.set(false);
    }
  }

  logout(): void {
    this.auth.logout();
  }
}
