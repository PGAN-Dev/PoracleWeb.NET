import { Injectable, computed, effect, inject, signal } from '@angular/core';
import * as L from 'leaflet';

import { I18nService } from './i18n.service';
import { SettingsService } from './settings.service';
import {
  BUILTIN_BASEMAPS,
  BasemapDefinition,
  CUSTOM_BASEMAP_ID,
  DEFAULT_CUSTOM_BASEMAP_LABEL,
  FALLBACK_BASEMAP_ID,
  MAX_BASEMAP_NAME_LENGTH,
  applyBasemapKey,
  findBasemap,
  isTileTemplate,
  resolveBasemapProviderId,
} from '../../shared/utils/basemaps';

/** Where a viewer's own basemap choice is kept, alongside the theme/accent/language preferences. */
const STORAGE_KEY = 'poracle-basemap';

export interface BasemapAttachOptions {
  /**
   * Clamp below the provider's own maximum. Only the overview thumbnail needs this; it has always
   * capped at 18 and there is no reason to let a provider that serves 20 pull it further in.
   */
  maxZoom?: number;

  /** Draw the basemap picker on this map. Off for thumbnails, which are not interactive at all. */
  picker?: boolean;

  /** Corner for the picker. Top-left is where Leaflet's zoom and draw controls already live. */
  position?: L.ControlPosition;
}

interface Attachment {
  /** Id of the definition the layer was built from, so a theme change knows it can reuse it. */
  definitionId: string;
  layer: L.TileLayer | null;
  map: L.Map;
  options: BasemapAttachOptions;
  picker: HTMLElement | null;
  /** What the picker's markup was built from, so an unchanged list is not rebuilt under the cursor. */
  pickerSignature: string;
}

/** What the picker renders from. Passed as one object because it is all values and no behaviour. */
interface PickerState {
  active: BasemapDefinition;
  fallback: 'missing-key' | 'unavailable' | null;
  lang: string;
  options: BasemapDefinition[];
  /** The viewer's own choice, empty when they are on the site default. Marks the active entry. */
  selected: string;
  siteDefault: BasemapDefinition;
}

/**
 * The one place a map tile layer is built.
 *
 * Every map on the site once carried its own copy of a CARTO URL, which is how five of them ended up
 * requesting a keyless endpoint CARTO now watermarks -- silently, over a 200, with the words drawn
 * into the image. See #842 and {@link BasemapService.fallbackReason}.
 *
 * Attached maps track three things afterwards: the admin's configured provider, the viewer's own
 * choice, and the light/dark theme. All three can change while a map is on screen, so `attach`
 * registers the map rather than handing back a layer and forgetting about it.
 */
@Injectable({ providedIn: 'root' })
export class BasemapService {
  private readonly attachments = new Set<Attachment>();

  private readonly settings = inject(SettingsService);

  /**
   * The provider an admin nominated, before asking whether it can actually be drawn. Kept apart from
   * {@link active} so {@link fallbackReason} reports on what was asked for rather than on what it fell
   * back to. Resolved by the same helper the admin page uses to decide which fields to show, so the
   * form and the map cannot disagree about which provider is selected.
   */
  private readonly configuredId = computed(() => {
    const settings = this.settings.siteSettings();
    return resolveBasemapProviderId(settings['basemap_provider'] || '', settings['basemap_url'] || '');
  });

  /** Trimmed, because what an admin pastes into a settings field usually arrives with whitespace. */
  private readonly configuredKey = computed(() => (this.settings.siteSettings()['basemap_key'] || '').trim());

  /** The entry assembled from `basemap_url`, or null when no usable custom URL is configured. */
  private readonly customDefinition = computed<BasemapDefinition | null>(() => {
    const settings = this.settings.siteSettings();
    const url = (settings['basemap_url'] || '').trim();
    // A template that is not an absolute http(s) URL cannot be a tile source. Refusing it here means a
    // mistyped setting falls back to a working basemap instead of drawing a grid of broken images.
    if (!isTileTemplate(url)) return null;

    const dark = (settings['basemap_url_dark'] || '').trim();
    const attribution = (settings['basemap_attribution'] || '').trim();
    const name = (settings['basemap_name'] || '').trim().slice(0, MAX_BASEMAP_NAME_LENGTH);

    return {
      id: CUSTOM_BASEMAP_ID,
      // Escaped: Leaflet assigns attribution as innerHTML, and this is the one attribution string that
      // comes from a settings field rather than from the catalogue. The text renders; a link would not.
      attribution: attribution ? escapeHtml(attribution) : DEFAULT_CUSTOM_BASEMAP_LABEL,
      darkUrl: isTileTemplate(dark) ? dark : undefined,
      keyFamily: url.includes('{key}') ? CUSTOM_BASEMAP_ID : undefined,
      // The menu says "Custom" until someone names it, which tells a viewer nothing about the map
      // they are being offered. Set through to a display name, rendered as text and never as markup.
      label: name || DEFAULT_CUSTOM_BASEMAP_LABEL,
      maxZoom: 19,
      url,
    };
  });

