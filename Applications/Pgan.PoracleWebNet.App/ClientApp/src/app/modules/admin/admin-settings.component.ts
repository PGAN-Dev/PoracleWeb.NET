import {
  ChangeDetectionStrategy,
  Component,
  OnInit,
  DestroyRef,
  ElementRef,
  HostListener,
  ViewChild,
  inject,
  signal,
  computed,
} from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatButtonToggleModule } from '@angular/material/button-toggle';
import { MatDialog, MatDialogModule } from '@angular/material/dialog';
import { MatDividerModule } from '@angular/material/divider';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatInputModule } from '@angular/material/input';
import { MatProgressSpinnerModule } from '@angular/material/progress-spinner';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatSnackBar, MatSnackBarModule } from '@angular/material/snack-bar';
import { MatTooltipModule } from '@angular/material/tooltip';
import { TranslatePipe } from '@ngx-translate/core';

import { DiscordServerConfig, OidcServerConfig, PwebSetting, SiteSetting, TelegramServerConfig } from '../../core/models';
import { I18nService } from '../../core/services/i18n.service';
import { SettingsService } from '../../core/services/settings.service';
import { ConfirmDialogComponent, ConfirmDialogData } from '../../shared/components/confirm-dialog/confirm-dialog.component';

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

/**
 * Keys deliberately withdrawn from the admin UI: they saved, persisted and read back while nothing in
 * the product consumed them, and their descriptions promised behaviour the app does not have. Rows are
 * left in the database rather than deleted. See #547, #560.
 */
const RETIRED_KEYS = [
  // Legacy Poracle keys describing a map picker this app does not have. Removed from the settings UI and
  // from SettingsMigrationService when they were retired, but rows persist in existing databases and were
  // still rendering in the "Other" catch-all. See #589.
  'disable_geomap',
  'disable_geomap_select',
  'register_command',
  'location_command',
  'provider_url',
  'gAnalyticsId',
  'patreonUrl',
  'paypalUrl',
  'site_is_https',
  'debug',
];

const SETTING_GROUPS: SettingGroup[] = [
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
        descriptionKey: 'ADMIN_SETTINGS.DISABLE_USER_GEOFENCES_DESC',
        key: 'disable_user_geofences',
        labelKey: 'ADMIN_SETTINGS.DISABLE_USER_GEOFENCES_LABEL',
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
    ],
  },
  {
    color: '#607d8b',
    icon: 'terminal',
    labelKey: 'ADMIN_SETTINGS.GROUP_COMMANDS',
    settings: [],
  },
  {
    color: '#2e7d32',
    icon: 'map',
    labelKey: 'ADMIN_SETTINGS.GROUP_MAPS_ASSETS',
    settings: [],
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
    ],
  },
  {
    color: '#ff5722',
    icon: 'bug_report',
    labelKey: 'ADMIN_SETTINGS.GROUP_DEBUG',
    settings: [],
  },
];

@Component({
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [
    FormsModule,
    MatButtonModule,
    MatButtonToggleModule,
    MatDialogModule,
    MatIconModule,
    MatInputModule,
    MatFormFieldModule,
    MatProgressSpinnerModule,
    MatSnackBarModule,
    MatSlideToggleModule,
    MatDividerModule,
    MatTooltipModule,
    TranslatePipe,
  ],
  selector: 'app-admin-settings',
  standalone: true,
  styleUrl: './admin-settings.component.scss',
  templateUrl: './admin-settings.component.html',
})
export class AdminSettingsComponent implements OnInit {
  private static readonly COLLAPSED_STORAGE_KEY = 'poracle-admin-settings-collapsed';

  private readonly allDefinedKeys = new Set([
    ...SETTING_GROUPS.flatMap(g => g.settings.map(s => s.key)),
    'uicons_pkmn',
    'uicons_gym',
    'uicons_raid',
    'uicons_reward',
    // Driven by the Authentication mode switch rather than a generic group row, but still
    // a known key so it doesn't fall through to the "Other" catch-all section.
    'enable_oidc',
    // Single-logout toggle, surfaced as a dedicated control in the Authentication section.
    'enable_oidc_slo',
    // Withdrawn from the UI because nothing reads them (#547). Listed here so they do not reappear
    // in the "Other" catch-all, which is what happened when they were only removed from their groups:
    // the same editable controls, one section lower, still promising behaviour that does not exist.
    // Their rows stay in the database, unread. See #560.
    ...RETIRED_KEYS,
  ]);

