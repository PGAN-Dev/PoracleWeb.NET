import { provideHttpClient } from '@angular/common/http';
import { provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { NoopAnimationsModule } from '@angular/platform-browser/animations';
import { provideTranslateService } from '@ngx-translate/core';
import { Observable, of } from 'rxjs';

import { AdminSettingsComponent, SETTING_GROUPS } from './admin-settings.component';
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
