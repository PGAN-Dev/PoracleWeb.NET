import { ChangeDetectionStrategy, Component, OnInit, DestroyRef, inject, signal, computed } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';

import { DiscordServerConfig, PwebSetting, SiteSetting } from '../../core/models';
import { SettingsService } from '../../core/services/settings.service';

/** Union type for backward compatibility during migration */
type AnySettingItem = PwebSetting | SiteSetting;

/** Extract the key from either setting shape */
function settingKey(item: AnySettingItem): string {
  return 'key' in item ? item.key : item.setting;
}

interface SettingMeta {
  description: string;
  key: string;
  label: string;
  /** Only show this setting when another boolean setting is True */
  showWhen?: string;
  type: 'text' | 'url' | 'boolean';
}

interface SettingGroup {
  color: string;
  icon: string;
  label: string;
  settings: SettingMeta[];
}

const SETTING_GROUPS: SettingGroup[] = [
  {
    color: '#1976d2',
    icon: 'palette',
    label: 'Branding',
    settings: [
      { description: 'Name shown in the browser tab and page header.', key: 'custom_title', label: 'Site Title', type: 'text' },
      {
        description: 'URL for a custom logo image in the header (replaces the Pokeball). Leave empty for default.',
        key: 'header_logo_url',
        label: 'Header Logo URL',
        type: 'url',
      },
      {
        description: 'Hide the Pokeball/logo from the header entirely.',
        key: 'hide_header_logo',
        label: 'Hide Header Logo',
        type: 'boolean',
      },
      {
        description: 'Label for the custom navigation link (e.g. "Back To Map").',
        key: 'custom_page_name',
        label: 'Nav Link Label',
        type: 'text',
      },
      { description: 'URL the custom nav link points to.', key: 'custom_page_url', label: 'Nav Link URL', type: 'url' },
      {
        description: 'FontAwesome class for the nav link icon (e.g. "fas fa-map").',
        key: 'custom_page_icon',
        label: 'Nav Link Icon',
        type: 'text',
      },
    ],
  },
  {
    color: '#ff9800',
    icon: 'notifications',
    label: 'Alarm Types',
    settings: [
      { description: 'Hide Pokémon alarm management from all users.', key: 'disable_mons', label: 'Disable Pokémon', type: 'boolean' },
      { description: 'Hide raid alarm management from all users.', key: 'disable_raids', label: 'Disable Raids', type: 'boolean' },
      { description: 'Hide quest alarm management from all users.', key: 'disable_quests', label: 'Disable Quests', type: 'boolean' },
      {
        description: 'Hide invasion alarm management from all users.',
        key: 'disable_invasions',
        label: 'Disable Invasions',
        type: 'boolean',
      },
      { description: 'Hide lure alarm management from all users.', key: 'disable_lures', label: 'Disable Lures', type: 'boolean' },
      { description: 'Hide nest alarm management from all users.', key: 'disable_nests', label: 'Disable Nests', type: 'boolean' },
      { description: 'Hide gym alarm management from all users.', key: 'disable_gyms', label: 'Disable Gyms', type: 'boolean' },
    ],
  },
  {
    color: '#4caf50',
    icon: 'tune',
    label: 'Features',
    settings: [
      {
        description: 'Prevent users from managing their area subscriptions.',
        key: 'disable_areas',
        label: 'Disable Areas',
        type: 'boolean',
      },
      {
        description: 'Prevent users from creating and switching alarm profiles.',
        key: 'disable_profiles',
        label: 'Disable Profiles',
        type: 'boolean',
      },
      { description: 'Prevent users from setting a home location.', key: 'disable_location', label: 'Disable Location', type: 'boolean' },
      {
        description: 'Disable Nominatim address search for location picking.',
        key: 'disable_nominatim',
        label: 'Disable Geocoding',
        type: 'boolean',
      },
      { description: 'Hide the interactive geofence map entirely.', key: 'disable_geomap', label: 'Disable Map View', type: 'boolean' },
      {
        description: 'Prevent users from selecting areas by clicking the map.',
        key: 'disable_geomap_select',
        label: 'Disable Map Area Selection',
        type: 'boolean',
      },
      {
        description: 'Allow users to choose notification message templates.',
        key: 'enable_templates',
        label: 'Enable Templates',
        type: 'boolean',
      },
    ],
  },
  {
    color: '#f44336',
    icon: 'admin_panel_settings',
    label: 'Administration',
    settings: [
      {
        description: 'Only allow users with specific Discord roles to log in. Requires Bot Token and Guild ID.',
        key: 'enable_roles',
        label: 'Enable Role-Based Access',
        type: 'boolean',
      },
      {
        description: 'Comma-separated Discord role IDs that grant access (e.g. "123456789,987654321"). Leave empty to allow all.',
        key: 'allowed_role_ids',
        label: 'Allowed Role IDs',
        showWhen: 'enable_roles',
        type: 'text',
      },
      {
        description: 'Comma-separated list of language codes users can select (e.g. "en,de,fr").',
        key: 'allowed_languages',
        label: 'Allowed Languages',
        type: 'text',
      },
    ],
  },
  {
    color: '#607d8b',
    icon: 'terminal',
    label: 'Commands',
    settings: [
      {
        description: 'The Poracle bot command users run to register (e.g. "$!register").',
        key: 'register_command',
        label: 'Register Command',
        type: 'text',
      },
      {
        description: 'The Poracle bot command users run to set their location.',
        key: 'location_command',
        label: 'Location Command',
        type: 'text',
      },
    ],
  },
  {
    color: '#0088cc',
    icon: 'send',
    label: 'Telegram',
    settings: [
      {
        description: 'Allow users to log in and manage alarms via Telegram.',
        key: 'enable_telegram',
        label: 'Enable Telegram',
        type: 'boolean',
      },
      { description: 'Telegram bot username (without @).', key: 'telegram_bot', label: 'Bot Username', type: 'text' },
    ],
  },
  {
    color: '#5865F2',
    icon: 'forum',
    label: 'Discord',
    settings: [
      {
        description: 'Allow users to log in and manage alarms via Discord OAuth2.',
        key: 'enable_discord',
        label: 'Enable Discord',
        type: 'boolean',
      },
    ],
  },
  {
    color: '#2e7d32',
    icon: 'map',
    label: 'Maps & Assets',
    settings: [
      {
        description: 'URL template for the map tile provider (used for static maps).',
        key: 'provider_url',
        label: 'Map Tile URL',
        type: 'url',
      },
    ],
  },
  {
    color: '#7b1fa2',
    icon: 'bar_chart',
    label: 'Analytics & Links',
    settings: [
      { description: 'GA4 measurement ID (leave blank to disable).', key: 'gAnalyticsId', label: 'Google Analytics ID', type: 'text' },
      { description: 'Link to your Patreon page shown in the UI.', key: 'patreonUrl', label: 'Patreon URL', type: 'url' },
      { description: 'Link to your PayPal donation page shown in the UI.', key: 'paypalUrl', label: 'PayPal URL', type: 'url' },
    ],
  },
  {
    color: '#ff5722',
    icon: 'bug_report',
    label: 'Debug',
    settings: [
      {
        description: 'Mark the site as running over HTTPS (affects cookie security).',
        key: 'site_is_https',
        label: 'Site Is HTTPS',
        type: 'boolean',
      },
      { description: 'Enable verbose debug logging (not recommended in production).', key: 'debug', label: 'Debug Mode', type: 'boolean' },
    ],
  },
];