  private readonly darkTheme = signal(isDarkTheme());

  private readonly i18n = inject(I18nService);

  /**
   * The basemaps a viewer may pick between.
   *
   * Keyless providers are always here, because they are what makes the missing-key fallback possible.
   * Keyed ones appear only once a key is configured, and only for the family the admin nominated: one
   * `basemap_key` cannot be a CARTO key and a Stadia key at the same time, so offering both would put
   * a guaranteed-broken option in the list.
   */
  readonly available = computed<BasemapDefinition[]>(() => {
    const custom = this.customDefinition();
    const configured = this.configuredId();
    const hasKey = this.configuredKey() !== '';
    const family = configured === CUSTOM_BASEMAP_ID ? CUSTOM_BASEMAP_ID : findBasemap(configured)?.keyFamily;

    const usable = BUILTIN_BASEMAPS.filter(b => !b.keyFamily || (hasKey && b.keyFamily === family));
    return custom && (!custom.keyFamily || hasKey) ? [custom, ...usable] : [...usable];
  });

  /** The viewer's own choice. Empty means "whatever the admin configured". */
  readonly selectedId = signal(localStorage.getItem(STORAGE_KEY) ?? '');

  /**
   * What a viewer who has made no choice of their own sees: the admin's provider if it can be drawn,
   * and a keyless basemap if it cannot.
   *
   * That second step is the point. A keyed provider with no key still returns tiles, so drawing it
   * anyway produces a watermark nobody is told about. See {@link FALLBACK_BASEMAP_ID}.
   */
  readonly siteDefault = computed<BasemapDefinition>(() => {
    const usable = this.available();
    return (
      usable.find(b => b.id === this.configuredId()) ?? usable.find(b => b.id === FALLBACK_BASEMAP_ID) ?? findBasemap(FALLBACK_BASEMAP_ID)!
    );
  });

  /** The definition every attached map is currently drawing: the viewer's choice, or the site's. */
  readonly active = computed<BasemapDefinition>(() => {
    return this.available().find(b => b.id === this.selectedId()) ?? this.siteDefault();
  });

  /**
   * The basemap the admin selected, whether or not it can be drawn.
   *
   * Distinct from {@link active}, which is what a viewer is actually looking at. The admin page names
   * this one, because "no provider chosen, so maps use X" is a statement about the configuration.
   */
  readonly configured = computed<BasemapDefinition | null>(() => {
    const id = this.configuredId();
    return id === CUSTOM_BASEMAP_ID ? this.customDefinition() : findBasemap(id);
  });

  /**
   * Why {@link active} is not the basemap the admin configured, or null when it is.
   *
   * Every fallback here is silent by nature: the map renders, it just renders the wrong thing. Naming
   * the reason is the only thing that will ever report it, since the failure produces no error --
   * CARTO answers 200 without a key, and a URL that is not a tile template is simply never requested.
   *
   * `missing-key` is deliberately narrower than "no key is set": an operator who chose a keyless
   * provider is not warned about a key their basemap never asked for.
   */
  readonly fallbackReason = computed<'missing-key' | 'unavailable' | null>(() => {
    const configured = this.configuredId();
    const definition = configured === CUSTOM_BASEMAP_ID ? this.customDefinition() : findBasemap(configured);

    if (!!definition?.keyFamily && this.configuredKey() === '') return 'missing-key';
    // Covers a Custom provider whose tile URL is blank or not an http(s) template -- which is what
    // choosing Custom and saving before filling the field leaves behind -- and a provider id this
    // build does not know, which a rollback can produce.
    return this.available().some(b => b.id === configured) ? null : 'unavailable';
  });

