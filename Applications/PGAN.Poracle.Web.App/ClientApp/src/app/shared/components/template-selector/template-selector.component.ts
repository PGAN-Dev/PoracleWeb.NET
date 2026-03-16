import { Component, EventEmitter, Input, OnInit, Output, inject, signal, computed, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatSelectModule } from '@angular/material/select';
import { MatIconModule } from '@angular/material/icon';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';
import { MatChipsModule } from '@angular/material/chips';
import { MatButtonModule } from '@angular/material/button';
import { TemplateService } from '../../../core/services/template.service';
import { ConfigService } from '../../../core/services/config.service';
import { AuthService } from '../../../core/services/auth.service';

interface DtsRaw {
  id: number;
  type: string;
  platform: string;
  template: {
    embed?: {
      color?: string;
      title?: string;
      description?: string;
      fields?: { name: string; value: string; inline?: boolean }[];
      thumbnail?: { url: string };
      image?: { url: string };
      footer?: { text: string; icon_url?: string };
    };
    content?: string;
  };
}

interface ConditionToggle {
  name: string;
  label: string;
  enabled: boolean;
}

// Friendly labels for Handlebars conditions
const CONDITION_LABELS: Record<string, string> = {
  pvpUserRanking: 'PVP Ranking',
  pvpAvailable: 'PVP Data Available',
  pvpGreat: 'Great League',
  pvpUltra: 'Ultra League',
  pvpLittle: 'Little League',
  pvpGreatBest: 'Great League Best',
  pvpUltraBest: 'Ultra League Best',
  pvpLittleBest: 'Little League Best',
  confirmedTime: 'Confirmed Despawn',
  boostWeatherEmoji: 'Weather Boosted',
  weatherChange: 'Weather Changing',
  formNormalisedEng: 'Has Form Name',
  disguisePokemonName: 'Disguise (Zorua)',
  ex: 'EX Raid Eligible',
  futureEvent: 'Upcoming Event',
};

