import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatDividerModule } from '@angular/material/divider';
import { SettingsService } from '../../core/services/settings.service';
import { PwebSetting } from '../../core/models';

interface SettingMeta {
  key: string;
  label: string;
  description: string;
  type: 'text' | 'url' | 'boolean';
  /** Only show this setting when another boolean setting is True */
  showWhen?: string;
}

interface SettingGroup {
  label: string;
  icon: string;
  color: string;
  settings: SettingMeta[];
}

const SETTING_GROUPS: SettingGroup[] = [
  {
    label: 'Branding',
    icon: 'palette',
    color: '#1976d2',
    settings: [
      { key: 'custom_title', label: 'Site Title', description: 'Name shown in the browser tab and page header.', type: 'text' },
      { key: 'custom_page_name', label: 'Nav Link Label', description: 'Label for the custom navigation link (e.g. "Back To Map").', type: 'text' },
      { key: 'custom_page_url', label: 'Nav Link URL', description: 'URL the custom nav link points to.', type: 'url' },
      { key: 'custom_page_icon', label: 'Nav Link Icon', description: 'FontAwesome class for the nav link icon (e.g. "fas fa-map").', type: 'text' },
    ],
  },
  {
    label: 'Alarm Types',
    icon: 'notifications',
    color: '#ff9800',
    settings: [
      { key: 'disable_mons', label: 'Disable Pokémon', description: 'Hide Pokémon alarm management from all users.', type: 'boolean' },
      { key: 'disable_raids', label: 'Disable Raids', description: 'Hide raid alarm management from all users.', type: 'boolean' },
      { key: 'disable_quests', label: 'Disable Quests', description: 'Hide quest alarm management from all users.', type: 'boolean' },
      { key: 'disable_invasions', label: 'Disable Invasions', description: 'Hide invasion alarm management from all users.', type: 'boolean' },
      { key: 'disable_lures', label: 'Disable Lures', description: 'Hide lure alarm management from all users.', type: 'boolean' },
      { key: 'disable_nests', label: 'Disable Nests', description: 'Hide nest alarm management from all users.', type: 'boolean' },
      { key: 'disable_gyms', label: 'Disable Gyms', description: 'Hide gym alarm management from all users.', type: 'boolean' },
    ],
  },
  {
    label: 'Features',
    icon: 'tune',
    color: '#4caf50',
    settings: [
      { key: 'disable_areas', label: 'Disable Areas', description: 'Prevent users from managing their area subscriptions.', type: 'boolean' },
      { key: 'disable_profiles', label: 'Disable Profiles', description: 'Prevent users from creating and switching alarm profiles.', type: 'boolean' },
      { key: 'disable_location', label: 'Disable Location', description: 'Prevent users from setting a home location.', type: 'boolean' },
      { key: 'disable_nominatim', label: 'Disable Geocoding', description: 'Disable Nominatim address search for location picking.', type: 'boolean' },
      { key: 'disable_geomap', label: 'Disable Map View', description: 'Hide the interactive geofence map entirely.', type: 'boolean' },
      { key: 'disable_geomap_select', label: 'Disable Map Area Selection', description: 'Prevent users from selecting areas by clicking the map.', type: 'boolean' },
      { key: 'enable_templates', label: 'Enable Templates', description: 'Allow users to choose notification message templates.', type: 'boolean' },
    ],
  },
  {
    label: 'Administration',
    icon: 'admin_panel_settings',
    color: '#f44336',
    settings: [
      { key: 'enable_roles', label: 'Enable Role-Based Access', description: 'Only allow users with specific Discord roles to log in. Requires Bot Token and Guild ID.', type: 'boolean' },
      { key: 'allowed_role_ids', label: 'Allowed Role IDs', description: 'Comma-separated Discord role IDs that grant access (e.g. "123456789,987654321"). Leave empty to allow all.', type: 'text', showWhen: 'enable_roles' },
      { key: 'allowed_languages', label: 'Allowed Languages', description: 'Comma-separated list of language codes users can select (e.g. "en,de,fr").', type: 'text' },
    ],
  },
  {
    label: 'Commands',
    icon: 'terminal',
    color: '#607d8b',
    settings: [
      { key: 'register_command', label: 'Register Command', description: 'The Poracle bot command users run to register (e.g. "$!register").', type: 'text' },
      { key: 'location_command', label: 'Location Command', description: 'The Poracle bot command users run to set their location.', type: 'text' },
    ],
  },
  {
    label: 'Telegram',
    icon: 'send',
    color: '#0088cc',
    settings: [
      { key: 'enable_telegram', label: 'Enable Telegram', description: 'Allow users to log in and manage alarms via Telegram.', type: 'boolean' },
      { key: 'telegram_bot', label: 'Bot Username', description: 'Telegram bot username (without @).', type: 'text' },
    ],
  },
  {
    label: 'Maps & Assets',
    icon: 'map',
    color: '#2e7d32',
    settings: [
      { key: 'provider_url', label: 'Map Tile URL', description: 'URL template for the map tile provider (used for static maps).', type: 'url' },
    ],
  },
  {
    label: 'Analytics & Links',
    icon: 'bar_chart',
    color: '#7b1fa2',
    settings: [
      { key: 'gAnalyticsId', label: 'Google Analytics ID', description: 'GA4 measurement ID (leave blank to disable).', type: 'text' },
      { key: 'patreonUrl', label: 'Patreon URL', description: 'Link to your Patreon page shown in the UI.', type: 'url' },
      { key: 'paypalUrl', label: 'PayPal URL', description: 'Link to your PayPal donation page shown in the UI.', type: 'url' },
    ],
  },
  {
    label: 'Debug',
    icon: 'bug_report',
    color: '#ff5722',
    settings: [
      { key: 'site_is_https', label: 'Site Is HTTPS', description: 'Mark the site as running over HTTPS (affects cookie security).', type: 'boolean' },
      { key: 'debug', label: 'Debug Mode', description: 'Enable verbose debug logging (not recommended in production).', type: 'boolean' },
    ],
  },
];

