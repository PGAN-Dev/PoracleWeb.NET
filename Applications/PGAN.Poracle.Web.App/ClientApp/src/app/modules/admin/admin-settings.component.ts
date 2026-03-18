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
      { key: 'enable_roles', label: 'Enable Role-Based Access', description: 'Restrict access based on Discord roles.', type: 'boolean' },
      { key: 'enable_admin_dis', label: 'Enable Admin Discord Features', description: 'Enable Discord-specific admin tools.', type: 'boolean' },
      { key: 'admin_disable_userlist', label: 'Hide User List from Non-Admins', description: 'Prevent non-admin users from seeing other registered users.', type: 'boolean' },
      { key: 'admin_channel_id', label: 'Admin Channel', description: 'Discord channel ID used for admin notifications.', type: 'text' },
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
      { key: 'uicons_pkmn', label: 'Pokémon Icons URL', description: 'Base URL for UIcons Pokémon sprite assets.', type: 'url' },
      { key: 'uicons_gym', label: 'Gym Icons URL', description: 'Base URL for UIcons gym sprite assets.', type: 'url' },
      { key: 'uicons_raid', label: 'Raid Icons URL', description: 'Base URL for UIcons raid sprite assets.', type: 'url' },
      { key: 'uicons_reward', label: 'Reward Icons URL', description: 'Base URL for UIcons quest reward sprite assets.', type: 'url' },
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
                @if (getSettingValue(meta.key) !== null) {
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

  private readonly allDefinedKeys = new Set(SETTING_GROUPS.flatMap((g) => g.settings.map((s) => s.key)));
  private readonly internalPrefixes = ['webhook_delegates:', 'scan_db', 'cf_', 'api_address', 'api_secret', 'source_raid_bosses', 'telegram_bot_token'];

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