@Component({
  selector: 'app-template-selector',
  standalone: true,
  imports: [
    MatFormFieldModule, MatSelectModule, FormsModule, MatIconModule,
    MatSlideToggleModule, MatChipsModule, MatButtonModule,
  ],
  template: `
    <mat-form-field appearance="outline" class="full-width">
      <mat-label>Template</mat-label>
      <mat-select [(ngModel)]="value" (ngModelChange)="onChange($event)">
        <mat-option value="">Default</mat-option>
        @for (t of templates(); track t) {
          <mat-option [value]="t.toString()">
            <mat-icon>description</mat-icon>
            Template {{ t }}
          </mat-option>
        }
      </mat-select>
      <mat-hint>Notification message format</mat-hint>
    </mat-form-field>

    @if (currentEmbed()) {
      <div class="preview-wrapper">
        <!-- Condition Toggles (collapsible) -->
        @if (conditions().length > 0) {
          <button mat-button class="scenario-btn" (click)="showToggles.set(!showToggles())">
            <mat-icon>{{ showToggles() ? 'expand_less' : 'tune' }}</mat-icon>
            {{ showToggles() ? 'Hide Options' : 'Preview Options' }}
            <span class="active-count">{{ activeConditionCount() }}/{{ conditions().length }}</span>
          </button>
          @if (showToggles()) {
            <div class="toggles-grid">
              @for (cond of conditions(); track cond.name) {
                <label class="toggle-chip" [class.active]="cond.enabled" (click)="toggleCondition(cond.name, !cond.enabled)">
                  <mat-icon>{{ cond.enabled ? 'check_circle' : 'radio_button_unchecked' }}</mat-icon>
                  {{ cond.label }}
                </label>
              }
            </div>
          }
        }

        <!-- Discord Embed Preview (always visible) -->
        <div class="discord-embed" [style.border-left-color]="previewColor">
          @if (renderedEmbed(); as embed) {
            <div class="embed-body">
              <div class="embed-content-row">
                <div class="embed-content-main">
                  @if (embed.title) {
                    <div class="embed-title" [innerHTML]="md(embed.title)"></div>
                  }
                  @if (embed.description) {
                    <div class="embed-description" [innerHTML]="md(embed.description)"></div>
                  }
                </div>
                @if (hasThumbnail()) {
                  <img src="https://raw.githubusercontent.com/whitewillem/PogoAssets/main/uicons/pokemon/25.png"
                       class="embed-thumbnail" alt="Pokemon" />
                }
              </div>
              @if (embed.fields?.length) {
                <div class="embed-fields">
                  @for (field of embed.fields; track $index) {
                    @if (fieldHasContent(field)) {
                      <div class="embed-field" [class.inline]="field.inline">
                        <div class="field-name" [innerHTML]="md(field.name)"></div>
                        <div class="field-value" [innerHTML]="md(field.value)"></div>
                      </div>
                    }
                  }
                </div>
              }
              @if (hasImage()) {
                <img src="https://tiles.pgan.me/staticmap/pregenerated/rwk+Eg_eNoFHlFh0KB6SNn_vuZj48+O2WI9GD87CHKA=.png"
                     class="embed-image" alt="Map" loading="lazy" />
              }
              @if (embed.footer) {
                <div class="embed-footer">
                  @if (hasFooterIcon()) {
                    <img [src]="footerIcon()" class="footer-icon" alt="" />
                  }
                  {{ embed.footer.text }}
                </div>
              }
            </div>
          }
        </div>
      </div>
    }
  `,
  styles: [`
    .full-width { width: 100%; }

    .preview-wrapper {
      margin: 8px 0 12px;
    }

    /* Scenario Button */
    .scenario-btn {
      display: flex;
      align-items: center;
      gap: 6px;
      font-size: 12px;
      color: #72767d;
      margin-bottom: 6px;
      padding: 4px 8px;
      min-height: 0;
      line-height: 1;
    }
    .scenario-btn mat-icon { font-size: 16px; width: 16px; height: 16px; }
    .active-count {
      font-size: 11px;
      background: #40444b;
      color: #b9bbbe;
      padding: 1px 6px;
      border-radius: 8px;
      margin-left: 4px;
    }
    .toggles-grid {
      display: flex; flex-wrap: wrap; gap: 6px;
      margin-bottom: 10px;
      padding: 8px;
      background: #36393f;
      border-radius: 6px;
    }
    .toggle-chip {
      display: inline-flex;
      align-items: center;
      gap: 4px;
      padding: 4px 10px;
      border-radius: 16px;
      font-size: 12px;
      cursor: pointer;
      user-select: none;
      background: #40444b;
      color: #8e9297;
      border: 1px solid transparent;
      transition: all 0.15s;
    }
    .toggle-chip:hover {
      background: #4f545c;
      color: #dcddde;
    }
    .toggle-chip.active {
      background: rgba(87, 242, 135, 0.12);
      color: #57f287;
      border-color: rgba(87, 242, 135, 0.3);
    }
    .toggle-chip mat-icon {
      font-size: 14px; width: 14px; height: 14px;
    }

    /* Discord Embed */
    .discord-embed {
      border-left: 4px solid #4caf50;
      border-radius: 4px;
      background: #2f3136;
      color: #dcddde;
      overflow: hidden;
      font-family: 'Segoe UI', system-ui, -apple-system, sans-serif;
    }
    .embed-body { padding: 10px 12px 12px; }
    .embed-content-row { display: flex; gap: 16px; }
    .embed-content-main { flex: 1; min-width: 0; }
    .embed-thumbnail {
      width: 80px; height: 80px; object-fit: contain;
      border-radius: 4px; flex-shrink: 0;
    }
    .embed-image {
      width: 100%; max-height: 200px; object-fit: cover;
      border-radius: 4px; margin-top: 10px;
    }
    .footer-icon {
      width: 16px; height: 16px; border-radius: 50%;
      margin-right: 6px; vertical-align: middle;
    }
    .embed-title {
      font-size: 14px; font-weight: 600;
      color: #00aff4; margin-bottom: 6px; line-height: 1.3;
    }
    .embed-description {
      font-size: 13px; color: #dcddde;
      line-height: 1.5; margin-bottom: 8px;
    }
    .embed-fields {
      display: flex; flex-wrap: wrap; gap: 4px 16px; margin-top: 8px;
    }
    .embed-field { flex: 0 0 100%; margin-bottom: 6px; }
    .embed-field.inline { flex: 1 1 30%; min-width: 100px; }
    .field-name { font-size: 12px; font-weight: 600; color: #dcddde; margin-bottom: 2px; }
    .field-value { font-size: 12px; color: #a3a6aa; line-height: 1.4; }
    .embed-footer {
      font-size: 11px; color: #72767d;
      margin-top: 8px; padding-top: 8px;
      border-top: 1px solid #40444b;
    }

    /* Markdown rendering */
    :host ::ng-deep .embed-link { color: #00aff4; text-decoration: none; }
    :host ::ng-deep .embed-link:hover { text-decoration: underline; }
    :host ::ng-deep .embed-body code {
      background: #202225; padding: 1px 4px; border-radius: 3px;
      font-size: 12px; font-family: 'Consolas', monospace;
    }
    :host ::ng-deep .embed-body strong { color: #fff; }
    :host ::ng-deep .embed-body em { font-style: italic; }
    :host ::ng-deep .embed-body u { text-decoration: underline; }
    :host ::ng-deep .embed-body s { text-decoration: line-through; color: #72767d; }
  `],
})
export class TemplateSelectorComponent implements OnInit {
  @Input() alarmType = 'monster';
  @Input() value = '';
  @Output() valueChange = new EventEmitter<string>();