@Component({
  selector: 'app-admin-settings',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatSlideToggleModule,
    MatDividerModule,
  ],
  template: `
    <div class="page-header">
      <div class="page-header-text">
        <h1>Server Settings</h1>
        <p class="page-description">Configure site behaviour, integrations, and feature flags.</p>
      </div>
      @if (modifiedSettings().size > 0) {
        <button mat-raised-button color="primary" (click)="saveAllModified()" [disabled]="bulkSaving()">
          @if (bulkSaving()) {
            <mat-spinner diameter="18" class="inline-spinner"></mat-spinner>
          } @else {
            <mat-icon>save</mat-icon>
          }
          Save {{ modifiedSettings().size }} Change{{ modifiedSettings().size === 1 ? '' : 's' }}
        </button>
      }
    </div>

    <div class="page-content">
      @if (settingsLoading()) {
        <div class="loading-container">
          <mat-spinner diameter="48"></mat-spinner>
        </div>
      } @else {
        @for (group of visibleGroups(); track group.label; let last = $last) {
          <section class="settings-section">
            <div class="section-header" [style.border-left]="'4px solid ' + group.color">
              <mat-icon class="section-icon" [style.color]="group.color">{{ group.icon }}</mat-icon>
              <span class="section-label">{{ group.label }}</span>
            </div>

            <div class="section-rows">
              @for (meta of group.settings; track meta.key; let rowLast = $last) {
                @if (getSettingValue(meta.key) !== null && isSettingVisible(meta)) {
                  <div class="setting-row" [class.modified]="modifiedSettings().has(meta.key)">
                    <div class="setting-info">
                      <span class="setting-label">{{ meta.label }}</span>
                      <span class="setting-desc">{{ meta.description }}</span>
                    </div>
                    <div class="setting-control">
                      @if (meta.type === 'boolean') {
                        <mat-slide-toggle
                          [checked]="getBool(meta.key)"
                          (change)="onBoolChange(meta.key, $event.checked)"
                          color="primary"
                        ></mat-slide-toggle>
                      } @else {
                        <mat-form-field appearance="outline" class="setting-field">
                          <input
                            matInput
                            type="text"
                            [ngModel]="getSettingValue(meta.key) ?? ''"
                            (ngModelChange)="onTextChange(meta.key, $event)"
                          />
                        </mat-form-field>
                      }
                    </div>
                  </div>
                  @if (!rowLast) {
                    <mat-divider></mat-divider>
                  }
                }
              }
            </div>
          </section>

          @if (!last) {
            <div class="section-gap"></div>
          }
        }

        <!-- Icon Repository Selector -->
        <div class="section-gap"></div>
        <section class="settings-section">
          <div class="section-header" style="border-left: 4px solid #2e7d32">
            <mat-icon class="section-icon" style="color: #2e7d32">image</mat-icon>
            <span class="section-label">Icon Repository</span>
          </div>
          <div class="repo-selector">
            @for (repo of iconRepos; track repo.name) {
              <div class="repo-card" [class.repo-active]="isRepoActive(repo)" (click)="selectRepo(repo)">
                <div class="repo-header">
                  <div class="repo-radio">
                    @if (isRepoActive(repo)) {
                      <mat-icon class="repo-check">check_circle</mat-icon>
                    } @else {
                      <mat-icon class="repo-uncheck">radio_button_unchecked</mat-icon>
                    }
                  </div>
                  <div class="repo-info">
                    <span class="repo-name">{{ repo.name }}</span>
                    <span class="repo-url">{{ repo.base }}</span>
                  </div>
                </div>
                <div class="repo-preview-row">
                  @for (img of repo.previewImages; track img.path) {
                    <div class="icon-preview-item">
                      <img [src]="repo.base + '/' + img.path"
                           class="icon-preview-img"
                           (error)="onPreviewError($event)"
                           (load)="onPreviewLoad($event)"
                           loading="lazy" />
                      <span class="icon-preview-name">{{ img.name }}</span>
                    </div>
                  }
                </div>
              </div>
            }
          </div>
        </section>

        @if (unknownSettings().length > 0) {
          <div class="section-gap"></div>
          <section class="settings-section">
            <div class="section-header">
              <mat-icon class="section-icon">help_outline</mat-icon>
              <span class="section-label">Other</span>
            </div>
            <div class="section-rows">
              @for (s of unknownSettings(); track s.setting; let rowLast = $last) {
                <div class="setting-row" [class.modified]="modifiedSettings().has(s.setting)">
                  <div class="setting-info">
                    <span class="setting-label">{{ s.setting }}</span>
                  </div>
                  <div class="setting-control">
                    <mat-form-field appearance="outline" class="setting-field">
                      <input matInput [ngModel]="s.value ?? ''" (ngModelChange)="onTextChange(s.setting, $event)" />
                    </mat-form-field>
                  </div>
                </div>
                @if (!rowLast) { <mat-divider></mat-divider> }
              }
            </div>
          </section>
        }
      }
    </div>
  `,
  styles: [
    `
      .page-header {
        display: flex;
        justify-content: space-between;
        align-items: center;
        padding: 20px 24px;
        gap: 16px;
        background: linear-gradient(135deg, rgba(69,90,100,0.06) 0%, rgba(0,137,123,0.04) 100%);
        border-radius: 12px;
        margin-bottom: 16px;
      }
      .page-header-text { flex: 1; min-width: 0; }
      .page-header h1 { margin: 0; font-size: 24px; font-weight: 400; }
      .page-description {
        margin: 4px 0 0;
        color: var(--text-secondary, rgba(0,0,0,0.54));
        font-size: 13px;
        border-left: 3px solid #1976d2;
        padding-left: 12px;
      }
      .page-content { padding: 0 24px 48px; max-width: 860px; }
      .loading-container { display: flex; justify-content: center; padding: 64px; }

      .settings-section {
        border: 1px solid var(--divider, rgba(0,0,0,0.12));
        border-radius: 8px;
        overflow: hidden;
      }
      .section-header {
        display: flex;
        align-items: center;
        gap: 10px;
        padding: 10px 20px;
        background: var(--mat-app-surface-variant, rgba(0,0,0,0.03));
        border-bottom: 1px solid var(--divider, rgba(0,0,0,0.12));
      }
      .section-icon { font-size: 18px; width: 18px; height: 18px; opacity: 0.85; }
      .section-label { font-size: 13px; font-weight: 600; letter-spacing: 0.03em; text-transform: uppercase; color: var(--text-secondary, rgba(0,0,0,0.6)); }
      .section-gap { height: 16px; }

      .section-rows { display: flex; flex-direction: column; }
      .setting-row {
        display: flex;
        align-items: center;
        gap: 24px;
        padding: 14px 20px;
        transition: background 0.15s;
      }
      .setting-row.modified { background: rgba(25, 118, 210, 0.08); border-left: 3px solid rgba(25, 118, 210, 0.4); }
      .setting-info { flex: 1; min-width: 0; }
      .setting-label { display: block; font-size: 14px; font-weight: 500; }
      .setting-desc { display: block; font-size: 12px; color: var(--text-secondary, rgba(0,0,0,0.54)); margin-top: 2px; line-height: 1.4; }
      .setting-control { flex-shrink: 0; display: flex; align-items: center; }
      .setting-field { width: 280px; }
      .setting-field .mat-mdc-form-field-subscript-wrapper { display: none; }

      .inline-spinner { display: inline-block; margin-right: 8px; vertical-align: middle; }

      .repo-selector {
        display: flex;
        flex-direction: column;
      }
      .repo-card {
        padding: 16px 20px;
        border-bottom: 1px solid var(--divider, rgba(0,0,0,0.08));
        cursor: pointer;
        transition: background 0.15s;
      }
      .repo-card:last-child { border-bottom: none; }
      .repo-card:hover { background: rgba(46, 125, 50, 0.04); }
      .repo-card.repo-active {
        background: rgba(46, 125, 50, 0.06);
        border-left: 3px solid #2e7d32;
      }
      .repo-header {
        display: flex;
        align-items: center;
        gap: 12px;
        margin-bottom: 12px;
      }
      .repo-radio { flex-shrink: 0; display: flex; }
      .repo-check { color: #2e7d32; font-size: 22px; width: 22px; height: 22px; }
      .repo-uncheck { color: var(--text-hint, rgba(0,0,0,0.3)); font-size: 22px; width: 22px; height: 22px; }
      .repo-info { display: flex; flex-direction: column; min-width: 0; }
      .repo-name { font-size: 14px; font-weight: 500; }
      .repo-url {
        font-size: 11px;
        color: var(--text-hint, rgba(0,0,0,0.38));
        overflow: hidden;
        text-overflow: ellipsis;
        white-space: nowrap;
      }
      .repo-preview-row {
        display: flex;
        gap: 12px;
        flex-wrap: wrap;
        padding-left: 34px;
      }
      .icon-preview-item {
        display: flex;
        flex-direction: column;
        align-items: center;
        gap: 4px;
      }
      .icon-preview-img {
        width: 48px;
        height: 48px;
        object-fit: contain;
        border-radius: 8px;
        background: var(--skeleton-bg, rgba(0,0,0,0.05));
        padding: 4px;
        transition: opacity 0.2s;
      }
      .icon-preview-img.preview-error {
        opacity: 0.15;
        filter: grayscale(1);
      }
      .icon-preview-name {
        font-size: 10px;
        color: var(--text-hint, rgba(0,0,0,0.38));
        text-align: center;
      }

      @media (max-width: 599px) {
        .setting-row {
          flex-direction: column;
          align-items: stretch;
          gap: 8px;
        }
        .setting-field {
          width: 100%;
        }
      }
    `,
  ],
})
export class AdminSettingsComponent implements OnInit {
  private readonly destroyRef = inject(DestroyRef);
  private readonly settingsService = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);

  readonly settings = signal<PwebSetting[]>([]);
  readonly settingsLoading = signal(true);
  readonly modifiedSettings = signal<Map<string, string>>(new Map());
  readonly bulkSaving = signal(false);
  private readonly settingMap = computed(() => {
    const map = new Map<string, string | null>();
    for (const s of this.settings()) map.set(s.setting, s.value);
    return map;
  });

  private readonly allDefinedKeys = new Set([
    ...SETTING_GROUPS.flatMap((g) => g.settings.map((s) => s.key)),
    'uicons_pkmn', 'uicons_gym', 'uicons_raid', 'uicons_reward',
  ]);
  private readonly internalPrefixes = ['webhook_delegates:', 'scan_db', 'cf_', 'api_address', 'api_secret', 'source_raid_bosses', 'telegram_bot_token', 'enable_admin_dis', 'admin_disable_userlist', 'admin_channel_id'];

  readonly visibleGroups = computed(() =>
    SETTING_GROUPS.filter((g) => g.settings.some((s) => this.settingMap().has(s.key))),
  );

  readonly unknownSettings = computed(() =>
    this.settings().filter(
      (s) =>
        !this.allDefinedKeys.has(s.setting) &&
        !this.internalPrefixes.some((p) => s.setting.startsWith(p)),
    ),
  );

  readonly iconRepos = [
    {
      name: 'Whitewillem (Ingame)',
      base: 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons',
      previewImages: [
        { path: 'pokemon/25.png', name: 'Pikachu' },
        { path: 'pokemon/6.png', name: 'Charizard' },
        { path: 'pokemon/150.png', name: 'Mewtwo' },
        { path: 'raid/egg/5.png', name: 'T5 Egg' },
        { path: 'gym/1.png', name: 'Mystic' },
      ],
    },
    {
      name: 'Nileplumb (Home)',
      base: 'https://raw.githubusercontent.com/nileplumb/PkmnHomeIcons/master/UICONS',
      previewImages: [
        { path: 'pokemon/25.png', name: 'Pikachu' },
        { path: 'pokemon/6.png', name: 'Charizard' },
        { path: 'pokemon/150.png', name: 'Mewtwo' },
        { path: 'raid/egg/5.png', name: 'T5 Egg' },
        { path: 'gym/1.png', name: 'Mystic' },
      ],
    },
    {
      name: 'Nileplumb (Shuffle)',
      base: 'https://raw.githubusercontent.com/nileplumb/PkmnShuffleMap/master/UICONS',
      previewImages: [
        { path: 'pokemon/25.png', name: 'Pikachu' },
        { path: 'pokemon/6.png', name: 'Charizard' },
        { path: 'pokemon/150.png', name: 'Mewtwo' },
        { path: 'raid/egg/5.png', name: 'T5 Egg' },
        { path: 'gym/1.png', name: 'Mystic' },
      ],
    },
    {
      name: 'Jms412 (Home)',
      base: 'https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS',
      previewImages: [
        { path: 'pokemon/25.png', name: 'Pikachu' },
        { path: 'pokemon/6.png', name: 'Charizard' },
        { path: 'pokemon/150.png', name: 'Mewtwo' },
        { path: 'raid/egg/5.png', name: 'T5 Egg' },
        { path: 'gym/1.png', name: 'Mystic' },
      ],
    },
    {
      name: 'Jms412 (Pokedex)',
      base: 'https://raw.githubusercontent.com/jms412/PkmnPokedexIcons/master/UICONS',
      previewImages: [
        { path: 'pokemon/25.png', name: 'Pikachu' },
        { path: 'pokemon/6.png', name: 'Charizard' },
        { path: 'pokemon/150.png', name: 'Mewtwo' },
        { path: 'raid/egg/5.png', name: 'T5 Egg' },
        { path: 'gym/1.png', name: 'Mystic' },
      ],
    },
  ];

  isRepoActive(repo: { base: string }): boolean {
    const current = (this.getSettingValue('uicons_pkmn') ?? '').toLowerCase();
    return current.startsWith(repo.base.toLowerCase());
  }

  selectRepo(repo: { base: string }): void {
    const keys: Record<string, string> = {
      uicons_pkmn: `${repo.base}/pokemon`,
      uicons_raid: `${repo.base}/raid`,
      uicons_gym: `${repo.base}/gym`,
      uicons_reward: `${repo.base}/reward`,
    };
    for (const [key, value] of Object.entries(keys)) {
      this.applyChange(key, value);
      // Also update the settings list so the value is reflected immediately
      this.settings.update((list) => {
        const exists = list.some((s) => s.setting === key);
        if (exists) return list.map((s) => s.setting === key ? { ...s, value } : s);
        return [...list, { setting: key, value }];
      });
    }
    this.snackBar.open(`Selected ${repo.base.split('/').pop()} icons — click Save to apply`, 'OK', { duration: 4000 });
  }

  onPreviewError(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.classList.add('preview-error');
  }

  onPreviewLoad(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.classList.remove('preview-error');
  }

  ngOnInit(): void {
    this.settingsService.getAll().pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
      next: (settings) => { this.settings.set(settings); this.settingsLoading.set(false); },
      error: () => { this.settingsLoading.set(false); this.snackBar.open('Failed to load settings', 'OK', { duration: 3000 }); },
    });
  }

  getSettingValue(key: string): string | null | undefined {
    const map = this.settingMap();
    return map.has(key) ? map.get(key) ?? null : undefined;
  }

  isSettingVisible(meta: SettingMeta): boolean {
    if (!meta.showWhen) return true;
    return this.getBool(meta.showWhen);
  }

  getBool(key: string): boolean {
    return (this.getSettingValue(key) ?? '').toLowerCase() === 'true';
  }

  onBoolChange(key: string, value: boolean): void {
    this.applyChange(key, value ? 'True' : 'False');
  }

  onTextChange(key: string, value: string): void {
    this.applyChange(key, value);
  }

  private applyChange(key: string, value: string): void {
    this.settings.update((list) => list.map((s) => s.setting === key ? { ...s, value } : s));
    this.modifiedSettings.update((map) => { const m = new Map(map); m.set(key, value); return m; });
  }

  saveAllModified(): void {
    const entries = Array.from(this.modifiedSettings().entries());
    if (!entries.length) return;
    this.bulkSaving.set(true);
    let done = 0, errors = 0;
    for (const [key, value] of entries) {
      this.settingsService.update(key, value).pipe(takeUntilDestroyed(this.destroyRef)).subscribe({
        next: () => {
          done++;
          this.modifiedSettings.update((m) => { const n = new Map(m); n.delete(key); return n; });
          if (done + errors === entries.length) this.finish(done, errors);
        },
        error: () => { errors++; if (done + errors === entries.length) this.finish(done, errors); },
      });
    }
  }

  private finish(done: number, errors: number): void {
    this.bulkSaving.set(false);
    const msg = errors === 0 ? `${done} setting(s) saved` : `${done} saved, ${errors} failed`;
    this.snackBar.open(msg, 'OK', { duration: errors ? 5000 : 3000 });
  }
}