  constructor() {
    // app.ts owns the theme and expresses it as a body class; watching the class rather than keeping a
    // second copy of the preference means anything that toggles it moves the maps with it.
    new MutationObserver(() => this.darkTheme.set(isDarkTheme())).observe(document.body, { attributeFilter: ['class'] });

    effect(() => {
      // Read up front so the effect depends on all of them even when nothing is attached yet.
      const definition = this.active();
      const url = this.tileUrl();
      const state: PickerState = {
        active: definition,
        fallback: this.fallbackReason(),
        lang: this.i18n.currentLang(),
        options: this.available(),
        selected: this.selectedId(),
        siteDefault: this.siteDefault(),
      };

      for (const attachment of this.attachments) {
        this.draw(attachment, definition, url);
        this.renderPicker(attachment, state);
      }
    });
  }

  /**
   * Adds the current basemap to a map and keeps it current.
   *
   * Nothing to unwind afterwards: cleanup hangs off Leaflet's own `unload`, which `map.remove()`
   * fires. A disposer would have to be held and called, and the geofence submissions grid tears its
   * thumbnails down from four places, none of them a component lifecycle hook.
   */
  attach(map: L.Map, options: BasemapAttachOptions = {}): void {
    const attachment: Attachment = { definitionId: '', layer: null, map, options, picker: null, pickerSignature: '' };
    this.attachments.add(attachment);

    map.on('unload', () => {
      this.attachments.delete(attachment);
      attachment.picker = null;
      attachment.layer = null;
    });

    this.draw(attachment, this.active(), this.tileUrl());
    if (options.picker) {
      this.addPicker(attachment);
    }
  }

  /**
   * A tile layer for the active basemap, unattached.
   *
   * Exposed for tests and for anything that wants a layer without the tracking. A layer made this way
   * keeps the URL it was born with, so it will not follow a theme change.
   */
  createLayer(options: { maxZoom?: number } = {}): L.TileLayer {
    const definition = this.active();
    return L.tileLayer(this.tileUrl(), {
      attribution: definition.attribution,
      maxZoom: Math.min(definition.maxZoom, options.maxZoom ?? definition.maxZoom),
      referrerPolicy: definition.sendReferrer ? 'origin' : undefined,
      subdomains: definition.subdomains ?? 'abc',
    });
  }

  /** Records a viewer's choice. An id that is not on offer resets them to the site default. */
  select(id: string): void {
    const next = this.available().some(b => b.id === id) ? id : '';
    this.selectedId.set(next);
    localStorage.setItem(STORAGE_KEY, next);
  }

  /** The active basemap's tile URL for the current theme, with the key substituted in. */
  tileUrl(): string {
    const definition = this.active();
    const template = (this.darkTheme() && definition.darkUrl) || definition.url;
    return applyBasemapKey(template, this.configuredKey());
  }

  private addPicker(attachment: Attachment): void {
    const control = new L.Control({ position: attachment.options.position ?? 'topright' });
    control.onAdd = () => {
      const container = L.DomUtil.create('div', 'basemap-control leaflet-bar');
      // Without these a click on the menu pans the map underneath and a scroll over it zooms.
      L.DomEvent.disableClickPropagation(container);
      L.DomEvent.disableScrollPropagation(container);
      attachment.picker = container;
      this.renderPicker(attachment, {
        active: this.active(),
        fallback: this.fallbackReason(),
        lang: this.i18n.currentLang(),
        options: this.available(),
        selected: this.selectedId(),
        siteDefault: this.siteDefault(),
      });
      return container;
    };
    control.addTo(attachment.map);
  }