  templates = signal<(string | number)[]>([]);
  dtsEntries = signal<DtsRaw[]>([]);
  conditions = signal<ConditionToggle[]>([]);
  conditionState = signal<Record<string, boolean>>({});
  showToggles = signal(false);
  activeConditionCount = computed(() => this.conditions().filter(c => c.enabled).length);

  private templateService = inject(TemplateService);
  private http = inject(HttpClient);
  private config = inject(ConfigService);
  private auth = inject(AuthService);

  private static dtsCache: DtsRaw[] | null = null;

  previewColor = '#4caf50';

  currentEmbed = computed(() => {
    const entries = this.dtsEntries();
    const platform = this.auth.user()?.type?.startsWith('telegram') ? 'telegram' : 'discord';
    const id = parseInt(this.value || '1', 10) || 1;
    const match = entries.find(e =>
      e.type === this.alarmType && e.platform === platform && e.id === id
    ) || entries.find(e =>
      e.type === this.alarmType && e.platform === platform
    );
    return match?.template?.embed || null;
  });

  renderedEmbed = computed(() => {
    const embed = this.currentEmbed();
    if (!embed) return null;
    const state = this.conditionState();

    return {
      title: embed.title ? this.evalHandlebars(this.sub(embed.title), state) : '',
      description: embed.description ? this.evalHandlebars(this.sub(embed.description), state) : '',
      fields: embed.fields?.map(f => ({
        name: this.evalHandlebars(this.sub(f.name), state),
        value: this.evalHandlebars(this.sub(f.value), state),
        inline: f.inline,
      })),
      footer: embed.footer ? { text: this.sub(embed.footer.text) } : undefined,
    };
  });

  hasThumbnail = computed(() => !!this.currentEmbed()?.thumbnail?.url);
  hasImage = computed(() => !!this.currentEmbed()?.image?.url);
  hasFooterIcon = computed(() => !!this.currentEmbed()?.footer?.icon_url);
  footerIcon = computed(() => this.currentEmbed()?.footer?.icon_url || '');