  private readonly destroyRef = inject(DestroyRef);
  private readonly dialog = inject(MatDialog);

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

  private readonly originalSnapshot = signal<AnySettingItem[]>([]);

  readonly settings = signal<AnySettingItem[]>([]);

  private readonly settingMap = computed(() => {
    const map = new Map<string, string | null>();
    for (const s of this.settings()) map.set(settingKey(s), s.value);
    return map;
  });

  private readonly settingsService = inject(SettingsService);

  private readonly snackBar = inject(MatSnackBar);

  /** Current sign-in mode, derived from enable_oidc (opt-in; absent/false = local). */
  readonly authMode = computed<'local' | 'oidc'>(() => (this.getBool('enable_oidc') ? 'oidc' : 'local'));

  readonly searchQuery = signal('');
  /**
   * The Authentication section is hand-written rather than driven by SETTING_GROUPS, so the search
   * box never touched it and a nonsense query still left it on screen. See #426.
   */
  readonly authSectionVisible = computed(() => {
    const query = this.searchQuery().trim().toLowerCase();
    if (!query) return true;
    return [
      'ADMIN_SETTINGS.GROUP_AUTH',
      'ADMIN_SETTINGS.AUTH_MODE_LABEL',
      'ADMIN_SETTINGS.AUTH_MODE_LOCAL',
      'ADMIN_SETTINGS.AUTH_MODE_OIDC',
    ].some(key => this.i18n.instant(key).toLowerCase().includes(query));
  });

  readonly bulkSaving = signal(false);
  readonly collapsedGroups = signal<Set<string>>(AdminSettingsComponent.loadCollapsed());

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

  readonly oidcConfig = signal<OidcServerConfig | null>(null);

  /** Whether the OIDC provider is fully configured in the server env (gates the SSO option). */
  readonly oidcConfigured = computed(() => this.oidcConfig()?.configured ?? false);

  /** Whether a provider end-session endpoint is configured (gates the single-logout toggle). */
  readonly oidcEndSessionConfigured = computed(() => !!this.oidcConfig()?.endSessionUrl);

  /** Single-logout admin toggle state — absent defaults to ON once the end-session URL is wired. */
  readonly oidcSloEnabled = computed(() => (this.getSettingValue('enable_oidc_slo') ?? '').toLowerCase() !== 'false');

  @ViewChild('searchInput') searchInput?: ElementRef<HTMLInputElement>;
  readonly settingsLoading = signal(true);

  readonly telegramConfig = signal<TelegramServerConfig | null>(null);

  readonly unknownSettings = computed(() =>
    this.settings().filter(s => {
      const k = settingKey(s);
      return !this.allDefinedKeys.has(k) && !this.internalPrefixes.some(p => k.startsWith(p));
    }),
  );

  readonly visibleGroups = computed(() => {
    // In OIDC sign-in mode the local provider sections are moot — hide them; the read-only
    // OIDC config card is shown by the bespoke Authentication section instead.
    const localProviderGroups = new Set(['ADMIN_SETTINGS.GROUP_DISCORD', 'ADMIN_SETTINGS.GROUP_TELEGRAM']);
    const oidcMode = this.authMode() === 'oidc';
    const query = this.searchQuery().trim();
    const base = SETTING_GROUPS.filter(g => {
      if (oidcMode && localProviderGroups.has(g.labelKey)) return false;
      // Deliberately not gated on a key already having a row. A fresh install seeds exactly one
      // (custom_title), so Alarm Types, Features, Administration and Analytics were filtered out of the
      // DOM entirely -- and since this page is the only writer, the row could never appear. The
      // row-level guard below already renders an absent key correctly. See #629.
      return true;
    });
    if (!query) return base;
    return base.map(g => ({ ...g, settings: g.settings.filter(s => this.settingMatches(s)) })).filter(g => g.settings.length > 0);
  });

