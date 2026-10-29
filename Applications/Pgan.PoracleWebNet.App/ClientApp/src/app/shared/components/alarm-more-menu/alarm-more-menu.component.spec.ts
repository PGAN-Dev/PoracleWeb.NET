import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { MatSnackBar } from '@angular/material/snack-bar';
import { provideTranslateService } from '@ngx-translate/core';

import { AlarmMoreMenuComponent, TestAlertType } from './alarm-more-menu.component';
import { AuthService } from '../../../core/services/auth.service';
import { ConfigService } from '../../../core/services/config.service';
import { TestAlertService } from '../../../core/services/test-alert.service';

/**
 * The paper plane sat beside Edit on every card and members pressed it by accident (#1097). The test now sits
 * behind a "More actions" button: a stray press opens a menu and sends nothing. `TestAlertService` keeps the
 * cooldown, the in-flight dedupe and the snackbars, so these specs check only what the menu adds.
 */
describe('AlarmMoreMenuComponent', () => {
  let fixture: ComponentFixture<AlarmMoreMenuComponent>;

  const root = () => fixture.nativeElement as HTMLElement;
  const trigger = () => root().querySelector('[data-test="alarm-more"]') as HTMLButtonElement;
  const item = () => document.querySelector('.mat-mdc-menu-panel [data-test="send-test"]') as HTMLButtonElement | null;
  const hint = () => document.querySelector('.mat-mdc-menu-panel [data-test="send-test-hint"]')?.textContent?.trim();
  const triggerIcon = () => trigger().querySelector('mat-icon') as HTMLElement;
  const itemIcon = () => item()?.querySelector('mat-icon') as HTMLElement;

  const open = () => {
    trigger().click();
    fixture.detectChanges();
  };

  afterEach(() => {
    document.querySelector('.cdk-overlay-container')?.replaceChildren();
  });

  describe('with a stand-in TestAlertService', () => {
    const sending = signal(new Set<string>());
    const cooling = signal(new Set<string>());
    const service = {
      isCoolingDown: jest.fn((type: string, uid: number) => cooling().has(`${type}:${uid}`)),
      isSending: jest.fn((type: string, uid: number) => sending().has(`${type}:${uid}`)),
      sendTestAlert: jest.fn(),
    };

    const build = (alarmType: TestAlertType = 'pokemon', uid = 7) => {
      fixture = TestBed.createComponent(AlarmMoreMenuComponent);
      fixture.componentRef.setInput('alarmType', alarmType);
      fixture.componentRef.setInput('uid', uid);
      fixture.detectChanges();
    };

    beforeEach(() => {
      sending.set(new Set());
      cooling.set(new Set());
      service.sendTestAlert.mockClear();
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [provideTranslateService(), { provide: TestAlertService, useValue: service }],
        imports: [AlarmMoreMenuComponent],
      });
    });

    it('is one named "More actions" button with a menu behind it, and sends nothing on its own', () => {
      build();

      expect(root().querySelectorAll('button')).toHaveLength(1);
      expect(trigger().getAttribute('aria-label')).toBe('COMMON.MORE_ACTIONS');
      expect(trigger().getAttribute('aria-haspopup')).toBe('menu');
      expect(triggerIcon().textContent?.trim()).toBe('more_vert');
      expect(item()).toBeNull();
      expect(service.sendTestAlert).not.toHaveBeenCalled();
    });

    it('opens to one item that names the action and says the result is a sample', () => {
      build();
      open();

      expect(item()).not.toBeNull();
      expect(item()?.querySelector('.more-item-label')?.textContent?.trim()).toBe('ALARM.TEST_SEND');
      expect(hint()).toBe('ALARM.TEST_SEND_HINT');
      expect(itemIcon().textContent?.trim()).toBe('send');
      expect(item()?.disabled).toBe(false);
      expect(service.sendTestAlert).not.toHaveBeenCalled();
    });

    it('sends the sample for the alarm it was given when the item is chosen', () => {
      build('pokemon', 7);
      open();

      item()?.click();

      expect(service.sendTestAlert).toHaveBeenCalledTimes(1);
      expect(service.sendTestAlert).toHaveBeenCalledWith('pokemon', 7);
    });

    it.each<[TestAlertType, number]>([
      ['raid', 3],
      ['egg', 12],
      ['quest', 21],
      ['invasion', 5],
      ['lure', 8],
      ['nest', 9],
      ['gym', 4],
    ])('sends %s %i with that type and that uid', (alarmType, uid) => {
      build(alarmType, uid);
      open();

      item()?.click();

      expect(service.sendTestAlert).toHaveBeenCalledWith(alarmType, uid);
    });

    it('closes the menu once the item is chosen', () => {
      build();
      open();

      item()?.click();
      fixture.detectChanges();

      expect(trigger().getAttribute('aria-expanded')).toBe('false');
    });

    it('disables the item during the cooldown and says so in the existing cooldown words', () => {
      cooling.set(new Set(['pokemon:7']));
      build('pokemon', 7);
      open();

      expect(item()?.disabled).toBe(true);
      expect(hint()).toBe('ALARM.TEST_COOLDOWN');
      item()?.click();
      expect(service.sendTestAlert).not.toHaveBeenCalled();
    });

    it('cools down one alarm without disabling its neighbours', () => {
      cooling.set(new Set(['pokemon:7']));
      build('pokemon', 8);
      open();

      expect(item()?.disabled).toBe(false);
      expect(hint()).toBe('ALARM.TEST_SEND_HINT');
    });

    it('treats a raid and an egg with the same uid as different alarms', () => {
      cooling.set(new Set(['raid:3']));
      build('egg', 3);
      open();

      expect(item()?.disabled).toBe(false);
    });

    it('turns the button into a spinner while the send is in flight, and back after', () => {
      build('pokemon', 7);
      expect(triggerIcon().textContent?.trim()).toBe('more_vert');
      expect(trigger().getAttribute('aria-busy')).toBeNull();

      sending.set(new Set(['pokemon:7']));
      fixture.detectChanges();

      expect(triggerIcon().textContent?.trim()).toBe('sync');
      expect(triggerIcon().classList.contains('spin')).toBe(true);
      expect(trigger().getAttribute('aria-busy')).toBe('true');
      expect(trigger().getAttribute('aria-label')).toBe('COMMON.MORE_ACTIONS');

      sending.set(new Set());
      fixture.detectChanges();

      expect(triggerIcon().textContent?.trim()).toBe('more_vert');
      expect(triggerIcon().classList.contains('spin')).toBe(false);
      expect(trigger().getAttribute('aria-busy')).toBeNull();
    });

    it('shows a spinner and a disabled item when the menu is opened mid-send', () => {
      sending.set(new Set(['pokemon:7']));
      build('pokemon', 7);
      open();

      expect(item()?.disabled).toBe(true);
      expect(itemIcon().textContent?.trim()).toBe('sync');
      expect(itemIcon().classList.contains('spin')).toBe(true);
      item()?.click();
      expect(service.sendTestAlert).not.toHaveBeenCalled();
    });
  });

  /** The real service, so the cooldown and the spinner are the ones a member gets rather than a stand-in's. */
  describe('with the real TestAlertService', () => {
    const API = 'http://test-api';
    let httpMock: HttpTestingController;
    let snackBar: { open: jest.Mock };

    beforeEach(() => {
      jest.useFakeTimers();
      snackBar = { open: jest.fn() };
      TestBed.resetTestingModule();
      TestBed.configureTestingModule({
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideTranslateService(),
          { provide: ConfigService, useValue: { apiHost: API } },
          { provide: MatSnackBar, useValue: snackBar },
          { provide: AuthService, useValue: { isImpersonating: signal(false), user: () => ({ type: 'discord:user' }) } },
        ],
        imports: [AlarmMoreMenuComponent],
      });
      httpMock = TestBed.inject(HttpTestingController);
      fixture = TestBed.createComponent(AlarmMoreMenuComponent);
      fixture.componentRef.setInput('alarmType', 'quest');
      fixture.componentRef.setInput('uid', 5);
      fixture.detectChanges();
    });

    afterEach(() => {
      jest.clearAllTimers();
      jest.useRealTimers();
    });

    it('posts the test for this alarm, spins while it is out, then holds the item through the cooldown', () => {
      open();
      item()?.click();
      fixture.detectChanges();

      const req = httpMock.expectOne(`${API}/api/test-alert/quest/5`);
      expect(req.request.method).toBe('POST');
      expect(triggerIcon().classList.contains('spin')).toBe(true);

      req.flush({ message: 'sent', status: 'ok' });
      fixture.detectChanges();

      expect(triggerIcon().classList.contains('spin')).toBe(false);
      expect(snackBar.open).toHaveBeenCalledTimes(1);

      open();
      expect(item()?.disabled).toBe(true);
      expect(hint()).toBe('ALARM.TEST_COOLDOWN');

      // The menu is still open when the cooldown runs out: the item frees itself without a reopen.
      jest.advanceTimersByTime(15_000);
      fixture.detectChanges();

      expect(item()?.disabled).toBe(false);
      expect(hint()).toBe('ALARM.TEST_SEND_HINT');
      httpMock.verify();
    });

    it('sends nothing until the item is chosen', () => {
      open();

      httpMock.expectNone(`${API}/api/test-alert/quest/5`);
      httpMock.verify();
    });
  });
});
