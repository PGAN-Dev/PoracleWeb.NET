import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Observable, of } from 'rxjs';

import { AdminSettingsComponent, SETTING_GROUPS } from './admin-settings.component';
import { I18nService } from '../../core/services/i18n.service';
import { SettingsService } from '../../core/services/settings.service';

const meta = (key: string) => {
  const found = SETTING_GROUPS.flatMap(g => g.settings).find(s => s.key === key);
  if (!found) throw new Error(`no setting ${key}`);
  return found;
};

/**
 * The page has to show the state the server acts on. `AuthController` refuses a Discord or Telegram
 * sign-in only when the row says "false", so an install that never wrote the row has both on; the
 * switches read absent as off and told an admin both were disabled.
 */
describe('AdminSettingsComponent shows what the server enforces', () => {
  let update: jest.Mock;

  const create = (values: Record<string, string>) => {
    update = jest.fn().mockReturnValue(of({}));
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: {
            getOidcConfig: () => of(null),
            getAll: () => of(Object.entries(values).map(([key, value]) => ({ key, value }))),
            getDiscordConfig: () => of(null),
            getTelegramConfig: () => of(null),
            isForcedByPoracle: () => false,
            siteSettings: () => values,
            update: (...args: unknown[]) => update(...args) as Observable<unknown>,
          },
        },
      ],
      imports: [AdminSettingsComponent, NoopAnimationsModule],
    });

    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    return fixture;
  };

  describe.each(['enable_discord', 'enable_telegram'])('%s', key => {
    it('reads an absent row as on, as the sign-in check does', () => {
      expect(create({}).componentInstance.featureEnabled(meta(key))).toBe(true);
    });

    it('reads an explicit False as off', () => {
      expect(create({ [key]: 'False' }).componentInstance.featureEnabled(meta(key))).toBe(false);
    });

    it('reads an explicit True as on', () => {
      expect(create({ [key]: 'True' }).componentInstance.featureEnabled(meta(key))).toBe(true);
    });
  });

  it('still reads an absent opt-in switch as off', () => {
    // The legitimate-case half: enable_templates and enable_roles are off until written, server-side too.
    const sut = create({}).componentInstance;

    expect(sut.featureEnabled(meta('enable_templates'))).toBe(false);
    expect(sut.featureEnabled(meta('enable_roles'))).toBe(false);
  });

  it('delegation_poracle_channels is off when unset', () => {
    // UserRoleResolver reads an absent row as off, so the switch must too, or it promises access nobody has.
    expect(create({}).componentInstance.featureEnabled(meta('delegation_poracle_channels'))).toBe(false);
    expect(create({ delegation_poracle_channels: 'True' }).componentInstance.featureEnabled(meta('delegation_poracle_channels'))).toBe(
      true,
    );
  });

  it('still reads an absent disable_* switch as the feature on', () => {
    expect(create({}).componentInstance.featureEnabled(meta('disable_mons'))).toBe(true);
  });

  describe('search', () => {
    const noMatch = (el: HTMLElement) => el.querySelector('[data-test="search-no-match"]');

    it('says so when nothing matches', () => {
      const fixture = create({});
      fixture.componentInstance.searchQuery.set('zzqx-nothing');
      fixture.detectChanges();

      expect(noMatch(fixture.nativeElement)?.textContent).toContain('ADMIN_SETTINGS.SEARCH_NO_MATCH');
    });

    it('says nothing when something matches', () => {
      const fixture = create({});
      fixture.componentInstance.searchQuery.set('enable_discord');
      fixture.detectChanges();

      expect(noMatch(fixture.nativeElement)).toBeNull();
    });

    it('says nothing with no query', () => {
      expect(noMatch(create({}).nativeElement)).toBeNull();
    });
  });

  describe('unsaved changes', () => {
    it('reports none on a clean page', () => {
      expect(create({}).componentInstance.hasUnsavedChanges()).toBe(false);
    });

    it('reports a staged change', () => {
      const sut = create({}).componentInstance;
      sut.onTextChange('custom_title', 'Mine');

      expect(sut.hasUnsavedChanges()).toBe(true);
    });

    it('reports staged edits from the controls that are not switches or boxes', () => {
      // The icon pack list and the sign-in mode stage through the same map as every row.
      const repos = create({}).componentInstance;
      repos.restoreDefaultRepos();
      expect(repos.hasUnsavedChanges()).toBe(true);

      const mode = create({ enable_oidc: 'True' }).componentInstance;
      mode.setAuthMode('local');
      expect(mode.hasUnsavedChanges()).toBe(true);
    });

    it('reports none once a switch is turned off and back on, whatever case the row stored', () => {
      // The row says "false"; the switch writes "False". The same value both times, so nothing is unsaved.
      const sut = create({ disable_account_linking: 'false' }).componentInstance;
      sut.onFeatureToggle(meta('disable_account_linking'), false);
      expect(sut.hasUnsavedChanges()).toBe(true);
      sut.onFeatureToggle(meta('disable_account_linking'), true);

      expect(sut.hasUnsavedChanges()).toBe(false);
      expect(sut.groupModifiedCount(SETTING_GROUPS.find(g => g.labelKey === 'ADMIN_SETTINGS.GROUP_LINKED_ACCOUNTS')!)).toBe(0);
    });

    it('reports none once a switch with no row goes back to what absent means', () => {
      const sut = create({}).componentInstance;
      sut.onFeatureToggle(meta('disable_account_linking'), false);
      sut.onFeatureToggle(meta('disable_account_linking'), true);
      expect(sut.hasUnsavedChanges()).toBe(false);

      const signIn = create({}).componentInstance;
      signIn.onFeatureToggle(meta('enable_discord'), false);
      signIn.onFeatureToggle(meta('enable_discord'), true);
      expect(signIn.hasUnsavedChanges()).toBe(false);
    });

    it('still reports a text edited back to something else, and a switch left changed', () => {
      const sut = create({ custom_title: 'Mine', disable_account_linking: 'false' }).componentInstance;
      sut.onTextChange('custom_title', 'mine');
      expect(sut.hasUnsavedChanges()).toBe(true);
      sut.onTextChange('custom_title', 'Mine');
      expect(sut.hasUnsavedChanges()).toBe(false);

      sut.onFeatureToggle(meta('disable_account_linking'), false);
      expect(sut.hasUnsavedChanges()).toBe(true);
    });

    it('reports none once the change is saved', () => {
      const sut = create({}).componentInstance;
      sut.onTextChange('custom_title', 'Mine');
      sut.saveAllModified();

      expect(update).toHaveBeenCalledWith('custom_title', 'Mine');
      expect(sut.hasUnsavedChanges()).toBe(false);
    });

    it('reports none once the change is discarded', () => {
      const sut = create({}).componentInstance;
      sut.onTextChange('custom_title', 'Mine');
      sut.discardAllModified();

      expect(sut.hasUnsavedChanges()).toBe(false);
    });

    it('asks the browser to confirm a reload only while something is unsaved', () => {
      const sut = create({}).componentInstance;
      const clean = new Event('beforeunload', { cancelable: true });
      sut.onBeforeUnload(clean);
      expect(clean.defaultPrevented).toBe(false);

      sut.onTextChange('custom_title', 'Mine');
      const dirty = new Event('beforeunload', { cancelable: true });
      sut.onBeforeUnload(dirty);
      expect(dirty.defaultPrevented).toBe(true);
    });
  });
});