  private static loadCollapsed(): Set<string> {
    try {
      const raw = localStorage.getItem(AdminSettingsComponent.COLLAPSED_STORAGE_KEY);
      if (!raw) return new Set();
      const parsed: unknown = JSON.parse(raw);
      if (Array.isArray(parsed)) return new Set(parsed.filter((x): x is string => typeof x === 'string'));
    } catch {
      // Ignore malformed/inaccessible storage.
    }
    return new Set();
  }

  discardAllModified(): void {
    this.settings.set(this.originalSnapshot().map(s => ({ ...s })));
    this.modifiedSettings.set(new Map());
  }

  /** Positive-framing checked state for a boolean setting (ON = enabled). */
  featureEnabled(meta: SettingMeta): boolean {
    return this.isInverted(meta) ? !this.getBool(meta.key) : this.getBool(meta.key);
  }

  getBool(key: string): boolean {
    return (this.getSettingValue(key) ?? '').toLowerCase() === 'true';
  }

  getSettingValue(key: string): string | null | undefined {
    const map = this.settingMap();
    return map.has(key) ? (map.get(key) ?? null) : undefined;
  }

  groupModifiedCount(group: SettingGroup): number {
    const modified = this.modifiedSettings();
    return group.settings.reduce((acc, s) => acc + (modified.has(s.key) ? 1 : 0), 0);
  }

  groupSummary(group: SettingGroup): string {
    const disableKeys = group.settings.filter(s => s.key.startsWith('disable_'));
    if (disableKeys.length === 0) return '';
    // Positive framing: report how many features are enabled (i.e. NOT disabled).
    const count = disableKeys.reduce((acc, s) => acc + (this.getBool(s.key) ? 0 : 1), 0);
    return this.i18n.instant('ADMIN_SETTINGS.SUMMARY_ENABLED', { count, total: disableKeys.length });
  }

  /** Translated label/description with current search matches wrapped in <mark>. */
  highlight(key: string): string {
    const text = this.i18n.instant(key);
    const escaped = this.escapeHtml(text);
    const query = this.searchQuery().trim();
    if (!query) return escaped;
    const pattern = new RegExp(`(${this.escapeRegExp(this.escapeHtml(query))})`, 'gi');
    return escaped.replace(pattern, '<mark>$1</mark>');
  }

  /** Search-active force-expands; otherwise read collapsed membership. */
  isCollapsed(labelKey: string): boolean {
    if (this.searchQuery().trim()) return false;
    return this.collapsedGroups().has(labelKey);
  }

  /**
   * Boolean settings are presented in positive framing: a toggle ON always means "feature
   * enabled". The stored `disable_*` keys have inverted semantics (true = disabled), so they are
   * displayed and written inverted. `enable_*`/other booleans pass through unchanged. The stored
   * value is never changed in meaning — only the presentation — so backend feature-gating is
   * unaffected.
   */
  isInverted(meta: SettingMeta): boolean {
    return meta.key.startsWith('disable_');
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
          this.originalSnapshot.set(settings.map(s => ({ ...s })));
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

    this.settingsService
      .getOidcConfig()
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({ next: config => this.oidcConfig.set(config) });
  }

  onBoolChange(key: string, value: boolean): void {
    this.applyChange(key, value ? 'True' : 'False');
  }

  /** Persist a positive-framing toggle, converting back to the stored (possibly inverted) value. */
  onFeatureToggle(meta: SettingMeta, checked: boolean): void {
    this.onBoolChange(meta.key, this.isInverted(meta) ? !checked : checked);
  }