@Component({
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
    MatTooltipModule,
  ],
  selector: 'app-admin-settings',
  standalone: true,
  styleUrl: './admin-settings.component.scss',
  templateUrl: './admin-settings.component.html',
})
export class AdminSettingsComponent implements OnInit {
  private readonly allDefinedKeys = new Set([
    ...SETTING_GROUPS.flatMap(g => g.settings.map(s => s.key)),
    'uicons_pkmn',
    'uicons_gym',
    'uicons_raid',
    'uicons_reward',
  ]);

  private readonly destroyRef = inject(DestroyRef);
  private readonly internalPrefixes = [
    'webhook_delegates:',
    'quick_pick:',
    'user_quick_pick:',
    'qp_applied:',
    'scan_db',
    'cf_',
    'api_address',
    'api_secret',
    'source_raid_bosses',
    'telegram_bot_token',
    'enable_admin_dis',
    'admin_disable_userlist',
    'admin_channel_id',
    'migration_completed',
  ];

  readonly settings = signal<AnySettingItem[]>([]);
  private readonly settingMap = computed(() => {
    const map = new Map<string, string | null>();
    for (const s of this.settings()) map.set(settingKey(s), s.value);
    return map;
  });

  private readonly settingsService = inject(SettingsService);
  private readonly snackBar = inject(MatSnackBar);
  readonly bulkSaving = signal(false);
  readonly discordConfig = signal<DiscordServerConfig | null>(null);