  private buildPicker(container: HTMLElement, state: PickerState): void {
    const wasOpen = container.querySelector('.basemap-control__menu')?.hasAttribute('data-open') ?? false;
    const title = this.i18n.instant('BASEMAP.TITLE');

    container.replaceChildren();

    const toggle = L.DomUtil.create('button', 'basemap-control__toggle', container);
    toggle.type = 'button';
    toggle.title = title;
    toggle.setAttribute('aria-label', title);
    toggle.setAttribute('aria-expanded', String(wasOpen));
    const icon = L.DomUtil.create('span', 'material-icons', toggle);
    icon.textContent = 'layers';
    icon.setAttribute('aria-hidden', 'true');

    const menu = L.DomUtil.create('div', 'basemap-control__menu', container);
    menu.hidden = !wasOpen;
    if (wasOpen) menu.setAttribute('data-open', '');
    toggle.addEventListener('click', () => {
      const open = menu.hidden;
      menu.hidden = !open;
      toggle.setAttribute('aria-expanded', String(open));
      if (open) menu.setAttribute('data-open', '');
      else menu.removeAttribute('data-open');
    });

    const heading = L.DomUtil.create('p', 'basemap-control__title', menu);
    heading.textContent = title;

    // Without an entry for it, a viewer who once touched this menu could never get back to whatever
    // the admin configures afterwards -- the admin included, which is how an admin concludes that
    // saving the setting does nothing.
    const entries: { id: string; label: string }[] = [
      { id: '', label: this.i18n.instant('BASEMAP.SITE_DEFAULT', { provider: state.siteDefault.label }) },
      ...state.options.map(option => ({ id: option.id, label: option.label })),
    ];

    for (const entry of entries) {
      const button = L.DomUtil.create('button', 'basemap-control__option', menu);
      button.type = 'button';
      button.dataset['basemap'] = entry.id;
      button.textContent = entry.label;
      button.addEventListener('click', () => this.select(entry.id));
    }

    if (state.fallback) {
      const warning = L.DomUtil.create('p', 'basemap-control__warning', menu);
      const key = state.fallback === 'missing-key' ? 'BASEMAP.KEY_MISSING' : 'BASEMAP.UNAVAILABLE';
      warning.textContent = this.i18n.instant(key, { fallback: state.active.label });
    }
  }

  /**
   * Puts the right tiles on a map.
   *
   * A theme change within one provider only swaps the URL, because `setUrl` keeps the tiles already
   * drawn until their replacements arrive. A provider change has to rebuild the layer -- attribution,
   * subdomains and maxZoom are fixed at construction -- and the new layer goes on before the old one
   * comes off so the map never flashes empty.
   */
  private draw(attachment: Attachment, definition: BasemapDefinition, url: string): void {
    if (attachment.layer && attachment.definitionId === definition.id) {
      attachment.layer.setUrl(url);
      return;
    }

    const previous = attachment.layer;
    const layer = L.tileLayer(url, {
      attribution: definition.attribution,
      maxZoom: Math.min(definition.maxZoom, attachment.options.maxZoom ?? definition.maxZoom),
      // Set on the tile images themselves, which overrides the document's Referrer-Policy for these
      // requests and nothing else. 'origin' sends the site's host and no path.
      referrerPolicy: definition.sendReferrer ? 'origin' : undefined,
      subdomains: definition.subdomains ?? 'abc',
    });
    layer.addTo(attachment.map);
    previous?.remove();

    attachment.layer = layer;
    attachment.definitionId = definition.id;
  }

  /**
   * Keeps the picker in step with what is on offer.
   *
   * The markup is rebuilt only when the list or the language changes, and the active flag is moved in
   * place otherwise. Rebuilding on every selection would replace the button under the pointer that
   * had just been used to make the selection, taking keyboard focus with it.
   */
  private renderPicker(attachment: Attachment, state: PickerState): void {
    const container = attachment.picker;
    if (!container) return;

    const signature = [state.lang, state.fallback, state.siteDefault.label, ...state.options.map(o => o.id)].join('|');
    if (signature !== attachment.pickerSignature) {
      this.buildPicker(container, state);
      attachment.pickerSignature = signature;
    }

    for (const button of container.querySelectorAll<HTMLButtonElement>('.basemap-control__option')) {
      // Matched against the viewer's own choice, not against what is drawn, so the site-default entry
      // reads as active exactly when they have not overridden it -- and their override is visible as
      // an override rather than looking like the site's own setting.
      const isActive = (button.dataset['basemap'] ?? '') === state.selected;
      button.setAttribute('aria-pressed', String(isActive));
      button.classList.toggle('is-active', isActive);
    }
  }
}

/** Escapes a string for a context that will be assigned as innerHTML. */
function escapeHtml(value: string): string {
  const element = document.createElement('span');
  element.textContent = value;
  return element.innerHTML;
}

function isDarkTheme(): boolean {
  return document.body.classList.contains('dark-theme');
}
