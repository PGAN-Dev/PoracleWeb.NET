import { HttpClient } from '@angular/common/http';
import { Component, EventEmitter, Input, OnInit, Output, inject, signal, computed, effect } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MatButtonModule } from '@angular/material/button';
import { MatChipsModule } from '@angular/material/chips';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatIconModule } from '@angular/material/icon';
import { MatSelectModule } from '@angular/material/select';
import { MatSlideToggleModule } from '@angular/material/slide-toggle';

import { AuthService } from '../../../core/services/auth.service';
import { ConfigService } from '../../../core/services/config.service';
import { IconService } from '../../../core/services/icon.service';
import { SettingsService } from '../../../core/services/settings.service';
import { TemplateService } from '../../../core/services/template.service';

interface DtsRaw {
  id: number;
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
  type: string;
}

interface ConditionToggle {
  enabled: boolean;
  label: string;
  name: string;
}

// Friendly labels for Handlebars conditions
const CONDITION_LABELS: Record<string, string> = {
  boostWeatherEmoji: 'Weather Boosted',
  confirmedTime: 'Confirmed Despawn',
  disguisePokemonName: 'Disguise (Zorua)',
  ex: 'EX Raid Eligible',
  formNormalisedEng: 'Has Form Name',
  futureEvent: 'Upcoming Event',
  pvpAvailable: 'PVP Data Available',
  pvpGreat: 'Great League',
  pvpGreatBest: 'Great League Best',
  pvpLittle: 'Little League',
  pvpLittleBest: 'Little League Best',
  pvpUltra: 'Ultra League',
  pvpUltraBest: 'Ultra League Best',
  pvpUserRanking: 'PVP Ranking',
  weatherChange: 'Weather Changing',
};

@Component({
  imports: [MatFormFieldModule, MatSelectModule, FormsModule, MatIconModule, MatSlideToggleModule, MatChipsModule, MatButtonModule],
  selector: 'app-template-selector',
  standalone: true,
  styleUrl: './template-selector.component.scss',
  templateUrl: './template-selector.component.html',
})
export class TemplateSelectorComponent implements OnInit {
  private static dtsCache: DtsRaw[] | null = null;
  private auth = inject(AuthService);
  private config = inject(ConfigService);

  private http = inject(HttpClient);
  private settings = inject(SettingsService);
  private templateService = inject(TemplateService);
  conditions = signal<ConditionToggle[]>([]);
  activeConditionCount = computed(() => this.conditions().filter(c => c.enabled).length);
  @Input() alarmType = 'monster';

  conditionState = signal<Record<string, boolean>>({});
  dtsEntries = signal<DtsRaw[]>([]);
  @Input() value = '';
  currentEmbed = computed(() => {
    const entries = this.dtsEntries();
    const platform = this.auth.user()?.type?.startsWith('telegram') ? 'telegram' : 'discord';
    const id = parseInt(this.value || '1', 10) || 1;
    const match =
      entries.find(e => e.type === this.alarmType && e.platform === platform && e.id === id) ||
      entries.find(e => e.type === this.alarmType && e.platform === platform);
    return match?.template?.embed || null;
  });

  footerIcon = computed(() => this.currentEmbed()?.footer?.icon_url || '');
  hasFooterIcon = computed(() => !!this.currentEmbed()?.footer?.icon_url);

  hasImage = computed(() => !!this.currentEmbed()?.image?.url);

  hasThumbnail = computed(() => !!this.currentEmbed()?.thumbnail?.url);

  readonly iconService = inject(IconService);

  previewColor = '#4caf50';

  renderedEmbed = computed(() => {
    const embed = this.currentEmbed();
    if (!embed) return null;
    const state = this.conditionState();

    return {
      description: embed.description ? this.evalHandlebars(this.sub(embed.description), state) : '',
      fields: embed.fields?.map(f => ({
        name: this.evalHandlebars(this.sub(f.name), state),
        inline: f.inline,
        value: this.evalHandlebars(this.sub(f.value), state),
      })),
      footer: embed.footer ? { text: this.sub(embed.footer.text) } : undefined,
      title: embed.title ? this.evalHandlebars(this.sub(embed.title), state) : '',
    };
  });

  showToggles = signal(false);
  templates = signal<(string | number)[]>([]);
  templatesEnabled = computed(() => this.settings.siteSettings()['enable_templates']?.toLowerCase() === 'true');
  @Output() valueChange = new EventEmitter<string>();

  constructor() {
    // When the embed changes, extract conditions
    effect(() => {
      const embed = this.currentEmbed();
      if (!embed) {
        this.conditions.set([]);
        return;
      }

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
        const defaultOn = ['boostWeatherEmoji', 'confirmedTime', 'formNormalisedEng'].includes(name);
        toggles.push({ name, enabled: defaultOn, label });
        defaults[name] = defaultOn;
      }

      toggles.sort((a, b) => a.label.localeCompare(b.label));
      this.conditions.set(toggles);
      this.conditionState.set(defaults);
    });
  }

  /** Evaluate {{#if cond}}...{{else}}...{{/if}} blocks based on toggle state */
  evalHandlebars(text: string, state: Record<string, boolean>): string {
    if (!text) return '';

    let result = text;
    let safety = 50;

    while (safety-- > 0) {
      // Find the innermost {{#if}} block (one with no nested {{#if}} inside)
      const ifMatch = result.match(/\{\{#if\s+(\w+)\}\}((?:(?!\{\{#if\s)[\s\S])*?)\{\{\/if\}\}/);
      if (!ifMatch) break;

      const [fullMatch, condName, inner] = ifMatch;
      const condValue = state[condName] ?? false;

      // Split on {{else}} — only top-level else (no nested blocks remain at this point)
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

  fieldHasContent(field: { name: string; value: string }): boolean {
    const name = field.name.replace(/<[^>]*>/g, '').trim();
    const value = field.value.replace(/<[^>]*>/g, '').trim();
    // Hide empty/zero-width fields (​ is zero-width space)
    return (name.length > 0 && name !== '​') || (value.length > 0 && value !== '​');
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

  ngOnInit(): void {
    const colors: Record<string, string> = {
      raid: '#f44336',
      egg: '#ff9800',
      gym: '#00bcd4',
      invasion: '#607d8b',
      lure: '#00bcd4',
      monster: '#4caf50',
      nest: '#8bc34a',
      quest: '#9c27b0',
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

  toggleCondition(name: string, enabled: boolean): void {
    this.conditionState.update(s => ({ ...s, [name]: enabled }));
    this.conditions.update(list => list.map(c => (c.name === name ? { ...c, enabled } : c)));
  }

  private loadDts(): void {
    if (TemplateSelectorComponent.dtsCache) {
      this.dtsEntries.set(TemplateSelectorComponent.dtsCache);
      return;
    }

    this.http.get<DtsRaw[]>(`${this.config.apiHost}/api/config/dts`).subscribe({
      error: () => {},
      next: entries => {
        TemplateSelectorComponent.dtsCache = entries;
        this.dtsEntries.set(entries);
      },
    });
  }
}