  readonly iconRepos = [
    {
      name: 'Whitewillem (Ingame)',
      base: 'https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons',
      previewImages: [
        { name: 'Pikachu', path: 'pokemon/25.png' },
        { name: 'Charizard', path: 'pokemon/6.png' },
        { name: 'Mewtwo', path: 'pokemon/150.png' },
        { name: 'T5 Egg', path: 'raid/egg/5.png' },
        { name: 'Mystic', path: 'gym/1.png' },
      ],
    },
    {
      name: 'Nileplumb (Home)',
      base: 'https://raw.githubusercontent.com/nileplumb/PkmnHomeIcons/master/UICONS',
      previewImages: [
        { name: 'Pikachu', path: 'pokemon/25.png' },
        { name: 'Charizard', path: 'pokemon/6.png' },
        { name: 'Mewtwo', path: 'pokemon/150.png' },
        { name: 'T5 Egg', path: 'raid/egg/5.png' },
        { name: 'Mystic', path: 'gym/1.png' },
      ],
    },
    {
      name: 'Nileplumb (Shuffle)',
      base: 'https://raw.githubusercontent.com/nileplumb/PkmnShuffleMap/master/UICONS',
      previewImages: [
        { name: 'Pikachu', path: 'pokemon/25.png' },
        { name: 'Charizard', path: 'pokemon/6.png' },
        { name: 'Mewtwo', path: 'pokemon/150.png' },
        { name: 'T5 Egg', path: 'raid/egg/5.png' },
        { name: 'Mystic', path: 'gym/1.png' },
      ],
    },
    {
      name: 'Jms412 (Home)',
      base: 'https://raw.githubusercontent.com/jms412/PkmnHomeIcons/master/UICONS',
      previewImages: [
        { name: 'Pikachu', path: 'pokemon/25.png' },
        { name: 'Charizard', path: 'pokemon/6.png' },
        { name: 'Mewtwo', path: 'pokemon/150.png' },
        { name: 'T5 Egg', path: 'raid/egg/5.png' },
        { name: 'Mystic', path: 'gym/1.png' },
      ],
    },
    {
      name: 'Jms412 (Pokedex)',
      base: 'https://raw.githubusercontent.com/jms412/PkmnPokedexIcons/master/UICONS',
      previewImages: [
        { name: 'Pikachu', path: 'pokemon/25.png' },
        { name: 'Charizard', path: 'pokemon/6.png' },
        { name: 'Mewtwo', path: 'pokemon/150.png' },
        { name: 'T5 Egg', path: 'raid/egg/5.png' },
        { name: 'Mystic', path: 'gym/1.png' },
      ],
    },
  ];

  readonly modifiedSettings = signal<Map<string, string>>(new Map());

  readonly settingsLoading = signal(true);

  readonly unknownSettings = computed(() =>
    this.settings().filter(s => {
      const k = settingKey(s);
      return !this.allDefinedKeys.has(k) && !this.internalPrefixes.some(p => k.startsWith(p));
    }),
  );