describe('AdminSettingsComponent group summaries', () => {
  const create = (values: Record<string, string>) => {
    TestBed.resetTestingModule();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideTranslateService(),
        {
          provide: SettingsService,
          useValue: {
            getOidcConfig: () => of(null),
            getAll: () => of(Object.entries(values).map(([key, value]) => ({ key, value }))),
            getDiscordConfig: () => of(null),
            getTelegramConfig: () => of(null),
            isForcedByPoracle: () => false,
            siteSettings: () => values,
            update: () => of({}),
          },
        },
      ],
      imports: [AdminSettingsComponent, NoopAnimationsModule],
    });
    const fixture = TestBed.createComponent(AdminSettingsComponent);
    fixture.detectChanges();
    const instant = jest.spyOn(TestBed.inject(I18nService), 'instant');
    return { instant, sut: fixture.componentInstance };
  };
  const group = (key: string) => SETTING_GROUPS.find(g => g.labelKey === key)!;

  it('counts the switches a group shows and leaves a dropdown beside them out of the total', () => {
    const { instant, sut } = create({ disable_account_linking: 'false', link_register_missing: 'always' });
    sut.groupSummary(group('ADMIN_SETTINGS.GROUP_LINKED_ACCOUNTS'));
    expect(instant).toHaveBeenLastCalledWith('ADMIN_SETTINGS.SUMMARY_ENABLED', { count: 1, total: 1 });
  });

  it('still counts the alarm types as before', () => {
    const { instant, sut } = create({ disable_raids: 'True' });
    sut.groupSummary(group('ADMIN_SETTINGS.GROUP_ALARM_TYPES'));
    const total = group('ADMIN_SETTINGS.GROUP_ALARM_TYPES').settings.length;
    expect(instant).toHaveBeenLastCalledWith('ADMIN_SETTINGS.SUMMARY_ENABLED', { count: total - 1, total });
  });

  it('gives no count to a group without feature switches', () => {
    const { sut } = create({});
    expect(sut.groupSummary(group('ADMIN_SETTINGS.GROUP_BRANDING'))).toBe('');
  });
});