  @HostListener('document:keydown', ['$event'])
  onKeydown(event: KeyboardEvent): void {
    const target = event.target as HTMLElement | null;
    const tag = target?.tagName?.toLowerCase();
    const isEditable = tag === 'input' || tag === 'textarea' || tag === 'select' || target?.isContentEditable === true;

    if (event.key === 'Escape' && this.searchInput && target === this.searchInput.nativeElement) {
      this.searchQuery.set('');
      return;
    }

    const isSlash = event.key === '/' && !isEditable;
    const isCmdK = (event.ctrlKey || event.metaKey) && (event.key === 'k' || event.key === 'K');
    if (isSlash || isCmdK) {
      event.preventDefault();
      this.searchInput?.nativeElement.focus();
    }
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
    // Enables first. The anti-lockout guard on the server reads the *other* login key from the
    // database, so turning Discord off and Telegram on in one batch failed on whichever request landed
    // first: the Discord PUT still saw enable_telegram false and answered 400, leaving a partial save
    // and a message about a state the admin was in the middle of leaving. See #633.
    const entries = Array.from(this.modifiedSettings().entries()).sort(
      ([, a], [, b]) => Number(this.isDisablingLogin(b)) - Number(this.isDisablingLogin(a)),
    );
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

  /**
   * Switch the sign-in mode. OIDC is gated on the provider being configured in env, and a
   * confirmation warns that local login is bypassed (and about the AUTH_FORCE_LOCAL recovery
   * path). The change is staged like any other setting — it persists on Save.
   */
  setAuthMode(mode: 'local' | 'oidc'): void {
    if (mode === this.authMode()) return;

    if (mode === 'oidc') {
      if (!this.oidcConfigured()) return;
      const provider = this.oidcConfig()?.providerName || this.i18n.instant('ADMIN_SETTINGS.AUTH_MODE_OIDC');
      const ref = this.dialog.open(ConfirmDialogComponent, {
        data: {
          confirmText: this.i18n.instant('ADMIN_SETTINGS.AUTH_MODE_SWITCH_CONFIRM'),
          message: this.i18n.instant('ADMIN_SETTINGS.AUTH_MODE_OIDC_CONFIRM_MSG', { provider }),
          title: this.i18n.instant('ADMIN_SETTINGS.AUTH_MODE_OIDC_CONFIRM_TITLE'),
          warn: true,
        } as ConfirmDialogData,
      });
      ref.afterClosed().subscribe(confirmed => {
        if (confirmed) this.applyChange('enable_oidc', 'True');
      });
      return;
    }

    this.applyChange('enable_oidc', 'False');
  }

  toggleGroup(labelKey: string): void {
    const next = new Set(this.collapsedGroups());
    if (next.has(labelKey)) next.delete(labelKey);
    else next.add(labelKey);
    this.collapsedGroups.set(next);
    try {
      localStorage.setItem(AdminSettingsComponent.COLLAPSED_STORAGE_KEY, JSON.stringify([...next]));
    } catch {
      // Ignore persistence failures (e.g. private mode quota).
    }
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

  private escapeHtml(text: string): string {
    return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
  }

  private escapeRegExp(text: string): string {
    return text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
  }

  private finish(done: number, errors: number, errorMessages: string[] = []): void {
    this.bulkSaving.set(false);
    if (errors === 0) {
      // Saved values become the new baseline for discard.
      this.originalSnapshot.set(this.settings().map(s => ({ ...s })));
    }
    const msg =
      errors === 0
        ? this.i18n.instant('ADMIN_SETTINGS.SAVE_SUCCESS', { count: done })
        : errorMessages.length > 0
          ? errorMessages.join(' ')
          : this.i18n.instant('ADMIN_SETTINGS.SAVE_PARTIAL', { done, errors });
    this.snackBar.open(msg, this.i18n.instant('COMMON.OK'), { duration: errors ? 5000 : 3000 });
  }

  /** Whether a pending value would switch a login method off. See #633. */
  private isDisablingLogin(value: unknown): boolean {
    return String(value).toLowerCase() === 'false';
  }

  private settingMatches(meta: SettingMeta): boolean {
    const query = this.searchQuery().trim().toLowerCase();
    if (!query) return true;
    const haystack = `${this.i18n.instant(meta.labelKey)} ${this.i18n.instant(meta.descriptionKey)} ${meta.key}`.toLowerCase();
    return haystack.includes(query);
  }
}
