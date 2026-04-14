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
import { TranslateModule } from '@ngx-translate/core';

import { DiscordServerConfig, PwebSetting, SiteSetting, TelegramServerConfig } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
import { SettingsService } from '../../core/services/settings.service';

/** Union type for backward compatibility during migration */
type AnySettingItem = PwebSetting | SiteSetting;

/** Extract the key from either setting shape */
function settingKey(item: AnySettingItem): string {
  return 'key' in item ? item.key : item.setting;
}

interface SettingMeta {
  descriptionKey: string;
  key: string;
  labelKey: string;
  /** Only show this setting when another boolean setting is True */
  showWhen?: string;
  type: 'text' | 'url' | 'boolean';
}

interface SettingGroup {
  color: string;
  icon: string;
  labelKey: string;
  settings: SettingMeta[];
}

const SETTING_GROUPS: SettingGroup[] = [
  {
    color: '#1976d2',
    icon: 'palette',
    labelKey: 'ADMIN_SETTINGS.GROUP_BRANDING',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.CUSTOM_TITLE_DESC',
        key: 'custom_title',
        labelKey: 'ADMIN_SETTINGS.CUSTOM_TITLE_LABEL',
        type: 'text',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.HEADER_LOGO_URL_DESC',
        key: 'header_logo_url',
        labelKey: 'ADMIN_SETTINGS.HEADER_LOGO_URL_LABEL',
        type: 'url',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.HIDE_HEADER_LOGO_DESC',
        key: 'hide_header_logo',
        labelKey: 'ADMIN_SETTINGS.HIDE_HEADER_LOGO_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.CUSTOM_PAGE_NAME_DESC',
        key: 'custom_page_name',
        labelKey: 'ADMIN_SETTINGS.CUSTOM_PAGE_NAME_LABEL',
        type: 'text',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.CUSTOM_PAGE_URL_DESC',
        key: 'custom_page_url',
        labelKey: 'ADMIN_SETTINGS.CUSTOM_PAGE_URL_LABEL',
        type: 'url',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.CUSTOM_PAGE_ICON_DESC',
        key: 'custom_page_icon',
        labelKey: 'ADMIN_SETTINGS.CUSTOM_PAGE_ICON_LABEL',
        type: 'text',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.FAVICON_URL_DESC',
        key: 'favicon_url',
        labelKey: 'ADMIN_SETTINGS.FAVICON_URL_LABEL',
        type: 'url',
      },
    ],
  },
  {
    color: '#ff9800',
    icon: 'notifications',
    labelKey: 'ADMIN_SETTINGS.GROUP_ALARM_TYPES',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_MONS_DESC',
        key: 'disable_mons',
        labelKey: 'ADMIN_SETTINGS.DISABLE_MONS_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_RAIDS_DESC',
        key: 'disable_raids',
        labelKey: 'ADMIN_SETTINGS.DISABLE_RAIDS_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_QUESTS_DESC',
        key: 'disable_quests',
        labelKey: 'ADMIN_SETTINGS.DISABLE_QUESTS_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_INVASIONS_DESC',
        key: 'disable_invasions',
        labelKey: 'ADMIN_SETTINGS.DISABLE_INVASIONS_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_LURES_DESC',
        key: 'disable_lures',
        labelKey: 'ADMIN_SETTINGS.DISABLE_LURES_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_NESTS_DESC',
        key: 'disable_nests',
        labelKey: 'ADMIN_SETTINGS.DISABLE_NESTS_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_GYMS_DESC',
        key: 'disable_gyms',
        labelKey: 'ADMIN_SETTINGS.DISABLE_GYMS_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_FORT_CHANGES_DESC',
        key: 'disable_fort_changes',
        labelKey: 'ADMIN_SETTINGS.DISABLE_FORT_CHANGES_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_MAXBATTLES_DESC',
        key: 'disable_maxbattles',
        labelKey: 'ADMIN_SETTINGS.DISABLE_MAXBATTLES_LABEL',
        type: 'boolean',
      },
    ],
  },
  {
    color: '#4caf50',
    icon: 'tune',
    labelKey: 'ADMIN_SETTINGS.GROUP_FEATURES',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_AREAS_DESC',
        key: 'disable_areas',
        labelKey: 'ADMIN_SETTINGS.DISABLE_AREAS_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_PROFILES_DESC',
        key: 'disable_profiles',
        labelKey: 'ADMIN_SETTINGS.DISABLE_PROFILES_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_LOCATION_DESC',
        key: 'disable_location',
        labelKey: 'ADMIN_SETTINGS.DISABLE_LOCATION_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_NOMINATIM_DESC',
        key: 'disable_nominatim',
        labelKey: 'ADMIN_SETTINGS.DISABLE_NOMINATIM_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_GEOMAP_DESC',
        key: 'disable_geomap',
        labelKey: 'ADMIN_SETTINGS.DISABLE_GEOMAP_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_GEOMAP_SELECT_DESC',
        key: 'disable_geomap_select',
        labelKey: 'ADMIN_SETTINGS.DISABLE_GEOMAP_SELECT_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.ENABLE_TEMPLATES_DESC',
        key: 'enable_templates',
        labelKey: 'ADMIN_SETTINGS.ENABLE_TEMPLATES_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.ALLOWED_LANGUAGES_DESC',
        key: 'allowed_languages',
        labelKey: 'ADMIN_SETTINGS.ALLOWED_LANGUAGES_LABEL',
        type: 'text',
      },
    ],
  },
  {
    color: '#f44336',
    icon: 'admin_panel_settings',
    labelKey: 'ADMIN_SETTINGS.GROUP_ADMINISTRATION',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.ENABLE_ROLES_DESC',
        key: 'enable_roles',
        labelKey: 'ADMIN_SETTINGS.ENABLE_ROLES_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.ALLOWED_ROLE_IDS_DESC',
        key: 'allowed_role_ids',
        labelKey: 'ADMIN_SETTINGS.ALLOWED_ROLE_IDS_LABEL',
        showWhen: 'enable_roles',
        type: 'text',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.ADMIN_ALLOWED_LANGUAGES_DESC',
        key: 'allowed_languages',
        labelKey: 'ADMIN_SETTINGS.ADMIN_ALLOWED_LANGUAGES_LABEL',
        type: 'text',
      },
    ],
  },
  {
    color: '#607d8b',
    icon: 'terminal',
    labelKey: 'ADMIN_SETTINGS.GROUP_COMMANDS',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.REGISTER_COMMAND_DESC',
        key: 'register_command',
        labelKey: 'ADMIN_SETTINGS.REGISTER_COMMAND_LABEL',
        type: 'text',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.LOCATION_COMMAND_DESC',
        key: 'location_command',
        labelKey: 'ADMIN_SETTINGS.LOCATION_COMMAND_LABEL',
        type: 'text',
      },
    ],
  },
  {
    color: '#0088cc',
    icon: 'send',
    labelKey: 'ADMIN_SETTINGS.GROUP_TELEGRAM',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.ENABLE_TELEGRAM_DESC',
        key: 'enable_telegram',
        labelKey: 'ADMIN_SETTINGS.ENABLE_TELEGRAM_LABEL',
        type: 'boolean',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.TELEGRAM_BOT_DESC',
        key: 'telegram_bot',
        labelKey: 'ADMIN_SETTINGS.TELEGRAM_BOT_LABEL',
        type: 'text',
      },
    ],
  },
  {
    color: '#5865F2',
    icon: 'forum',
    labelKey: 'ADMIN_SETTINGS.GROUP_DISCORD',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.ENABLE_DISCORD_DESC',
        key: 'enable_discord',
        labelKey: 'ADMIN_SETTINGS.ENABLE_DISCORD_LABEL',
        type: 'boolean',
      },
    ],
  },
  {
    color: '#2e7d32',
    icon: 'map',
    labelKey: 'ADMIN_SETTINGS.GROUP_MAPS_ASSETS',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.PROVIDER_URL_DESC',
        key: 'provider_url',
        labelKey: 'ADMIN_SETTINGS.PROVIDER_URL_LABEL',
        type: 'url',
      },
    ],
  },
  {
    color: '#7b1fa2',
    icon: 'bar_chart',
    labelKey: 'ADMIN_SETTINGS.GROUP_ANALYTICS_LINKS',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.SIGNUP_URL_DESC',
        key: 'signup_url',
        labelKey: 'ADMIN_SETTINGS.SIGNUP_URL_LABEL',
        type: 'url',
      },
      {
        descriptionKey: 'ADMIN_SETTINGS.GANALYTICSID_DESC',
        key: 'gAnalyticsId',
        labelKey: 'ADMIN_SETTINGS.GANALYTICSID_LABEL',
        type: 'text',
      },
      { descriptionKey: 'ADMIN_SETTINGS.PATREONURL_DESC', key: 'patreonUrl', labelKey: 'ADMIN_SETTINGS.PATREONURL_LABEL', type: 'url' },
      { descriptionKey: 'ADMIN_SETTINGS.PAYPALURL_DESC', key: 'paypalUrl', labelKey: 'ADMIN_SETTINGS.PAYPALURL_LABEL', type: 'url' },
    ],
  },
  {
    color: '#ff5722',
    icon: 'bug_report',
    labelKey: 'ADMIN_SETTINGS.GROUP_DEBUG',
    settings: [
      {
        descriptionKey: 'ADMIN_SETTINGS.SITE_IS_HTTPS_DESC',
        key: 'site_is_https',
        labelKey: 'ADMIN_SETTINGS.SITE_IS_HTTPS_LABEL',
        type: 'boolean',
      },
      { descriptionKey: 'ADMIN_SETTINGS.DEBUG_DESC', key: 'debug', labelKey: 'ADMIN_SETTINGS.DEBUG_LABEL', type: 'boolean' },
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
    TranslateModule,
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
  private readonly i18n = inject(I18nService);
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
  readonly telegramConfig = signal<TelegramServerConfig | null>(null);

  readonly unknownSettings = computed(() =>
    this.settings().filter(s => {
      const k = settingKey(s);
      return !this.allDefinedKeys.has(k) && !this.internalPrefixes.some(p => k.startsWith(p));
    }),
  );

  readonly visibleGroups = computed(() =>
    SETTING_GROUPS.filter(
      g =>
        g.settings.some(s => this.settingMap().has(s.key)) ||
        (g.labelKey === 'ADMIN_SETTINGS.GROUP_DISCORD' && this.discordConfig() !== null) ||
        (g.labelKey === 'ADMIN_SETTINGS.GROUP_TELEGRAM' && this.telegramConfig() !== null),
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
          this.snackBar.open(this.i18n.instant('ADMIN_SETTINGS.LOAD_FAILED'), this.i18n.instant('COMMON.OK'), { duration: 3000 });
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

    this.settingsService
      .getTelegramConfig()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: config => this.telegramConfig.set(config) });
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
    const errorMessages: string[] = [];
    for (const [key, value] of entries) {
      this.settingsService
        .update(key, value)
        .pipe(takeUntilDestroyed(this.destroyRef))
        .subscribe({
          error: (err: { error?: { error?: string } }) => {
            errors++;
            if (err.error?.error) errorMessages.push(err.error.error);
            if (done + errors === entries.length) this.finish(done, errors, errorMessages);
          },
          next: () => {
            done++;
            this.modifiedSettings.update(m => {
              const n = new Map(m);
              n.delete(key);
              return n;
            });
            if (done + errors === entries.length) this.finish(done, errors, errorMessages);
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
    this.snackBar.open(
      this.i18n.instant('ADMIN_SETTINGS.ICONS_SELECTED', { repo: repo.base.split('/').pop() }),
      this.i18n.instant('COMMON.OK'),
      { duration: 4000 },
    );
  }

  private applyChange(key: string, value: string): void {
    this.settings.update(list => {
      const exists = list.some(s => settingKey(s) === key);
      if (exists) return list.map(s => (settingKey(s) === key ? { ...s, value } : s));
      return [...list, { key, value } as unknown as AnySettingItem];
    });
    this.modifiedSettings.update(map => {
      const m = new Map(map);
      m.set(key, value);
      return m;
    });
  }

  private finish(done: number, errors: number, errorMessages: string[] = []): void {
    this.bulkSaving.set(false);
    const msg =
      errors === 0
        ? this.i18n.instant('ADMIN_SETTINGS.SAVE_SUCCESS', { count: done })
        : errorMessages.length > 0
          ? errorMessages.join(' ')
          : this.i18n.instant('ADMIN_SETTINGS.SAVE_PARTIAL', { done, errors });
    this.snackBar.open(msg, this.i18n.instant('COMMON.OK'), { duration: errors ? 5000 : 3000 });
  }
}