  constructor() {
    // When the embed changes, extract conditions
    effect(() => {
      const embed = this.currentEmbed();
      if (!embed) { this.conditions.set([]); return; }

      const raw = JSON.stringify(embed);
      const condNames = new Set<string>();

      // Extract {{#if condName}} patterns
      const ifRegex = /\{\{#if\s+([a-zA-Z_]\w*(?:\.\w+)?)\}\}/g;
      let m;
      while ((m = ifRegex.exec(raw)) !== null) {
        condNames.add(m[1]);
      }

      // Build toggle list with friendly labels
      const toggles: ConditionToggle[] = [];
      const defaults: Record<string, boolean> = {};

      for (const name of condNames) {
        // Skip internal/nested conditions
        if (name.includes('.')) continue;
        const label = CONDITION_LABELS[name] || name.replace(/([A-Z])/g, ' $1').trim();
        const defaultOn = ['confirmedTime', 'boostWeatherEmoji', 'formNormalisedEng'].includes(name);
        toggles.push({ name, label, enabled: defaultOn });
        defaults[name] = defaultOn;
      }

      toggles.sort((a, b) => a.label.localeCompare(b.label));
      this.conditions.set(toggles);
      this.conditionState.set(defaults);
    });
  }

  ngOnInit(): void {
    const colors: Record<string, string> = {
      monster: '#4caf50', raid: '#f44336', egg: '#ff9800', quest: '#9c27b0',
      invasion: '#607d8b', lure: '#00bcd4', nest: '#8bc34a', gym: '#00bcd4',
    };
    this.previewColor = colors[this.alarmType] || '#4caf50';

    this.templateService.getTemplatesForType(this.alarmType).subscribe(t => {
      this.templates.set(t);
      if (!this.value && t.length > 0) {
        this.value = t[0].toString();
        this.valueChange.emit(this.value);
      }
    });

    this.loadDts();
  }

  onChange(val: string): void {
    this.valueChange.emit(val);
  }

  toggleCondition(name: string, enabled: boolean): void {
    this.conditionState.update(s => ({ ...s, [name]: enabled }));
    this.conditions.update(list =>
      list.map(c => c.name === name ? { ...c, enabled } : c)
    );
  }

  fieldHasContent(field: { name: string; value: string }): boolean {
    const name = field.name.replace(/<[^>]*>/g, '').trim();
    const value = field.value.replace(/<[^>]*>/g, '').trim();
    // Hide empty/zero-width fields (​ is zero-width space)
    return (name.length > 0 && name !== '​') || (value.length > 0 && value !== '​');
  }

  /** Evaluate {{#if cond}}...{{else}}...{{/if}} blocks based on toggle state */
  evalHandlebars(text: string, state: Record<string, boolean>): string {
    if (!text) return '';

    let result = text;
    let safety = 20;

    while (safety-- > 0) {
      const ifMatch = result.match(/\{\{#if\s+(\w+)\}\}([\s\S]*?)\{\{\/if\}\}/);
      if (!ifMatch) break;

      const [fullMatch, condName, inner] = ifMatch;
      const condValue = state[condName] ?? false;

      // Split on {{else}}
      const elseParts = inner.split(/\{\{else\}\}/);
      const trueBranch = elseParts[0] || '';
      const falseBranch = elseParts[1] || '';

      result = result.replace(fullMatch, condValue ? trueBranch : falseBranch);
    }

    // Clean up remaining blocks
    result = result
      .replace(/\{\{#unless\s+\w+\}\}[\s\S]*?\{\{\/unless\}\}/g, '')
      .replace(/\{\{#each\s+\w+\}\}[\s\S]*?\{\{\/each\}\}/g, '')
      .replace(/\{\{#compare[\s\S]*?\{\{\/compare\}\}/g, '')
      .replace(/\{\{#getPowerUpCost[\s\S]*?\{\{\/getPowerUpCost\}\}/g, '')
      .replace(/\{\{#eq[\s\S]*?\{\{\/eq\}\}/g, '')
      .replace(/\{\{\{?[^}]+\}\}\}?/g, '')
      .replace(/<:[^:]+:\d+>/g, '')
      .replace(/\n{3,}/g, '\n')
      .replace(/^\s*\n/gm, '')
      .trim();

    return result;
  }

  sub(text: string): string {
    if (!text) return '';
    return text
      .replace(/\{\{\{?areas\}\}\}?/g, 'West End')
      .replace(/\{\{\{?boostWeatherEmoji\}\}\}?/g, '☀️')
      .replace(/\{\{\{?genderEmoji\}\}\}?/g, '♂')
      .replace(/\{\{\{?genderData\.emoji\}\}\}?/g, '♂')
      .replace(/\{\{round iv\}\}/g, '98')
      .replace(/\{\{iv\}\}/g, '98')
      .replace(/\{\{fullName\}\}/g, 'Pikachu')
      .replace(/\{\{name\}\}/g, 'Pikachu')
      .replace(/\{\{formName\}\}/g, 'Normal')
      .replace(/\{\{cp\}\}/g, '3200')
      .replace(/\{\{level\}\}/g, '35')
      .replace(/\{\{atk\}\}/g, '15')
      .replace(/\{\{def\}\}/g, '14')
      .replace(/\{\{sta\}\}/g, '15')
      .replace(/\{\{time\}\}/g, '3:45 PM')
      .replace(/\{\{tthm\}\}/g, '12')
      .replace(/\{\{tths\}\}/g, '30')
      .replace(/\{\{\{?countdown\}\}\}?/g, '12m 30s')
      .replace(/\{\{quickMoveName\}\}/g, 'Dragon Tail')
      .replace(/\{\{chargeMoveName\}\}/g, 'Outrage')
      .replace(/\{\{\{?addr\}\}\}?/g, '123 Main St')
      .replace(/\{\{\{?googleMapUrl\}\}\}?/g, '#')
      .replace(/\{\{\{?appleMapUrl\}\}\}?/g, '#')
      .replace(/\{\{\{?wazeMapUrl\}\}\}?/g, '#')
      .replace(/\{\{\{?gymName\}\}\}?/g, 'Central Park Gym')
      .replace(/\{\{\{?pokestopName\}\}\}?/g, 'Library Pokestop')
      .replace(/\{\{pokemonName\}\}/g, 'Mewtwo')
      .replace(/\{\{\{?rewardString\}\}\}?/g, 'Chansey encounter')
      .replace(/\{\{gruntType\}\}/g, 'Water')
      .replace(/\{\{nestName\}\}/g, 'River Walk Park')
      .replace(/\{\{lureTypeName\}\}/g, 'Glacial')
      .replace(/\{\{levelName\}\}/g, 'Tier 5')
      .replace(/\{\{previousControlName\}\}/g, 'Valor')
      .replace(/\{\{changeTypeText\}\}/g, 'Changed')
      .replace(/\{\{fortTypeText\}\}/g, 'Gym')
      .replace(/\{\{pokemon_id\}\}/g, '25')
      .replace(/\{\{encounterId\}\}/g, '12345')
      .replace(/\{\{percentage\}\}/g, '99.8')
      .replace(/\{\{rank\}\}/g, '3')
      .replace(/\{\{stardust\}\}/g, '75,000')
      .replace(/\{\{candy\}\}/g, '66')
      .replace(/\{\{xlCandy\}\}/g, '0')
      .replace(/\{\{disguisePokemonName\}\}/g, 'Poochyena')
      .replace(/\{\{disguiseFormName\}\}/g, 'Normal')
      .replace(/\{\{\{?pvpGreatBest\.name\}\}\}?/g, 'Dragonite')
      .replace(/\{\{pvpGreatBest\.rank\}\}/g, '3')
      .replace(/\{\{\{?pvpUltraBest\.name\}\}\}?/g, 'Dragonite')
      .replace(/\{\{pvpUltraBest\.rank\}\}/g, '8')
      .replace(/\{\{\{?pvpLittleBest\.name\}\}\}?/g, 'Dragonair')
      .replace(/\{\{pvpLittleBest\.rank\}\}/g, '12')
      .replace(/\{\{\{?futureEventName\}\}\}?/g, 'Community Day')
      .replace(/\{\{futureEventTime\}\}/g, '2:00 PM')
      .replace(/\{\{\{?weatherChange\}\}\}?/g, '⚠️ Weather changing soon');
  }

  md(text: string): string {
    if (!text) return '';
    return text
      .replace(/\*\*\*(.+?)\*\*\*/g, '<strong><em>$1</em></strong>')
      .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
      .replace(/\*(.+?)\*/g, '<em>$1</em>')
      .replace(/__(.+?)__/g, '<u>$1</u>')
      .replace(/~~(.+?)~~/g, '<s>$1</s>')
      .replace(/`([^`]+)`/g, '<code>$1</code>')
      .replace(/\[([^\]]+)\]\(([^)]+)\)/g, '<a href="$2" class="embed-link">$1</a>')
      .replace(/\n/g, '<br>');
  }

  private loadDts(): void {
    if (TemplateSelectorComponent.dtsCache) {
      this.dtsEntries.set(TemplateSelectorComponent.dtsCache);
      return;
    }

    this.http.get<DtsRaw[]>(`${this.config.apiHost}/api/config/dts`).subscribe({
      next: (entries) => {
        TemplateSelectorComponent.dtsCache = entries;
        this.dtsEntries.set(entries);
      },
      error: () => {},
    });
  }
}