  readonly visibleGroups = computed(() =>
    SETTING_GROUPS.filter(
      g => g.settings.some(s => this.settingMap().has(s.key)) || (g.label === 'Discord' && this.discordConfig() !== null),
    ),
  );

  getBool(key: string): boolean {
    return (this.getSettingValue(key) ?? '').toLowerCase() === 'true';
  }

  getSettingValue(key: string): string | null | undefined {
    const map = this.settingMap();
    return map.has(key) ? (map.get(key) ?? null) : undefined;
  }

  isRepoActive(repo: { base: string }): boolean {
    const current = (this.getSettingValue('uicons_pkmn') ?? '').toLowerCase();
    return current.startsWith(repo.base.toLowerCase());
  }

  isSettingVisible(meta: SettingMeta): boolean {
    if (!meta.showWhen) return true;
    return this.getBool(meta.showWhen);
  }

  /** Get the key string from an AnySettingItem (for use in the template) */
  itemKey(item: AnySettingItem): string {
    return settingKey(item);
  }

  ngOnInit(): void {
    this.settingsService
      .getAll()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        error: () => {
          this.settingsLoading.set(false);
          this.snackBar.open('Failed to load settings', 'OK', { duration: 3000 });
        },
        next: settings => {
          this.settings.set(settings);
          this.settingsLoading.set(false);
        },
      });

    this.settingsService
      .getDiscordConfig()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: config => this.discordConfig.set(config) });
  }

  onBoolChange(key: string, value: boolean): void {
    this.applyChange(key, value ? 'True' : 'False');
  }

  onPreviewError(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.classList.add('preview-error');
  }

  onPreviewLoad(event: Event): void {
    const img = event.target as HTMLImageElement;
    img.classList.remove('preview-error');
  }

  onTextChange(key: string, value: string): void {
    this.applyChange(key, value);
  }

  saveAllModified(): void {
    const entries = Array.from(this.modifiedSettings().entries());
    if (!entries.length) return;
    this.bulkSaving.set(true);
    let done = 0,
      errors = 0;
    for (const [key, value] of entries) {
      this.settingsService
        .update(key, value)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: () => {
            errors++;
            if (done + errors === entries.length) this.finish(done, errors);
          },
          next: () => {
            done++;
            this.modifiedSettings.update(m => {
              const n = new Map(m);
              n.delete(key);
              return n;
            });
            if (done + errors === entries.length) this.finish(done, errors);
          },
        });
    }
  }

  selectRepo(repo: { base: string }): void {
    const keys: Record<string, string> = {
      uicons_raid: `${repo.base}/raid`,
      uicons_gym: `${repo.base}/gym`,
      uicons_pkmn: `${repo.base}/pokemon`,
      uicons_reward: `${repo.base}/reward`,
    };
    for (const [key, value] of Object.entries(keys)) {
      this.applyChange(key, value);
      // Also update the settings list so the value is reflected immediately
      this.settings.update(list => {
        const exists = list.some(s => settingKey(s) === key);
        if (exists) return list.map(s => (settingKey(s) === key ? { ...s, key, value } : s));
        return [...list, { key, value } as unknown as AnySettingItem];
      });
    }
    this.snackBar.open(`Selected ${repo.base.split('/').pop()} icons — click Save to apply`, 'OK', { duration: 4000 });
  }

  private applyChange(key: string, value: string): void {
    this.settings.update(list => list.map(s => (settingKey(s) === key ? { ...s, value } : s)));
    this.modifiedSettings.update(map => {
      const m = new Map(map);
      m.set(key, value);
      return m;
    });
  }

  private finish(done: number, errors: number): void {
    this.bulkSaving.set(false);
    const msg = errors === 0 ? `${done} setting(s) saved` : `${done} saved, ${errors} failed`;
    this.snackBar.open(msg, 'OK', { duration: errors ? 5000 : 3000 });
  }
}
